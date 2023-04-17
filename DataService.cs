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
    IEnumerable<string> CoinTypes { get; }
    IReadOnlyDictionary<string, Transaction> Transactions { get; }
    IReadOnlyDictionary<string, Order> Orders { get; }
    IReadOnlyDictionary<string, CoinSummary> CoinSummaries { get; }
    IReadOnlyDictionary<string, TaxAssociation> TaxAssociations { get; }

    void ImportTransactions(IEnumerable<PersistedTransaction> transactions);
    void ImportTaxAssociations(IEnumerable<PersistedTaxAssociation> importedTaxAssociations);

    // 'taxAssociationId' may be null/empty when creating a new tax association
    // 'saleOrderId' may be null/empty when modifying an existing tax association to not change that aspect
    // 'purchaseOrderUpdates' may be null when pinning adjustments
    // 'purchaseIdToPinSaleProceedsFudge' may be null to not do that
    // Returns the new/modified TaxAssociation ID
    string UpdateTaxAssociation(string taxAssociationId, string saleOrderId,
      (string orderId, Decimal contributingCoinCount)[] purchaseOrderUpdates,
      string purchaseIdToPinSaleProceedsFudge);

    void DeleteTaxAssociation(string taxAssociationId);
    string AddAdjustment(string coinType, decimal coinCount);
    void DeleteAdjustment(string transactionId);

    (IEnumerable<PersistedTransaction> Transactions, IEnumerable<PersistedTaxAssociation> TaxAssociations) ClonePersistedData();
  }

  public class DataService : IDataService
  {
    private readonly object _lock = new object();
    private readonly ILogger<DataService> _logger;
    private readonly IFriendlyNameService _friendlyNameService;
    private readonly IPriceService _priceService;
    private readonly Services _services;

    public IEnumerable<string> CoinTypes { get; private set; } = Enumerable.Empty<string>();
    public IReadOnlyDictionary<string, Transaction> Transactions { get; private set; } = new Dictionary<string, Transaction>();
    public IReadOnlyDictionary<string, Order> Orders { get; private set; } = new Dictionary<string, Order>();
    public IReadOnlyDictionary<string, CoinSummary> CoinSummaries { get; private set; } = new Dictionary<string, CoinSummary>();
    public IReadOnlyDictionary<string, TaxAssociation> TaxAssociations { get; private set; } = new Dictionary<string, TaxAssociation>();
    
    public DataService(
      ILogger<DataService> logger,
      IFriendlyNameService friendlyNameService,
      IPriceService priceService,
      Services services)
    {
      _logger = logger;
      _friendlyNameService = friendlyNameService;
      _priceService = priceService;
      _priceService.CoinPricesUpdated += OnCoinPricesUpdated;
      _services = services;
    }

    public void ImportTransactions(IEnumerable<PersistedTransaction> importedTransactions)
    {
      lock (_lock)
      {
        var newTransactions = new Dictionary<string, Transaction>(Transactions);
        foreach (var t in importedTransactions)
        {
          newTransactions[t.Id] = new Transaction(
            t ?? throw new Exception("invalid null transaction"),
            _services);
        }

        var (newOrders, newCoinTypes) = BuildOrdersAndCoinTypes(newTransactions);
        var newCoinSummaries = BuildCoinSummaries(newTransactions);
        var newTaxAssociations = ImportTaxAssociations(new List<PersistedTaxAssociation>(0), newOrders);
        
        CoinTypes = newCoinTypes;
        Transactions = newTransactions;
        Orders = newOrders;
        CoinSummaries = newCoinSummaries;
        TaxAssociations = newTaxAssociations;
      }
    }

    private (Dictionary<string, Order>, HashSet<string>) BuildOrdersAndCoinTypes(
      IReadOnlyDictionary<string, Transaction> transactions)
    {
      var coinTypes = new HashSet<string>();
      var newOrders = new Dictionary<string, Order>();
      var orderTransactions = new List<Transaction>();
      
      // some transactions (like from kucoin) are aggregated perfectly by order id; so group those with zeal
      foreach (var kGroup in transactions.Values.Where(x => x.OrderAggregationId != null)
        .GroupBy(x => (x.PaymentCoinType, x.ReceivedCoinType, x.Exchange, x.TransactionType, x.OrderAggregationId)))
      {
        var order = new Order(kGroup.ToList(), _services);
        newOrders[order.Id] = order;
        coinTypes.Add(order.PaymentCoinType);
        coinTypes.Add(order.ReceivedCoinType);
      }

      // some transactions (like fills from coinbase pro without associated account statements) have no order id
      foreach (var tGroup in transactions.Values.Where(x => x.OrderAggregationId == null)
        .GroupBy(x => (x.PaymentCoinType, x.ReceivedCoinType, x.Exchange, x.TransactionType, x.OrderAggregationId)))
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
          
          var order = new Order(orderTransactions, _services);
          newOrders[order.Id] = order;
          coinTypes.Add(order.PaymentCoinType);
          coinTypes.Add(order.ReceivedCoinType);

          orderTransactions.Clear();
          orderTransactions.Add(t);
        }
        if (orderTransactions.Count > 0)
        {
          var order = new Order(orderTransactions, _services);
          newOrders[order.Id] = order;
          coinTypes.Add(order.PaymentCoinType);
          coinTypes.Add(order.ReceivedCoinType);

          orderTransactions.Clear();
        }
      }

      return (newOrders, coinTypes);
    }
    
    private Dictionary<string, CoinSummary> BuildCoinSummaries(
      IReadOnlyDictionary<string, Transaction> transactions)
    {
      // tally how much of each coin is held based on transactions
      var coinCounts = new Dictionary<string, Decimal>();
      foreach (var t in transactions.Values)
      {
        // account for received coin type
        var coins = coinCounts.GetValueOrDefault(t.ReceivedCoinType, 0m);
        coins += t.ReceivedCoinCount;
        coinCounts[t.ReceivedCoinType] = coins;
        
        // account for payment coin type
        coins = coinCounts.GetValueOrDefault(t.PaymentCoinType, 0m);
        coins -= t.PaymentCoinCount;
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
        persistedTaxAssociations[a.Id] = a.ClonePersistedData();
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

    // 'taxAssociationId' may be null/empty when creating a new tax association
    // 'saleOrderId' may be null/empty when modifying an existing tax association to not change that aspect
    // 'purchaseOrderUpdates' may be null when pinning adjustments
    // 'purchaseIdToPinSaleProceedsFudge' may be null to not do that
    // Returns the new/modified TaxAssociation ID
    public string UpdateTaxAssociation(string taxAssociationId, string saleOrderId,
      (string orderId, Decimal contributingCoinCount)[] purchaseOrderUpdates,
      string purchaseIdToPinSaleProceedsFudge)
    {
      lock (_lock)
      {
        // find or create tax association
        taxAssociationId ??= string.Empty;
        PersistedTaxAssociation data;
        if (string.IsNullOrEmpty(taxAssociationId))
        {
          if (!Orders.TryGetValue(saleOrderId ?? string.Empty, out var saleOrder))
          {
            throw new Exception("saleOrderId is empty or refers to unrecognized order, but it is required when creating a new tax association");
          }
          
          if (!saleOrder.IsTaxableSale)
          {
            throw new Exception($"cannot add order id \"{saleOrderId}\" as tax association sale order because it does not satisfy {nameof(Order)}.{nameof(Order.IsTaxableSale)}");
          }

          var existingTaxAssociation = TaxAssociations.Values.FirstOrDefault(t => t.Sale.Order.Id == saleOrderId);
          if (existingTaxAssociation != null)
          {
            throw new Exception("saleOrderId \"" + saleOrderId + "\" is already tax-associated, see tax association \"" + existingTaxAssociation.Id + "\"");
          }

          data = new PersistedTaxAssociation
          {
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
          data = thing.ClonePersistedData();
          
          if (string.IsNullOrEmpty(saleOrderId))
          {
            saleOrderId = data.SaleOrderId;
          }
          else if (!Orders.TryGetValue(saleOrderId, out var saleOrder))
          {
            throw new Exception("SaleOrderId refers to unrecognized order");
          }
          else if (!saleOrder.IsTaxableSale)
          {
            throw new Exception($"cannot use order id \"{saleOrderId}\" as tax association sale order because it does not satisfy {nameof(Order)}.{nameof(Order.IsTaxableSale)}");
          }

          var existingTaxAssociation = TaxAssociations.Values.FirstOrDefault(t => t.Sale.Order.Id == saleOrderId);
          if (existingTaxAssociation != null && existingTaxAssociation != thing)
          {
            throw new Exception("saleOrderId \"" + saleOrderId + "\" is already tax-associated, see tax association \"" + existingTaxAssociation.Id + "\"");
          }
          data.SaleOrderId = saleOrderId;
        }

        if (purchaseOrderUpdates != null)
        {
          var revisedPurchases = new List<PersistedTaxAssociationPurchase>(data.Purchases);
          foreach ((var orderId, var contributingCoinCount) in purchaseOrderUpdates)
          {
            // find order
            if (!Orders.TryGetValue(orderId ?? string.Empty, out var order))
            {
              throw new Exception("Cannot add unrecognized purchase order id \"" + orderId + "\" to tax association");
            }
            
            if (!order.IsTaxablePurchase)
            {
              throw new Exception($"cannot add order id \"{orderId}\" to tax association purchase orders because it does not satisfy {nameof(Order)}.{nameof(Order.IsTaxablePurchase)}");
            }
            
            if (order.ReceivedCoinType == "USD")
            {
              throw new Exception("cannot add order id \"" + orderId + "\" to tax association purchase orders because USD was received with this order (it was clearly a sale)");
            }

            // make new purchase data
            var newPurchase = new PersistedTaxAssociationPurchase
            {
              OrderId = orderId,
              ContributingCoinCount = contributingCoinCount,
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

          data.Purchases = revisedPurchases;
        }
        
        if (purchaseIdToPinSaleProceedsFudge != null)
        {
          var saleOrder = Orders[saleOrderId];
          int? total = 0;
          PersistedTaxAssociationPurchase foundPurchase = null;
          foreach (var p in data.Purchases)
          {
            p.SaleProceedsFudge = null;
            total += p.GetAttributedSaleProceeds(saleOrder);
            if (p.OrderId == purchaseIdToPinSaleProceedsFudge)
            {
              foundPurchase = p;
            }
          }
          
          var newFudge = saleOrder.TaxableReceivedValueUsd - total;
          if (newFudge == 0) newFudge = null;
          if (foundPurchase == null)
          {
            throw new Exception($"'{nameof(purchaseIdToPinSaleProceedsFudge)}' = {purchaseIdToPinSaleProceedsFudge} is not a purchase of the given tax association");
          }
          foundPurchase.SaleProceedsFudge = newFudge;
        }
        
        ImportTaxAssociations(new[] { data });
        return data.Id;
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
    
    public (IEnumerable<PersistedTransaction> Transactions, IEnumerable<PersistedTaxAssociation> TaxAssociations) ClonePersistedData()
    {
      lock (_lock)
      {
        return (
          Transactions.Values.Select(x => x.ClonePersistedData()), 
          TaxAssociations.Values.Select(x => x.ClonePersistedData())
        );
      }
    }
    
    public string AddAdjustment(string coinType, decimal coinCount)
    {
      lock (_lock)
      {
        if (!Transactions.Values.Any(x => x.PaymentCoinType == coinType || x.ReceivedCoinType == coinType))
        {
          throw new Exception("Unrecognized coin type; you can only adjust coins that you already own.");
        }
        
        var id = "adjustment-" + Guid.NewGuid().ToString();
        ImportTransactions(new [] { new PersistedTransaction
        {
          Id = id,
          TransactionType = TransactionType.Adjustment,
          Exchange = CryptoExchange.None,
          Time = DateTime.UtcNow,
          PaymentCoinType = coinCount < 0 ? coinType : "USD",
          PaymentCoinCount = coinCount < 0 ? coinCount : 0m,
          ReceivedCoinType = coinCount >= 0 ? coinType : "USD",
          ReceivedCoinCount = coinCount >= 0 ? coinCount : 0m,
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