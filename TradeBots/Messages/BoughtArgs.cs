using System;

namespace CryptoProfiteer.TradeBots.Messages
{
  // Notification that the bot successfully completed a purchase.
  public struct BoughtArgs
  {
    // The amount of USD that the bot spent.
    // Guaranteed to be greater than zero.
    public Decimal SpentUsd;
    
    // The number of coins that the bot acquired in this transaction.
    // Guaranteed to be greater than zero.
    public Decimal BoughtCoinCount;
    
    // The amount of USD the bot currently holds.
    // Guaranteed to be greater than or equal to zero.
    public Decimal Usd;
    
    // The amount of traded coins the bot currently holds.
    // Guaranteed to be greater than or equal to zero.
    public Decimal CoinCount;
    
    // The exchange fee in USD
    public Decimal FeeUsd;
    
    // The per-coin price of the transaction, taking into account the exchange fee.
    public Decimal PerCoinPriceIncludingFee;
    
    // The per-coin price of the transaction, NOT taking into account the exchange fee.
    public Decimal PerCoinPriceBeforeFee;
  }
}