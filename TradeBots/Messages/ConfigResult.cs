using System;

namespace CryptoProfiteer.TradeBots.Messages
{
  // The initial configuration of a bot; used to prime the bot runner.
  public struct ConfigResult
  {
    // which crypto exchange this bot operates on
    public CryptoExchange Exchange;
    
    // which crypto this bot buys/sells
    public string CoinType;
  
    // what timespan of candles this bot desires
    // or null if it doesn't use candle data
    public CandleGranularity? CandleGranularity;
    
    // whether the bot needs live market price updates
    public bool WantsCurrentPriceUpdates;
  }
}