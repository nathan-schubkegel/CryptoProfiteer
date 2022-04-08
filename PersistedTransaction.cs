using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CryptoProfiteer
{
  // NOTE: this type is JSON serialized/deserialized
  public class PersistedTransaction
  {
    public string TradeId { get; set; }
    public string OrderAggregationId { get; set; }
    public TransactionType TransactionType { get; set; }
    public CryptoExchange Exchange { get; set; }
    public DateTime Time { get; set; }
    public string CoinType { get; set; }
    public string PaymentCoinType { get; set; }
    public Decimal CoinCount { get; set; }
    public Decimal PerCoinCost { get; set; }
    public Decimal Fee { get; set; }
    public Decimal TotalCost { get; set; }
    
    public PersistedTransaction Clone() => (PersistedTransaction)MemberwiseClone();
  }
}