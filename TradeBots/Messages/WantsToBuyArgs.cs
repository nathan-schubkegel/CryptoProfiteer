using System;

namespace CryptoProfiteer.TradeBots.Messages
{
  // Inputs to querying a bot whether to purchase coins now.
  public struct WantsToBuyArgs
  {
    // The amount of USD the bot currently holds.
    // Guaranteed to be greater than zero.
    public Decimal Usd;
    
    // The amount of traded coins the bot currently holds.
    // Guaranteed to be greater than or equal to zero.
    public Decimal CoinCount;
    
    // The current coin price, in USD per coin.
    // Guaranteed to be greater than zero.
    public Decimal PerCoinPrice;
    
    // The fee that will be assessed by the crypto exchange, as a percent of Usd spent.
    // Example: 5% is presented as 0.05
    public Decimal FeePercent;

    // If all currently held coins were sold now, what would they be worth?
    public Decimal CoinCountToLikelyUsd => (CoinCount * PerCoinPrice) * (1m - FeePercent);
    
    // If all currently held USD was sold now, how many coins would that buy?
    public Decimal UsdToLikelyCoinCount => Usd * (1m - FeePercent) / PerCoinPrice;
  }
}