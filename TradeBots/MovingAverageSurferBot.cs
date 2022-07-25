
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
      public Deque<decimal> Prices = new Deque<decimal>();
      public Deque<decimal> Averages = new Deque<decimal>();
      public PriceDirection Slope;
      
      public void AddCandle(Candle candle)
      {
        Prices.AddEnd(candle.Close);
        while (Candles.Count > IntendedCandleCount) Prices.RemoveFront();
        Averages.AddEnd(Prices.Average());
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
    
    // TODO: stopped here
    
    private readonly ConfigResult _config;
    private readonly List< _countOfCandlesToMeasure = 50;
    private readonly List<Candle> _candles = new List<Candle>();

    private Decimal? _targetPurchaseCoinPrice;
    private Decimal _targetSellCoinPrice;
    private Decimal _lossPreventionSellCoinPrice;
    private bool _bought = false;

    public MovingAverageSurferBot(string coinType, CandleGranularity granularity)
    {
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
      if (_targetPurchaseCoinPrice == null) return default;
      
      // don't buy if I already bought
      if (_bought) return default;
      
      // buy when the price gets low enough
      if (args.PerCoinPrice <= _targetPurchaseCoinPrice.Value)
      {
        return new WantsToBuyResult
        {
          UsdToSpend = args.Usd,
          Note = $"buying because price looks low",
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
      
      // sell when the price gets high enough
      if (args.PerCoinPrice >= _targetSellCoinPrice)
      {
        return new WantsToSellResult
        {
          CoinCountToSell = args.CoinCount,
          Note = $"selling because price looks high",
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
    }
    
    // notifies the bot that it successfully sold X coins for Y dollars
    public void Sold(SoldArgs args)
    {
      _bought = false;
      _targetPurchaseCoinPrice = null;
      _candles.Clear();
    }
    
    // Notifies the bot of the latest-produced ticker candle
    public void ApplyNextCandle(NextCandleArgs args)
    {
      if (_bought)
      {
        return;
      }

      if (_candles.Count < _countOfCandlesToMeasure)
      {
        _candles.Add(args.Candle);
        if (_candles.Count == _countOfCandlesToMeasure)
        {
          _targetPurchaseCoinPrice = _candles.Select(x => x.Close).Min();
          _targetSellCoinPrice = _candles.Select(x => x.Close).Max();
          _lossPreventionSellCoinPrice = _targetSellCoinPrice - ((_targetPurchaseCoinPrice.Value - _targetSellCoinPrice) * _percentOfMeasuredRangeToGetOut);
        }
      }
    }
  }
}