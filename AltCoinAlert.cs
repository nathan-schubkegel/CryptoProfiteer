using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace CryptoProfiteer
{
  public class AltCoinAlert
  {
    public DateTime Date { get; }
    public IReadOnlyList<string> HypeCoins { get; }
    public IReadOnlyList<string> WinnerCoins { get; }
    public IReadOnlyList<string> LoserCoins { get; }

    public AltCoinAlert(DateTime date, List<string> hypeCoins, List<string> winnerCoins, List<string> loserCoins)
    {
      Date = date;
      HypeCoins = hypeCoins;
      WinnerCoins = winnerCoins;
      LoserCoins = loserCoins;
    }
  }
}
