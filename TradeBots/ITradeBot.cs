using System;
using CryptoProfiteer.TradeBots.Messages;

namespace CryptoProfiteer.TradeBots
{
  // interface for crypto trading bots
  public interface ITradeBot
  {
    // the runner asks the bot for this info at the start of a trading session
    // to determine stuff like what coin the runner should watch and try to buy
    ConfigResult GetConfig();
    
    // does the bot want to buy coins right now? how much money to spend? (assumes market price)
    // a non-null return value doesn't complete a purchase; it just instructs the system how much to buy if it can
    WantsToBuyResult WantsToBuy(WantsToBuyArgs args);
    
    // does the bot want to sell coins right now? how many coins to sell? (assumes market price)
    // a non-null return value doesn't complete a sale; it just instructs the system how much to sell if it can
    WantsToSellResult WantsToSell(WantsToSellArgs args);
    
    // notifies the bot that it successfully purchased X coins for Y dollars
    void Bought(BoughtArgs args);
    
    // notifies the bot that it successfully sold X coins for Y dollars
    void Sold(SoldArgs args);
    
    // notifies the bot of the latest-fetched current crypto price
    void ApplyCurrentPrice(CurrentPriceArgs args);
    
    // Notifies the bot of the latest-produced ticker candle
    void ApplyNextCandle(NextCandleArgs args);
  }
}