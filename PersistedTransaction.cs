using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CryptoProfiteer
{
  // NOTE: this type is JSON serialized/deserialized
  public class PersistedTransaction
  {
    [JsonProperty("id")]
    public string Id { get; set; }
    
    [JsonProperty("oaId")]
    public string OrderAggregationId { get; set; }
    
    [JsonProperty("type")]
    public TransactionType TransactionType { get; set; }
    
    [JsonProperty("exch")]
    public CryptoExchange Exchange { get; set; }
    
    [JsonProperty("time")]
    public DateTime Time { get; set; }
    
    [JsonProperty("rCoin")]
    public string ReceivedCoinType { get; set; }
    
    [JsonProperty("pCoin")]
    public string PaymentCoinType { get; set; }
    
    [JsonProperty("rCount")]
    public Decimal ReceivedCoinCount
    { 
      get => _receivedCoinCount;
      set => _receivedCoinCount = Math.Abs(value); // always positive - just fix mistakes
    }
    private Decimal _receivedCoinCount;
    
    [JsonProperty("pCount")]
    public Decimal PaymentCoinCount
    { 
      get => _paymentCoinCount;
      set => _paymentCoinCount = Math.Abs(value); // always positive - just fix mistakes
    }
    private Decimal _paymentCoinCount;
    
    public PersistedTransaction Clone() => (PersistedTransaction)MemberwiseClone();
  }
  
  // NOTE: this type is JSON serialized/deserialized
  public class PersistedTransaction_v04
  {
    public string TradeId { get; set; }
    public string OrderAggregationId { get; set; }
    public TransactionType_v04 TransactionType { get; set; }
    public CryptoExchange Exchange { get; set; }
    public DateTime Time { get; set; }
    public string CoinType { get; set; }
    public string PaymentCoinType { get; set; }
    public Decimal CoinCount { get; set; }
    public Decimal PerCoinCost { get; set; }
    public Decimal Fee { get; set; }
    public Decimal TotalCost { get; set; }
    
    public PersistedTransaction_v04 Clone() => (PersistedTransaction_v04)MemberwiseClone();
    public PersistedTransaction ToLatest() => new PersistedTransaction
    {
      Id = TradeId,
      OrderAggregationId = OrderAggregationId,
      TransactionType = TransactionType switch
      {
        TransactionType_v04.Buy => CryptoProfiteer.TransactionType.Trade,
        TransactionType_v04.Sell => CryptoProfiteer.TransactionType.Trade,
        TransactionType_v04.Adjustment => CryptoProfiteer.TransactionType.Adjustment,
        _ => throw new NotImplementedException(),
      },
      Exchange = Exchange,
      Time = Time,
      ReceivedCoinType = TransactionType switch
      {
        TransactionType_v04.Buy => CoinType,
        TransactionType_v04.Sell => PaymentCoinType,
        TransactionType_v04.Adjustment => CoinType,
        _ => throw new NotImplementedException(),
      },
      PaymentCoinType = TransactionType switch
      {
        TransactionType_v04.Buy => PaymentCoinType,
        TransactionType_v04.Sell => CoinType,
        TransactionType_v04.Adjustment => CoinType,
        _ => throw new NotImplementedException(),
      },
      ReceivedCoinCount = TransactionType switch
      {
        TransactionType_v04.Buy => CoinCount,
        TransactionType_v04.Sell => TotalCost,
        TransactionType_v04.Adjustment => CoinCount > 0 ? CoinCount : 0,
        _ => throw new NotImplementedException(),
      },
      PaymentCoinCount = TransactionType switch
      {
        TransactionType_v04.Buy => TotalCost,
        TransactionType_v04.Sell => CoinCount,
        TransactionType_v04.Adjustment => CoinCount < 0 ? CoinCount : 0,
        _ => throw new NotImplementedException(),
      },
    };
  }
}