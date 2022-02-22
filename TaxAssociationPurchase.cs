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

      if (order.TransactionType != TransactionType.Buy) throw new Exception("Tax association purchase data refers to order that is not a purchase");
    }
    
    public Order Order { get; }
    public Decimal ContributingCoinCount => _data.ContributingCoinCount;
    public int ContributingCost => _data.ContributingCost;
  }
}
