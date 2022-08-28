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
    // NOTE: this object is identified by SaleOrderId
    public string SaleOrderId { get; set; }
    public List<PersistedTaxAssociationPurchase> Purchases { get; set; }
    
    public PersistedTaxAssociation Clone() => new PersistedTaxAssociation
    {
      SaleOrderId = SaleOrderId,
      Purchases = Purchases.Select(x => x.Clone()).ToList(),
    };
  }
  
  // NOTE: this type is JSON serialized/deserialized
  public class PersistedTaxAssociation_v04
  {
    public string Id { get; set; }
    public string SaleOrderId { get; set; }
    public List<PersistedTaxAssociationPurchase_v04> Purchases { get; set; }
    
    public PersistedTaxAssociation_v04 Clone() => new PersistedTaxAssociation_v04
    {
      Id = Id,
      SaleOrderId = SaleOrderId,
      Purchases = Purchases.Select(x => x.Clone()).ToList(),
    };
    
    public PersistedTaxAssociation ToLatest() => new PersistedTaxAssociation
    {
      SaleOrderId = SaleOrderId,
      Purchases = Purchases.Select(x => x.ToLatest()).ToList(),
    };
  }
}