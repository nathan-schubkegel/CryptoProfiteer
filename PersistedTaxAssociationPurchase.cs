using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CryptoProfiteer
{
  // NOTE: this type is JSON serialized/deserialized
  public class PersistedTaxAssociationPurchase
  {
    public string OrderId { get; set; }
    public Decimal ContributingCoinCount { get; set; }
    public int ContributingCost { get; set; }
    
    public PersistedTaxAssociationPurchase Clone() => (PersistedTaxAssociationPurchase)MemberwiseClone();
  }
}
