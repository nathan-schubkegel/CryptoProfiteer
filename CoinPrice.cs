using System;

namespace CryptoProfiteer
{
  public class CoinPrice
  {
    private readonly FriendlyName _friendlyName;

    public string CoinType { get; }
    public string FriendlyName => _friendlyName.Value;
    public Decimal PerCoinCostUsd { get; }
    public DateTime LastUpdatedTime { get; }
    public CoinPrice(string coinType, decimal perCoinCostUsd, FriendlyName friendlyName, DateTime lastUpdated)
    {
      CoinType = coinType;
      _friendlyName = friendlyName;
      PerCoinCostUsd = perCoinCostUsd;
      LastUpdatedTime = lastUpdated;
    }
  }
}