using System;

namespace CryptoProfiteer.TradeBots.Messages
{
  // A bot's answer to whether to sell coins now
  public struct WantsToSellResult
  {
    // The amount of coins to sell now, or null to sell none.
    public Decimal? CoinCountToSell;
    
    // Decision-making note to add to the log output. May be null if not needed.
    public string Note;
  }
}