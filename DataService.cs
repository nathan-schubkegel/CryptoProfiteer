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

    // 'taxAssociationId' may be null or empty string to add the order to a new tax association
    void UpdateTaxAssociation(string taxAssociationId, string orderId, Decimal coinCount, Decimal partCost);
    
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
    
    private IReadOnlyDictionary<string, TaxAssociation> ImportTaxAssociations(IEnumerable<PersistedTaxAssociation> importedTaxAssociations, IReadOnlyDictionary<string, Order> orders)
    {
      // combine all persisted data into 1 collection
      var combinedData = new Dictionary<string, PersistedTaxAssociation>();
      foreach ((var id, var a) in TaxAssociations)
      {
        combinedData[a.Id] = a.GetPersistedData();
      }
      foreach (var a in importedTaxAssociations)
      {
        combinedData[a.Id] = a ?? throw new Exception("invalid null tax association input");
      }
      
      // remove tax association parts for orders that no longer exist for whatever reason.
      // remove tax associations that are empty.
      var taxAssociationIdsToRemove = new HashSet<string>();
      var orderIdsToRemove = new HashSet<string>();
      foreach (var t in combinedData.Values)
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
        combinedData.Remove(id);
      }

      // record which orders are double-booked
      var orderIdToTaxAssociations = new Dictionary<string, List<PersistedTaxAssociation>>();
      foreach (var t in combinedData.Values)
      {
        foreach (var p in t.Parts)
        {
          if (!orderIdToTaxAssociations.TryGetValue(p.OrderId, out var bucket))
          {
            bucket = new List<PersistedTaxAssociation>(1);
            orderIdToTaxAssociations[p.OrderId] = bucket;
          }
          bucket.Add(t);
        }
      }
      
      // TODO: how to complain about 0.01 rounding errors?

      // produce public-facing objects
      var newTaxAssociations = combinedData.Values.ToDictionary(
        t => t.Id, 
        t => new TaxAssociation(t, orders /* TODO pass this in t.Parts.Select(p => p.OrderId).Where(orderIdToTaxAssociations.shows.it.double.booked)*/);

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
    
    public void UpdateTaxAssociation(string taxAssociationId, string orderId, Decimal coinCount, Decimal partCost)
    {
      lock (_lock)
      {
        // find order
        orderId = orderId ?? string.Empty;
        if (!Orders.TryGetValue(orderId, out var order))
        {
          throw new Exception("Cannot add unrecognized order id \"" + orderId + "\" to tax association");
        }
        
        // find or create tax association
        taxAssociationId ??= string.Empty;
        PersistedTaxAssociation data;
        if (taxAssociationId == "")
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
        
        // add or update part in tax association
        int i = data.Parts.FindIndex(x => x.OrderId == orderId);
        var newPart = new PersistedTaxAssociationPart
        {
          OrderId = orderId,
          CoinCount = coinCount,
          PartCost = partCost,
        };
        var newData = new PersistedTaxAssociation
        {
          Id = data.Id,
          Parts = i < 0 
            ? data.Parts.Concat(new[]{newPart}).ToList() // add
            : data.Parts.Select((x, i2) => i == i2 ? newPart : x).ToList(), // replace
        };
        ImportTaxAssociations(new[]{newData});
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