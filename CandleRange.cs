using System;
using System.Collections.Generic;

namespace CryptoProfiteer
{
  public class PersistedCandleRange
  {
    public PersistedCandleRangeId Id { get; set; }
    
    // NOTE: this list contains null arrays where the exchange has gaps in trade data
    // NOTE: this property is persisted to JSON on disk
    public List<Decimal[]> Candles { get; set; }

    public int Count => Candles.Count;

    public Candle? TryGetCandle(int i)
    {
      if (i < 0 || i >= Candles.Count) return null;
      var data = Candles[i];
      if (data == null) return null;
      return new Candle(data);
    }
  }
}