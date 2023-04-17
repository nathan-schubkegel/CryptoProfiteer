using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CryptoProfiteer
{
  public class TaxAssociationPurchase
  {
    private readonly PersistedTaxAssociationPurchase _data;

    public TaxAssociationPurchase(PersistedTaxAssociationPurchase data, Order order)
    {
      _data = data ?? throw new Exception("nope. gotta have some backing data.");
      Order = order ?? throw new Exception("nope. gotta have an associated order.");

      if (order.TransactionType != TransactionType.Trade && order.TransactionType != TransactionType.FuturesPnl) throw new Exception("Tax association purchase data refers to order that is not a trade or a FuturesPnl");
      if (order.ReceivedCoinType == "USD") throw new Exception("Tax association purchase data refers to order that received USD (nope. if USD is involved in a purchase, it's gotta be paid USD)");
    }

    public Order Order { get; }
    public Decimal ContributingCoinCount => _data.ContributingCoinCount;
    public int? ContributingCost
    {
      get
      {
        if (Order.TransactionType == TransactionType.Trade)
        {
          double percent = (double)ContributingCoinCount / (double)Order.ReceivedCoinCount;
          double? amount = (double?)Order.PaymentValueUsd * percent;
          if (amount == null) return null;
          return (int)Math.Round((Decimal)amount.Value, MidpointRounding.AwayFromZero);
        }
        else // FuturesPnl
        {
          var totalCoinCount = Order.ReceivedCoinCount > 0 ? Order.ReceivedCoinCount : Order.PaymentCoinCount;
          double percent = (double)ContributingCoinCount / (double)totalCoinCount;
          Decimal? totalValueUsd = Order.ReceivedCoinCount > 0 ? Order.ReceivedValueUsd : Order.PaymentValueUsd;
          double? amount = (double?)totalValueUsd * percent;
          if (amount == null) return null;
          return (int)Math.Round((Decimal)amount.Value, MidpointRounding.AwayFromZero);
        }
      }
    }
    public int? TaxableCostBasisUsd => ContributingCost;
    public int? SaleProceedsFudge => _data.SaleProceedsFudge;
    
    public string PurchaseDescription =>
      $"Bought {ContributingCoinCount.FormatMinDecimals()} {Order.ReceivedCoinType} " +
      (ContributingCoinCount != Order.ReceivedCoinCount ? $"as part of {Order.ReceivedCoinCount.FormatMinDecimals()} {Order.ReceivedCoinType} order " : "") +
      $"for {Order.PaymentCoinCount.FormatMinDecimals()} {Order.PaymentCoinType}" +
      (Order.PaymentCoinType != "USD" ? $" worth {(Order.PaymentValueUsd?.FormatMinDecimals() ?? "<unknown>")} USD" : "");
      
    public int? GetAttributedSaleProceeds(TaxAssociation taxAssociation) => _data.GetAttributedSaleProceeds(taxAssociation.Sale.Order);
  }
}
