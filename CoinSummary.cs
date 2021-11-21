using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoProfiteer
{
  public class CoinSummary
  {
    public string CoinType { get; }
    public Decimal CoinCount { get; }
    
    // may be null if not known
    public CoinPrice CoinPrice { get; }
    
    // may be null if not known
    public Decimal? TotalValue { get; }
    
    public CoinSummary(string coinType, Decimal coinCount, CoinPrice coinPrice)
    {
      CoinType = coinType;
      CoinCount = coinCount;
      CoinPrice = coinPrice;
      TotalValue = coinPrice == null ? (Decimal?)null : coinPrice.PerCoinCost * CoinCount;
    }
  }
}
