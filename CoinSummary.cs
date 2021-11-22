using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoProfiteer
{
  public class CoinSummary
  {
    private readonly FriendlyName _friendlyName;

    public string CoinType { get; }
    public string FriendlyName => _friendlyName.Value;
    public Decimal CoinCount { get; }

    // may be null if not known
    public CoinPrice CoinPrice { get; }

    // may be null if not known
    public Decimal? CashValue { get; }

    public CoinSummary(string coinType, FriendlyName friendlyName, Decimal coinCount, CoinPrice coinPrice)
    {
      CoinType = coinType;
      _friendlyName = friendlyName;
      CoinCount = coinCount;
      CoinPrice = coinPrice;
      CashValue = coinPrice == null ? (Decimal?)null : coinPrice.PerCoinCost * CoinCount;
    }
  }
}
