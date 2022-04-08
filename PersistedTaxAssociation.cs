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
    
    public PersistedTaxAssociation Clone() => new PersistedTaxAssociation
    {
      Id = Id,
      SaleOrderId = SaleOrderId,
      Purchases = Purchases.Select(x => x.Clone()).ToList(),
    };
  }
}