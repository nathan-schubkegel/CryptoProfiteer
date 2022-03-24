using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace CryptoProfiteer
{
  public interface IDataService
  {
    IReadOnlyDictionary<string, Transaction> Transactions { get; }
    IReadOnlyDictionary<string, Order> Orders { get; }
    IReadOnlyDictionary<string, CoinSummary> CoinSummaries { get; }
    IReadOnlyDictionary<string, TaxAssociation> TaxAssociations { get; }

    void ImportTransactions(IEnumerable<PersistedTransaction> transactions);
    void ImportTaxAssociations(IEnumerable<PersistedTaxAssociation> importedTaxAssociations);

    // 'taxAssociationId' may be null/empty when creating a new tax association
    // 'saleOrderId' may be null/empty when modifying an existing tax association to not change that aspect
    // Returns the new/modified TaxAssociation ID
    string UpdateTaxAssociation(string taxAssociationId, string saleOrderId,
      (string orderId, Decimal contributingCoinCount, int contributingCost)[] purchaseOrderUpdates);

    void DeleteTaxAssociation(string taxAssociationId);
    string AddAdjustment(string coinType, decimal coinCount);
    void DeleteAdjustment(string transactionId);

    (IEnumerable<PersistedTransaction> Transactions, IEnumerable<PersistedTaxAssociation> TaxAssociations) GetPersistedData();
  }

  public class DataService : IDataService
  {
    private readonly object _lock = new object();
    private readonly ILogger<DataService> _logger;
    private readonly IFriendlyNameService _friendlyNameService;
    private readonly IHistoricalCoinPriceService _historicalCoinPriceService;
    private readonly IPriceService _priceService;

    public IReadOnlyDictionary<string, Transaction> Transactions { get; private set; } = new Dictionary<string, Transaction>();
    public IReadOnlyDictionary<string, Order> Orders { get; private set; } = new Dictionary<string, Order>();
    public IReadOnlyDictionary<string, CoinSummary> CoinSummaries { get; private set; } = new Dictionary<string, CoinSummary>();
    public IReadOnlyDictionary<string, TaxAssociation> TaxAssociations { get; private set; } = new Dictionary<string, TaxAssociation>();
    
    public DataService(
      ILogger<DataService> logger,
      IHistoricalCoinPriceService historicalCoinPriceService,
      IFriendlyNameService friendlyNameService,
      IPriceService priceService)
    {
      _logger = logger;
      _historicalCoinPriceService = historicalCoinPriceService;
      _friendlyNameService = friendlyNameService;
      _priceService = priceService;
      _priceService.CoinPricesUpdated += OnCoinPricesUpdated;
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
            _friendlyNameService.GetOrCreateFriendlyName(t.CoinType),
            _historicalCoinPriceService);
        }

        var newOrders = BuildOrders(newTransactions);
        var newCoinSummaries = BuildCoinSummaries(newTransactions);
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
      
      // kucoin transactions are aggregated perfectly by order id; so group those with zeal
      foreach (var kGroup in transactions.Values.Where(x => x.OrderAggregationId != null).GroupBy(x => x.OrderAggregationId))
      {
        var order = new Order(kGroup.ToList(), _friendlyNameService.GetOrCreateFriendlyName(kGroup.First().CoinType));
        newOrders[order.Id] = order;
      }

      foreach (var tGroup in transactions.Values.Where(x => x.OrderAggregationId == null).GroupBy(x => (x.CoinType, x.PaymentCoinType, x.Exchange, x.TransactionType)))
      {
        foreach (var t in tGroup.OrderBy(x => x.Time))
        {
          if (orderTransactions.Count == 0)
          {
            orderTransactions.Add(t);
            continue;
          }
          
          // consider fills within the same second to be part of the same order
          // (this won't be 100% reliable when I start doing bot trading... but it's true enough for a single human doing purchases)
          var difference = orderTransactions[0].Time - t.Time;
          if (difference < TimeSpan.FromSeconds(1) && difference > TimeSpan.FromSeconds(-1))
          {
            orderTransactions.Add(t);
            continue;
          }
          
          var order = new Order(orderTransactions, _friendlyNameService.GetOrCreateFriendlyName(orderTransactions[0].CoinType));
          newOrders[order.Id] = order;
          orderTransactions.Clear();
          orderTransactions.Add(t);
        }
        if (orderTransactions.Count > 0)
        {
          var order = new Order(orderTransactions, _friendlyNameService.GetOrCreateFriendlyName(orderTransactions[0].CoinType));
          newOrders[order.Id] = order;
          orderTransactions.Clear();
        }
      }

      return newOrders;
    }
    
    private Dictionary<string, CoinSummary> BuildCoinSummaries(
      IReadOnlyDictionary<string, Transaction> transactions)
    {
      // tally how much of each coin is held based on transactions
      var coinCounts = new Dictionary<string, Decimal>();
      foreach (var t in transactions.Values)
      {
        // account for coin type
        var coins = coinCounts.GetValueOrDefault(t.CoinType, 0m);
        switch (t.TransactionType)
        {
          case TransactionType.Sell: coins -= Math.Abs(t.CoinCount); break;
          case TransactionType.Buy: coins += Math.Abs(t.CoinCount); break;
          case TransactionType.Adjustment: coins += t.CoinCount; break;
          default: throw new Exception("Unrecognized transaction type" + t.TransactionType);
        }
        coinCounts[t.CoinType] = coins;
        
        // account for payment coin type
        coins = coinCounts.GetValueOrDefault(t.PaymentCoinType, 0m);
        switch (t.TransactionType)
        {
          case TransactionType.Sell: coins += Math.Abs(t.TotalCost); break;
          case TransactionType.Buy: coins -= Math.Abs(t.TotalCost); break;
          case TransactionType.Adjustment: throw new Exception("adjustments are supposed to be always \"paid\" in USD");
          default: throw new Exception("Unrecognized transaction type " + t.TransactionType);
        }
        coinCounts[t.PaymentCoinType] = coins;
      }
      
      // return a CoinSummary for each
      return coinCounts.ToDictionary(
        x => x.Key,
        x => new CoinSummary(
          coinType: x.Key,
          friendlyName: _friendlyNameService.GetOrCreateFriendlyName(x.Key),
          coinCount: x.Value,
          coinPrice: _priceService.TryGetCoinPrice(x.Key))
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

      // remove tax association data for orders that no longer exist for whatever reason.
      // remove tax associations that are empty.
      var taxAssociationIdsToRemove = new HashSet<string>();
      var orderIdsToRemove = new HashSet<string>();
      foreach (var t in persistedTaxAssociations.Values)
      {
        orderIdsToRemove.Clear();
        foreach (var p in t.Purchases)
        {
          if (!orders.ContainsKey(p.OrderId))
          {
            orderIdsToRemove.Add(p.OrderId);
          }
        }
        if (orderIdsToRemove.Count > 0)
        {
          // TODO: this would feel like a "changed some state that was supposed to be immutable" fault
          //t.Purchases = t.Purchases.Where(x => !orderIdsToRemove.Contains(x.OrderId)).ToList();
          
          // so... I'm going with the safer but more drastic move "just gonna drop the tax association"
          taxAssociationIdsToRemove.Add(t.Id);
        }
        if (!orders.ContainsKey(t.SaleOrderId) ||
            t.Purchases.Count == 0)
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

    private void OnCoinPricesUpdated()
    {
      lock (_lock)
      {
        var newCoinSummaries = BuildCoinSummaries(Transactions);
        CoinSummaries = newCoinSummaries;
      }
    }
    
    public string UpdateTaxAssociation(string taxAssociationId, string saleOrderId,
      (string orderId, Decimal contributingCoinCount, int contributingCost)[] purchaseOrderUpdates)
    {
      lock (_lock)
      {
        // find or create tax association
        taxAssociationId ??= string.Empty;
        PersistedTaxAssociation data;
        if (string.IsNullOrEmpty(taxAssociationId))
        {
          if (!Orders.ContainsKey(saleOrderId ?? string.Empty))
          {
            throw new Exception("saleOrderId is empty or refers to unrecognized order, but it is required when creating a new tax association");
          }
          
          var existingTaxAssociation = TaxAssociations.Values.FirstOrDefault(t => t.Sale.Order.Id == saleOrderId);
          if (existingTaxAssociation != null)
          {
            throw new Exception("saleOrderId \"" + saleOrderId + "\" is already tax-associated, see tax association \"" + existingTaxAssociation.Id + "\"");
          }

          data = new PersistedTaxAssociation
          {
            Id = Guid.NewGuid().ToString(),
            SaleOrderId = saleOrderId,
            Purchases = new List<PersistedTaxAssociationPurchase>(0),
          };
        }
        else
        {
          if (!TaxAssociations.TryGetValue(taxAssociationId, out var thing))
          {
            throw new Exception("Cannot add to unrecognized TaxAssociation id \"" + taxAssociationId + "\"");
          }
          data = thing.GetPersistedData();
          
          if (string.IsNullOrEmpty(saleOrderId))
          {
            saleOrderId = data.SaleOrderId;
          }
          else if (!Orders.ContainsKey(saleOrderId))
          {
            throw new Exception("SaleOrderId refers to unrecognized order");
          }

          var existingTaxAssociation = TaxAssociations.Values.FirstOrDefault(t => t.Sale.Order.Id == saleOrderId);
          if (existingTaxAssociation != thing)
          {
            throw new Exception("saleOrderId \"" + saleOrderId + "\" is already tax-associated, see tax association \"" + existingTaxAssociation.Id + "\"");
          }
        }

        var revisedPurchases = new List<PersistedTaxAssociationPurchase>(data.Purchases);
        foreach ((var orderId, var contributingCoinCount, var contributingCost) in purchaseOrderUpdates)
        {
          // find order
          if (!Orders.TryGetValue(orderId ?? string.Empty, out var order))
          {
            throw new Exception("Cannot add unrecognized purchase order id \"" + orderId + "\" to tax association");
          }
          
          if (order.TransactionType != TransactionType.Buy)
          {
            throw new Exception("cannot add 'sell' order id \"" + orderId + "\" to tax association purchase orders");
          }
          
          // make new purchase data
          var newPurchase = new PersistedTaxAssociationPurchase
          {
            OrderId = orderId,
            // fix polarity because it's easier here than in the front-end
            ContributingCoinCount = Math.Abs(contributingCoinCount),
            ContributingCost = -Math.Abs(contributingCost),
          };

          // add or replace purchase in list
          int i = revisedPurchases.FindIndex(x => x.OrderId == orderId);
          if (i < 0)
          {
            revisedPurchases.Add(newPurchase);
          }
          else
          {
            revisedPurchases[i] = newPurchase;
          }
        }

        var newData = new PersistedTaxAssociation
        {
          Id = data.Id,
          Purchases = revisedPurchases,
          SaleOrderId = saleOrderId,
        };
        ImportTaxAssociations(new[] { newData });
        return newData.Id;
      }
    }
    
    public void DeleteTaxAssociation(string taxAssociationId)
    {
      lock (_lock)
      {
        taxAssociationId ??= string.Empty;
        if (!TaxAssociations.TryGetValue(taxAssociationId, out var thing))
        {
          throw new Exception("Cannot delete unrecognized TaxAssociation id \"" + taxAssociationId + "\"");
        }
        
        TaxAssociations = TaxAssociations.Values.Where(a => a.Id != taxAssociationId)
          .ToDictionary(a => a.Id, a => a);
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
    
    public string AddAdjustment(string coinType, decimal coinCount)
    {
      lock (_lock)
      {
        if (!Transactions.Values.Any(x => x.CoinType == coinType))
        {
          throw new Exception("Unrecognized coin type; you can only adjust coins that you already own.");
        }
        
        var id = "adjustment-" + Guid.NewGuid().ToString();
        ImportTransactions(new [] { new PersistedTransaction
        {
          TradeId = id,
          TransactionType = TransactionType.Adjustment,
          Exchange = CryptoExchange.None,
          Time = DateTime.UtcNow,
          CoinType = coinType,
          PaymentCoinType = "USD",
          CoinCount = coinCount,
          PerCoinCost = 0m,
          Fee = 0m,
          TotalCost = 0m,
        }});
        return id;
      }
    }

    public void DeleteAdjustment(string tradeId)
    {
      lock (_lock)
      {
        var oldTransactions = Transactions;
        var newTransactions = new Dictionary<string, Transaction>(Transactions.Where(
          kvp => !(kvp.Key == tradeId && kvp.Value.TransactionType == TransactionType.Adjustment)));
        try
        {
          Transactions = newTransactions;
          ImportTransactions(new PersistedTransaction[0]);
        }
        catch
        {
          Transactions = oldTransactions;
          throw;
        }
      }
    }
  }
}