using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CryptoProfiteer
{
  public class PersistedHistoricalCoinPrice
  {
    [JsonProperty("coin")]
    public string CoinType { get; set; }
    
    [JsonProperty("time")]
    public DateTime Time { get; set; }
    
    [JsonProperty("price")]
    public Decimal PricePerCoinUsd { get; set; }
    
    public PersistedHistoricalCoinPrice Clone() => (PersistedHistoricalCoinPrice)MemberwiseClone();
  }
  
  public class PersistedHistoricalCoinPrice_v04
  {
    public string CoinType { get; set; }
    public DateTime Time { get; set; }
    public CryptoExchange Exchange { get; set; }
    public Decimal? PricePerCoinUsd { get; set; }
    
    public PersistedHistoricalCoinPrice ToLatest() => new PersistedHistoricalCoinPrice
    {
      CoinType = CoinType,
      Time = Time,
      PricePerCoinUsd = PricePerCoinUsd ?? throw new Exception("You need to check for null PricePerCoinUsd before calling ToLatest()"),
    };
  }
}