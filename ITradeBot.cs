using System;

namespace CryptoProfiteer
{
  public class SunkBotException : Exception { }
  
  public interface ITradeBot
  {
    // which crypto exchange this bot operates on
    CryptoExchange Exchange { get; }
    
    // what timespan of candles this bot operates on
    CandleGranularity Granularity { get; }
    
    // what frequency the bot desires to learn the current price of its crypto
    int? CurrentPriceFrequencySeconds { get; }
    
    // which crypto this bot buys/sells
    string CoinType { get; }
    
    // how much of the crypto coin this bot currently holds
    Decimal CoinCount { get; }
    
    // how much cash this bot currently holds
    Decimal Usd { get; }
    
    // if true, then the bot has self-destructed from taking on too many losses
    bool IsSunk { get; }

    // does the bot want to buy coins right now? how much money to spend? (assumes market price)
    // a non-null return value doesn't complete a purchase; it just instructs the system how much to buy if it can
    Decimal? WantsToBuy();
    
    // does the bot want to sell coins right now? how many coins to sell? (assumes market price)
    // a non-null return value doesn't complete a sale; it just instructs the system how much to sell if it can
    Decimal? WantsToSell();
    
    // notifies the bot that it successfully purchased X coins for Y dollars
    void Bought(Decimal coinCount, Decimal usd);
    
    // notifies the bot that it successfully sold X coins for Y dollars
    void Sold(Decimal coinCount, Decimal usd);
    
    // notifies the bot of the latest-fetched current crypto price
    void ApplyCurrentPrice(DateTime time, Decimal price);
    
    // Notifies the bot of the latest-produced ticker candle
    void ApplyNextCandle(DateTime time, Candle candle);
  }
}