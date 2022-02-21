using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CryptoProfiteer
{
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