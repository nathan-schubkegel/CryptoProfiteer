using System;

namespace CryptoProfiteer.TradeBots.Messages
{
  // Notification that a candle has closed
  public struct NextCandleArgs
  {
    // The time this candle closed.
    public DateTime Time;
    
    // The candle data.
    public Candle Candle;
  }
}