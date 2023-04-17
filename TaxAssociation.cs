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
      
      if ((data.Purchases == null || data.Purchases.Count == 0) && !saleOrder.IsTaxableFuturesGain) throw new Exception("Tax association had empty purchase data");
      var purchases = new List<TaxAssociationPurchase>();
      foreach (var purchase in data.Purchases ?? Enumerable.Empty<PersistedTaxAssociationPurchase>())
      {
        var order = allOrders.GetValueOrDefault(purchase.OrderId) ?? throw new Exception("Tax association purchase data refers to order that does not exist");
        if (order.ReceivedCoinType != saleOrder.PaymentCoinType) throw new Exception("Tax association purchase data refers to order that sold a different coin type");
        purchases.Add(new TaxAssociationPurchase(purchase, order));
      }
      Purchases = purchases;
      CoinCountBought = Purchases.Sum(p => p.ContributingCoinCount);
    }

    public string Id => _data.Id;
    public string CoinType => Sale.Order.PaymentCoinType;
    public DateTime Time => Sale.Order.Time;
    public IReadOnlyList<TaxAssociationPurchase> Purchases { get; }
    public TaxAssociationSale Sale { get; }
    private int? _taxableCostBasisUsd;
    public int? TaxableCostBasisUsd
    {
      get
      {
        if (_taxableCostBasisUsd != null) return _taxableCostBasisUsd;
        int sum = 0;
        foreach (var purchase in Purchases)
        {
          var cost = purchase.ContributingCost;
          if (cost == null) return null;
          sum += cost.Value;
        }
        _taxableCostBasisUsd = sum;
        return sum;
      }
    }
    public int? TaxableSaleProceedsUsd => Sale.Order.TaxableReceivedValueUsd;
    public int? NetGainLoss => -TaxableCostBasisUsd + TaxableSaleProceedsUsd;
    public int? NetGainLossAbs => NetGainLoss == null ? null : Math.Abs(NetGainLoss.Value);
    
    public int? PercentNetGainLoss
    {
      get
      {
        if (TaxableCostBasisUsd == null) return null;
        if (TaxableCostBasisUsd == 0) return 100;
        if (NetGainLoss == null) return null;
        try
        {
          return (int)Math.Abs(((double)NetGainLoss.Value / TaxableCostBasisUsd.Value) * 100);
        }
        catch
        {
          return null;
        }
      }
    }
    public Decimal CoinCountBought { get; }
    public Decimal CoinCountSold => Sale.Order.PaymentCoinCount;

    public PersistedTaxAssociation ClonePersistedData() => _data.Clone();
    
    public string SaleDescription => $"Sold {Sale.Order.PaymentCoinCount.FormatMinDecimals()} {Sale.Order.PaymentCoinType} " +
      (Sale.Order.PaymentCoinType != "USD" ? $"worth {(Sale.Order.PaymentValueUsd?.FormatMinDecimals() ?? "<unknown>")} USD " : "") +
      $"for {Sale.Order.ReceivedCoinCount.FormatMinDecimals()} {Sale.Order.ReceivedCoinType}" + 
      (Sale.Order.ReceivedCoinType != "USD" ? $" worth {(Sale.Order.ReceivedValueUsd?.FormatMinDecimals() ?? "<unknown>")} USD" : "");
  }
}
