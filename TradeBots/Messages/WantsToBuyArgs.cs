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
    
    // Computes how many coins the bot will receive if it spends the given amount of USD.
    public Decimal PredictPurchase(Decimal usdToSpend)
    {
      return usdToSpend * (1m - FeePercent) / PerCoinPrice;
    }
  }
}