using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CryptoProfiteer
{
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

      // FUTURE: somebody should make sure these contributing costs loaded from file are all negative
      // (currently it's done in DataService.UpdateTaxAssociation() when they're created)
      TotalCostBought = Purchases.Sum(p => p.ContributingCost);

      // FUTURE: somebody should make sure these contributing counts loaded from file are all positive
      // (currently it's done in DataService.UpdateTaxAssociation() when they're created)
      CoinCountBought = Purchases.Sum(p => p.ContributingCoinCount);
    }

    public string Id => _data.Id;
    public string CoinType => Sale.Order.CoinType;
    public DateTime Time => Sale.Order.Time;
    public string FriendlyName => Sale.Order.FriendlyName;
    public IReadOnlyList<TaxAssociationPurchase> Purchases { get; }
    public TaxAssociationSale Sale { get; }
    public int TotalCostBought { get; }
    public int TotalCostSold => Sale.Order.TaxableTotalCostUsd ?? 0;
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

    public PersistedTaxAssociation ClonePersistedData() => _data.Clone();
  }
}
