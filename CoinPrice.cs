using System;

namespace CryptoProfiteer
{
  public class CoinbaseCoinPrice
  {
    public string CoinType { get; set; }
    public Decimal PerCoinCost { get; set; }
  }
  
  public class CoinPrice
  {
    private readonly FriendlyName _friendlyName;

    public string CoinType { get; }
    public string FriendlyName => _friendlyName.Value;
    public Decimal PerCoinCost { get; }
    public DateTime LastUpdatedTime { get; }
    public CoinPrice(CoinbaseCoinPrice data, FriendlyName friendlyName, DateTime lastUpdated)
    {
      CoinType = data.CoinType;
      _friendlyName = friendlyName;
      PerCoinCost = data.PerCoinCost;
      LastUpdatedTime = lastUpdated;
    }
  }
}