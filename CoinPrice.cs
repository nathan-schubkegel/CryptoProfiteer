using System;

namespace CryptoProfiteer
{
  public class CoinPrice
  {
    public string CoinType { get; }
    public Decimal PerCoinCost { get; }
    public DateTime LastUpdatedTime { get; }
    
    public CoinPrice(string coinType, Decimal perCoinCost, DateTime lastUpdated)
    {
      CoinType = coinType;
      PerCoinCost = perCoinCost;
      LastUpdatedTime = lastUpdated;
    }
  }
}