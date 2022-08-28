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
    // NOTE: the contributing coin type is Order.PaymentCoinType
    
    [JsonProperty("oId")]
    public string OrderId { get; set; }
    
    [JsonProperty("count")]
    public Decimal ContributingCoinCount
    {
      get => _contributingCoinCount;
      set => _contributingCoinCount = Math.Abs(value); // always positive - just fix mistakes
    }
    
    [JsonProperty("cost")]
    public int ContributingCost
    {
      get => _contributingCost;
      set => _contributingCost = Math.Abs(value); // always positive - just fix mistakes
    }

    public PersistedTaxAssociationPurchase Clone() => (PersistedTaxAssociationPurchase)MemberwiseClone();
  }
  
  public class PersistedTaxAssociationPurchase_v04
  {
    public string OrderId { get; set; }
    public Decimal ContributingCoinCount { get; set; }
    public int ContributingCost { get; set; }

    public PersistedTaxAssociationPurchase_v04 Clone() => (PersistedTaxAssociationPurchase_v04)MemberwiseClone();
    
    public PersistedTaxAssociationPurchase ToLatest() => new PersistedTaxAssociationPurchase
    {
      OrderId = OrderId,
      ContributingCoinCount = ContributingCoinCount,
      ContributingCost = ContributingCost,
    };
  }
}
