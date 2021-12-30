using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CryptoProfiteer
{
  // NOTE: this type is JSON serialized/deserialized
  public class PersistedTaxAssociation
  {
    public string Id { get; set; }
    public string SaleOrderId { get; set; }
    public List<PersistedTaxAssociationPurchase> Purchases { get; set; }
  }

  // NOTE: this type is JSON serialized/deserialized
  public class PersistedTaxAssociationPurchase
  {
    public string OrderId { get; set; }
    public Decimal ContributingCoinCount { get; set; }
    public int ContributingCost { get; set; }
  }

  public class TaxAssociation
  {
    private readonly PersistedTaxAssociation _data;

    public TaxAssociation(PersistedTaxAssociation data, IReadOnlyDictionary<string, Order> allOrders)
    {
      _data = data;

      var saleOrder = allOrders.GetValueOrDefault(data.SaleOrderId) ?? throw new Exception("Tax association sale data refers to order that does not exist");      
      Sale = new TaxAssociationSale(this, saleOrder);
      
      if (data.Purchases == null || data.Purchases.Count == 0) throw new Exception("Tax association had empty purchase data");
      var purchases = new List<TaxAssociationPurchase>();
      foreach (var purchase in data.Purchases)
      {
        var order = allOrders.GetValueOrDefault(purchase.OrderId) ?? throw new Exception("Tax association purchase data refers to order that does not exist");
        purchases.Add(new TaxAssociationPurchase(purchase, order));
      }
      Purchases = purchases;
      
      // TODO: somebody should make sure these contributing costs are all negative
      TotalCostBought = Purchases.Sum(p => p.ContributingCost);
      
      // TODO: somebody shoud make sure these contributing coin counts are all positive
      CoinCountBought = Purchases.Sum(p => p.ContributingCoinCount);
    }

    public string Id => _data.Id;
    public string CoinType => Sale.Order.CoinType;
    public DateTime Time => Sale.Order.Time;
    public string FriendlyName => Sale.Order.FriendlyName;
    public IReadOnlyList<TaxAssociationPurchase> Purchases { get; }
    public TaxAssociationSale Sale { get; }
    public int TotalCostBought { get; }
    public int TotalCostSold => Sale.Order.TaxableTotalCost;
    public bool IsNetGain => TotalCostBought + TotalCostSold >= 0;
    public int PercentNetGainLoss
    {
      get
      {
        try
        {
          return (int)(Math.Abs(((double)TotalCostBought + TotalCostSold) / TotalCostBought) * 100);
        }
        catch
        {
          return 0;
        }
      }
    }
    public Decimal CoinCountBought { get; }
    public Decimal CoinCountSold => Sale.Order.CoinCount;

    public PersistedTaxAssociation GetPersistedData() => _data;
  }
  
  public class TaxAssociationPurchase
  {
    private readonly PersistedTaxAssociationPurchase _data;
    
    public TaxAssociationPurchase(PersistedTaxAssociationPurchase data, Order order)
    {
      _data = data ?? throw new Exception("nope. gotta have some backing data.");
      Order = order ?? throw new Exception("nope. gotta have an associated order.");

      if (order.TransactionType != TransactionType.Buy) throw new Exception("Tax association purchase data refers to order that is not a purchase");
    }
    
    public Order Order { get; }
    public Decimal ContributingCoinCount => _data.ContributingCoinCount;
    public int ContributingCost => _data.ContributingCost;
  }
  
  public class TaxAssociationSale
  {
    private TaxAssociation _association;
    
    public TaxAssociationSale(TaxAssociation association, Order order)
    {
      _association = association ?? throw new Exception("nope. gotta have some backing data.");
      Order = order ?? throw new Exception("nope. gotta have an associated order.");

      if (order.TransactionType != TransactionType.Sell) throw new Exception("Tax association sale data refers to order that is not a sale");
    }
    
    public Order Order { get; }
  }
}
