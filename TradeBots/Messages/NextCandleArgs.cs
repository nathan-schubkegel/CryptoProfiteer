using System;

namespace CryptoProfiteer.TradeBots.Messages
{
  // Notification that a candle has closed
  public struct NextCandleArgs
  {
    // The time this candle started.
    public DateTime StartTime;
    
    // The time this candle finished.
    public DateTime EndTime;
    
    // The candle data.
    public Candle Candle;
  }
}