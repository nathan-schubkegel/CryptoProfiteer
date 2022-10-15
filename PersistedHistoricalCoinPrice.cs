using System;

namespace CryptoProfiteer
{
  public class PersistedHistoricalCoinPrice
  {
    public string CoinType { get; set; }
    public DateTime Time { get; set; }
    
    // NOTE FOREVER: this was present in release v04
    //public CryptoExchange Exchange { get; set; }
    
    public Decimal? PricePerCoinUsd { get; set; }
  }
}