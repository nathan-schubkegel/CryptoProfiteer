using System;

namespace CryptoProfiteer.TradeBots.Messages
{
  // A bot's answer to whether to purchase coins now
  public struct WantsToBuyResult
  {
    // The amount of USD to spend now, or null to spend none.
    public Decimal? UsdToSpend;
    
    // Decision-making note to add to the log output. May be null if not needed.
    public string Note;
  }
}