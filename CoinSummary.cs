using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoProfiteer
{
  public class CoinSummary
  {
    public string CoinType { get; }
    public Decimal CoinCount { get; }
    
    public CoinSummary(string coinType, Decimal coinCount)
    {
      CoinType = coinType;
      CoinCount = coinCount;
    }
  }
}
