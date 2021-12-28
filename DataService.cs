using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace CryptoProfiteer
{
  public interface IDataService
  {
    IReadOnlyDictionary<string, Transaction> Transactions { get; }
    IReadOnlyDictionary<string, Order> Orders { get; }
    IReadOnlyDictionary<string, CoinSummary> CoinSummaries { get; }
    IReadOnlyDictionary<string, CoinPrice> CoinPrices { get; }
    IReadOnlyDictionary<string, TaxAssociation> TaxAssociations { get; }

    void ImportTransactions(IEnumerable<PersistedTransaction> transactions);
    void ImportTaxAssociations(IEnumerable<PersistedTaxAssociation> importedTaxAssociations);
    void UpdateCoinPrices(IEnumerable<CoinPriceFromExchange> prices);
    void UpdateFriendlyNames(Dictionary<string, string> newFriendlyNames);

    // 'taxAssociationId' may be null or empty string to add order to a new tax association
    // Returns the new/modified TaxAssociation ID
    string UpdateTaxAssociation(string taxAssociationId,
      (string orderId, Decimal contributingCoinCount, Decimal contributingCost)[] updates);
    
    (IEnumerable<PersistedTransaction> Transactions, IEnumerable<PersistedTaxAssociation> TaxAssociations) GetPersistedData();
  }

  public class DataService : IDataService
  {
    private readonly object _lock = new object();
    private readonly ILogger<DataService> _logger;
    private readonly Dictionary<string, FriendlyName> _friendlyNames = new Dictionary<string, FriendlyName>();

    public IReadOnlyDictionary<string, Transaction> Transactions { get; private set; } = new Dictionary<string, Transaction>();
    public IReadOnlyDictionary<string, Order> Orders { get; private set; } = new Dictionary<string, Order>();
    public IReadOnlyDictionary<string, CoinSummary> CoinSummaries { get; private set; } = new Dictionary<string, CoinSummary>();
    public IReadOnlyDictionary<string, CoinPrice> CoinPrices { get; private set; } = new Dictionary<string, CoinPrice>();
    public IReadOnlyDictionary<string, TaxAssociation> TaxAssociations { get; private set; } = new Dictionary<string, TaxAssociation>();
    
    public DataService(ILogger<DataService> logger)
    {
      _logger = logger;
    }
    
    private FriendlyName GetOrCreateFriendlyName(string coinType)
    {
      // NOTE: assuming caller has lock held
      if (!_friendlyNames.TryGetValue(coinType, out var friendlyName))
      {
        friendlyName = new FriendlyName { Value = coinType };
        _friendlyNames[coinType] = friendlyName;
      }
      return friendlyName;
    }
    
    public void UpdateFriendlyNames(Dictionary<string, string> newFriendlyNames)
    {
      lock (_lock)
      {
        foreach ((var coinType, var friendlyName) in newFriendlyNames)
        {
          GetOrCreateFriendlyName(coinType).Value = friendlyName;
        }
      }
    }
    
    public void ImportTransactions(IEnumerable<PersistedTransaction> importedTransactions)
    {
      lock (_lock)
      {
        var newTransactions = new Dictionary<string, Transaction>(Transactions);
        foreach (var t in importedTransactions)
        {
          newTransactions[t.TradeId] = new Transaction(
            t ?? throw new Exception("invalid null transaction"),
            GetOrCreateFriendlyName(t.CoinType));
        }

        var newOrders = BuildOrders(newTransactions);
        var newCoinSummaries = BuildCoinSummaries(newTransactions, CoinPrices);
        var newTaxAssociations = ImportTaxAssociations(new List<PersistedTaxAssociation>(0), newOrders);
        
        Transactions = newTransactions;
        Orders = newOrders;
        CoinSummaries = newCoinSummaries;
        TaxAssociations = newTaxAssociations;
      }
    }

    private Dictionary<string, Order> BuildOrders(
      IReadOnlyDictionary<string, Transaction> transactions)
    {
      var newOrders = new Dictionary<string, Order>();
      var orderTransactions = new List<Transaction>();
      foreach (var t in transactions.Values.OrderBy(x => x.Time))
      {
        if (orderTransactions.Count == 0)
        {
          orderTransactions.Add(t);
          continue;
        }
        
        var difference = orderTransactions[0].Time - t.Time;
        if (difference < TimeSpan.FromSeconds(1) && difference > TimeSpan.FromSeconds(-1))
        {
          orderTransactions.Add(t);
          continue;
        }
        
        var order = new Order(orderTransactions, GetOrCreateFriendlyName(orderTransactions[0].CoinType));
        newOrders[order.Id] = order;
        orderTransactions.Clear();
        orderTransactions.Add(t);
      }
      if (orderTransactions.Count > 0)
      {
        var order = new Order(orderTransactions, GetOrCreateFriendlyName(orderTransactions[0].CoinType));
        newOrders[order.Id] = order;
      }
      return newOrders;
    }
    
    private Dictionary<string, CoinSummary> BuildCoinSummaries(
      IReadOnlyDictionary<string, Transaction> transactions,
      IReadOnlyDictionary<string, CoinPrice> coinPrices)
    {
      return transactions.Values.GroupBy(x => x.CoinType).ToDictionary(
        x => x.Key,
        x => new CoinSummary(
          coinType: x.Key,
          friendlyName: GetOrCreateFriendlyName(x.Key),
          coinCount: x.Where(y => y.TransactionType == TransactionType.Buy).Select(y => y.CoinCount).Sum()
            - x.Where(y => y.TransactionType == TransactionType.Sell).Select(y => y.CoinCount).Sum(),
          coinPrice: coinPrices.GetValueOrDefault(x.Key))
      );
    }
    
    public void ImportTaxAssociations(IEnumerable<PersistedTaxAssociation> importedTaxAssociations)
    {
      lock (_lock)
      {
        var newTaxAssociations = ImportTaxAssociations(importedTaxAssociations, Orders);
        TaxAssociations = newTaxAssociations;
      }
    }
    
    private IReadOnlyDictionary<string, TaxAssociation> ImportTaxAssociations(
      IEnumerable<PersistedTaxAssociation> importedTaxAssociations, 
      IReadOnlyDictionary<string, Order> orders)
    {
      // combine all existing and proposed tax association data into 1 collection
      var persistedTaxAssociations = new Dictionary<string, PersistedTaxAssociation>();
      foreach ((var id, var a) in TaxAssociations)
      {
        persistedTaxAssociations[a.Id] = a.GetPersistedData();
      }
      foreach (var a in importedTaxAssociations)
      {
        persistedTaxAssociations[a.Id] = a ?? throw new Exception("invalid null tax association input");
      }
      
      // remove tax association parts for orders that no longer exist for whatever reason.
      // remove tax associations that are empty.
      var taxAssociationIdsToRemove = new HashSet<string>();
      var orderIdsToRemove = new HashSet<string>();
      foreach (var t in persistedTaxAssociations.Values)
      {
        orderIdsToRemove.Clear();
        foreach (var p in t.Parts)
        {
          if (!orders.TryGetValue(p.OrderId, out var order))
          {
            orderIdsToRemove.Add(p.OrderId);
          }
        }
        if (orderIdsToRemove.Count > 0)
        {
          t.Parts = t.Parts.Where(x => !orderIdsToRemove.Contains(x.OrderId)).ToList();
        }
        if (t.Parts.Count == 0)
        {
          taxAssociationIdsToRemove.Add(t.Id);
        }
      }
      foreach (var id in taxAssociationIdsToRemove)
      {
        persistedTaxAssociations.Remove(id);
      }

      // produce public-facing objects
      var newTaxAssociations = persistedTaxAssociations.Values.ToDictionary(
        t => t.Id, 
        t => new TaxAssociation(t, orders));

      return newTaxAssociations;
    }
    
    public void UpdateCoinPrices(IEnumerable<CoinPriceFromExchange> prices)
    {
      lock (_lock)
      {
        int newCount = 0;
        var newPrices = new Dictionary<string, CoinPrice>(CoinPrices);
        var now = DateTime.Now;
        foreach (var price in prices)
        {
          newPrices[price.CoinType] = new CoinPrice(price, GetOrCreateFriendlyName(price.CoinType), now);
          newCount++;
        }
        
        var newCoinSummaries = BuildCoinSummaries(Transactions, newPrices);
        
        CoinPrices = newPrices;
        CoinSummaries = newCoinSummaries;
      }
    }
    
    public string UpdateTaxAssociation(string taxAssociationId, 
      (string orderId, Decimal contributingCoinCount, Decimal contributingCost)[] updates)
    {
      lock (_lock)
      {
        // find or create tax association
        taxAssociationId ??= string.Empty;
        PersistedTaxAssociation data;
        if (string.IsNullOrEmpty(taxAssociationId))
        {
          data = new PersistedTaxAssociation
          {
            Id = Guid.NewGuid().ToString(),
            Parts = new List<PersistedTaxAssociationPart>(0),
          };
        }
        else
        {
          if (!TaxAssociations.TryGetValue(taxAssociationId, out var thing))
          {
            throw new Exception("Cannot add to unrecognized TaxAssociation id \"" + taxAssociationId + "\"");
          }
          data = thing.GetPersistedData();
        }

        var revisedParts = new List<PersistedTaxAssociationPart>(data.Parts);
        foreach ((var orderId, var contributingCoinCount, var contributingCost) in updates)
        {
          // find order
          if (!Orders.TryGetValue(orderId ?? string.Empty, out var order))
          {
            throw new Exception("Cannot add unrecognized order id \"" + orderId + "\" to tax association");
          }

          // make new part data
          var newPart = new PersistedTaxAssociationPart
          {
            OrderId = orderId,
            ContributingCoinCount = contributingCoinCount,
            ContributingCost = contributingCost,
          };

          // add or replace part in list
          int i = revisedParts.FindIndex(x => x.OrderId == orderId);
          if (i < 0)
          {
            revisedParts.Add(newPart);
          }
          else
          {
            revisedParts[i] = newPart;
          }
        }
        
        var newData = new PersistedTaxAssociation { Id = data.Id, Parts = revisedParts };
        ImportTaxAssociations(new[] { newData });
        return newData.Id;
      }
    }
    
    public (IEnumerable<PersistedTransaction> Transactions, IEnumerable<PersistedTaxAssociation> TaxAssociations) GetPersistedData()
    {
      lock (_lock)
      {
        return (
          Transactions.Values.Select(x => x.GetPersistedData()), 
          TaxAssociations.Values.Select(x => x.GetPersistedData())
        );
      }
    }
  }
}