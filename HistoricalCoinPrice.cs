using System;

namespace CryptoProfiteer
{
  public class PersistedHistoricalCoinPrice
  {
    public string CoinType { get; set; }
    public DateTime Time { get; set; }
    public CryptoExchange Exchange { get; set; }
    public Decimal PricePerCoinUsd { get; set; }
  }
}