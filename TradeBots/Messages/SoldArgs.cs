using System;

namespace CryptoProfiteer.TradeBots.Messages
{
  // Notification that the bot successfully completed a sale.
  public struct SoldArgs
  {
    // The amount of USD that the bot earned.
    // Guaranteed to be greater than zero.
    public Decimal EarnedUsd;
    
    // The number of coins that the bot sold in this transaction.
    // Guaranteed to be greater than zero.
    public Decimal SoldCoinCount;
    
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