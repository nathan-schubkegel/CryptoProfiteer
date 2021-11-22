using System;

namespace CryptoProfiteer
{
  public class CoinPrice
  {
    public string CoinType { get; }
    public Decimal PerCoinCost { get; }
    public DateTime LastUpdatedTime { get; }
    
    // may be null if not known
    public string FriendlyName { get; }
    
    public CoinPrice(string coinType, Decimal perCoinCost, DateTime lastUpdated, string friendlyName)
    {
      CoinType = coinType;
      PerCoinCost = perCoinCost;
      LastUpdatedTime = lastUpdated;
      FriendlyName = friendlyName;
    }
  }
}