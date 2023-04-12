using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CryptoProfiteer
{
  public class TaxAssociationPurchase : ITransactionish
  {
    private readonly PersistedTaxAssociationPurchase _data;

    public TaxAssociationPurchase(PersistedTaxAssociationPurchase data, Order order)
    {
      _data = data ?? throw new Exception("nope. gotta have some backing data.");
      Order = order ?? throw new Exception("nope. gotta have an associated order.");

      if (order.TransactionType != TransactionType.Trade) throw new Exception("Tax association purchase data refers to order that is not a trade");
      if (order.ReceivedCoinType == "USD") throw new Exception("Tax association purchase data refers to order that received USD (nope. if USD is involved in a purchase, it's gotta be paid USD)");
    }

    public Order Order { get; }
    public Decimal ContributingCoinCount => _data.ContributingCoinCount;
    public int ContributingCost => _data.ContributingCost;
    
    // ITransactionish members
    string ITransactionish.Id => Order.Id;
    TransactionType ITransactionish.TransactionType => Order.TransactionType;
    CryptoExchange ITransactionish.Exchange => Order.Exchange;
    DateTime ITransactionish.Time => Order.Time;
    string ITransactionish.ReceivedCoinType => Order.ReceivedCoinType;
    string ITransactionish.PaymentCoinType => "USD";
    Decimal ITransactionish.ReceivedCoinCount => ContributingCoinCount;
    Decimal ITransactionish.PaymentCoinCount => ContributingCost;
    Decimal? ITransactionish.ReceivedValueUsd => Order.ReceivedValueUsd * (ContributingCoinCount / Order.ReceivedCoinCount);
    Decimal? ITransactionish.PaymentValueUsd => ContributingCost;
    Decimal? ITransactionish.ReceivedPerCoinCostUsd => Order.ReceivedPerCoinCostUsd;
    Decimal? ITransactionish.PaymentPerCoinCostUsd => 1;
  }
}
