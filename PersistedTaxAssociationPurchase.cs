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
    
    // The amount of coins received by the order that are used as cost basis for the tax association
    [JsonProperty("count")]
    public Decimal ContributingCoinCount
    {
      get => _contributingCoinCount;
      set => _contributingCoinCount = Math.Abs(value); // always positive - just fix mistakes
    }
    private Decimal _contributingCoinCount;
    
    // The $USD (positive or negative) to fudge the reported sale proceeds for the sale of this purchase
    // so that the total amount of the sale proceeds for all purchases sums to the single true sale order amount
    [JsonProperty("fudge")]
    public int? SaleProceedsFudge
    {
      get => _saleProceedsFudge;
      set => _saleProceedsFudge = value;
    }
    private int? _saleProceedsFudge;
    
    public int? GetAttributedSaleProceeds(Order taxAssociationSaleOrder)
    {
      double percent = (double)ContributingCoinCount / (double)taxAssociationSaleOrder.PaymentCoinCount;
      double? amount = (double?)taxAssociationSaleOrder.ReceivedValueUsd * percent;
      if (amount == null)
      {
        return null;
      }
      var result = (int)Math.Round(amount.Value, MidpointRounding.AwayFromZero);
      if (SaleProceedsFudge != null)
      {
        result += SaleProceedsFudge.Value;
      }
      return result;
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
    };
  }
}
