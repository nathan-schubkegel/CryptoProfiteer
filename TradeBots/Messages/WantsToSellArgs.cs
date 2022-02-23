using System;

namespace CryptoProfiteer.TradeBots.Messages
{
  // Inputs to querying a bot whether to sell coins now.
  public struct WantsToSellArgs
  {
    // The amount of USD the bot currently holds.
    // Guaranteed to be greater than or equal to zero.
    public Decimal Usd;
    
    // The amount of traded coins the bot currently holds.
    // Guaranteed to be greater than zero.
    public Decimal CoinCount;
    
    // The current coin price, in USD per coin.
    // Guaranteed to be greater than zero.
    public Decimal PerCoinPrice;
    
    // The fee that will be assessed by the crypto exchange, as a percent of Usd gained.
    // Example: 5% is presented as 0.05
    public Decimal FeePercent;
    
    // Computes how much USD the bot will earn if it sells the given number of coins.
    public Decimal UsdToEarn(Decimal coinCount)
    {
      return coinCount * PerCoinPrice * (1m - FeePercent);
    }
  }
}