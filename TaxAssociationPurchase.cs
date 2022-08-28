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

      if (order.TransactionType != TransactionType.Trade) throw new Exception("Tax association purchase data refers to order that is not a trade");
      if (order.ReceivedCoinType == "USD") throw new Exception("Tax association purchase data refers to order that received USD (nope. if USD is involved in a purchase, it's gotta be paid USD)");
    }

    public Order Order { get; }
    public Decimal ContributingCoinCount => _data.ContributingCoinCount;
    public int ContributingCost => _data.ContributingCost;
  }
}
