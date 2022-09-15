using System;

namespace CryptoProfiteer.TradeBots.Messages
{
  // Notification that a new current price of the bot's traded coin has been learned.
  public struct CurrentPriceArgs
  {
    // The time this price was determined.
    public DateTime Time;
    
    // The coin's price.
    public Decimal PerCoinPrice;
  }
}