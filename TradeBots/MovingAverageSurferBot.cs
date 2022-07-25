
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using CryptoProfiteer.TradeBots.Messages;

namespace CryptoProfiteer.TradeBots
{
  // This bot measures moving averages for 10, 20, 40, 80, 150, and 300 candles,
  // then buys low and sells high on those curves
  public class MovingAverageSurferBot : ITradeBot
  {
    public enum PriceDirection
    {
      None,
      Rising,
      Falling
    }
    
    private class MovingAverage
    {
      public int IntendedCandleCount;
      public Dequeue<decimal> Prices = new Dequeue<decimal>();
      public Dequeue<decimal> Averages = new Dequeue<decimal>();
      public PriceDirection Slope;
      
      public void AddCandle(Candle candle)
      {
        Prices.AddBack(candle.Close);
        while (Prices.Count > IntendedCandleCount) Prices.RemoveFront();
        Averages.AddBack(Prices.Average());
        while (Averages.Count > IntendedCandleCount) Averages.RemoveFront();
        if (Averages.Count == 1)
        {
          Slope = PriceDirection.None;
        }
        else
        {
          var difference = Averages[Averages.Count - 1] - Averages[Averages.Count - 2];
          Slope = difference > 0 ? PriceDirection.Rising
            : difference < 0 ? PriceDirection.Falling
            : PriceDirection.None;
        }
      }
    }
    
    private readonly ConfigResult _config;
    private readonly int _movingAverageCandleCount;
    private MovingAverage _thing = new MovingAverage();
    private const decimal _lossPreventionPricePercent = 0.95m;
    private decimal _lossPreventionSellCoinPrice;
    private bool _bought = false;

    public MovingAverageSurferBot(string coinType, CandleGranularity granularity, int movingAverageCandleCount)
    {
      _movingAverageCandleCount = movingAverageCandleCount;
      _thing = new MovingAverage { IntendedCandleCount = movingAverageCandleCount };
      _config = new ConfigResult
      {
        CoinType = coinType,
        Exchange = CryptoExchange.CoinbasePro,
        CandleGranularity = granularity,
      };
    }
    
    // the runner asks the bot for this info at the start of a trading session
    // to determine stuff like what coin the runner should watch and try to buy
    public ConfigResult GetConfig() => _config;
   
    // does the bot want to buy coins right now? how much money to spend? (assumes market price)
    // a non-null return value doesn't complete a purchase; it just instructs the system how much to buy if it can
    public WantsToBuyResult WantsToBuy(WantsToBuyArgs args)
    {
      // don't buy if I haven't measured enough
      if (_thing.Averages.Count < _thing.IntendedCandleCount) return default;
      
      // don't buy if I already bought
      if (_bought) return default;
      
      // buy when the moving average slope is positive
      if (_thing.Slope == PriceDirection.Rising)
      {
        return new WantsToBuyResult
        {
          UsdToSpend = args.Usd,
          Note = $"buying because moving average slope is flat/up",
        };
      }
      
      return default;
    }
    
    // does the bot want to sell coins right now? how many coins to sell? (assumes market price)
    // a non-null return value doesn't complete a sale; it just instructs the system how much to sell if it can
    public WantsToSellResult WantsToSell(WantsToSellArgs args)
    {
      // don't sell if I haven't purchased coins yet
      if (!_bought) return default;
      
      // sell when the moving average slope becomes negative
      if (_thing.Slope == PriceDirection.Falling)
      {
        return new WantsToSellResult
        {
          CoinCountToSell = args.CoinCount,
          Note = $"selling because moving average slope is flat/down",
        };
      }
      
      // sell when the price gets too low
      if (args.PerCoinPrice <= _lossPreventionSellCoinPrice)
      {
        return new WantsToSellResult
        {
          CoinCountToSell = args.CoinCount,
          Note = $"selling to prevent further loss :'(",
        };
      }
      
      return default;
    }
    
    // notifies the bot that it successfully purchased X coins for Y dollars
    public void Bought(BoughtArgs args)
    {
      _bought = true;
      _lossPreventionSellCoinPrice = args.PerCoinPriceBeforeFee * _lossPreventionPricePercent;
    }
    
    // notifies the bot that it successfully sold X coins for Y dollars
    public void Sold(SoldArgs args)
    {
      _bought = false;
      _thing = new MovingAverage { IntendedCandleCount = _movingAverageCandleCount };
    }
    
    // Notifies the bot of the latest-produced ticker candle
    public void ApplyNextCandle(NextCandleArgs args)
    {
      _thing.AddCandle(args.Candle);
    }
  }
}