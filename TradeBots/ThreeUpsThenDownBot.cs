
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using CryptoProfiteer.TradeBots.Messages;

namespace CryptoProfiteer.TradeBots
{
  // This bot tries to buy when a candle goes bearish after 3 bullish candles,
  // and tries to sell when a candle goes bearish
  public class ThreeUpsThenDownBot : ITradeBot
  {
    private readonly ConfigResult _config;
    private readonly Decimal _percentLossToJustSell = 0.05m;
    private readonly List<Candle> _candles = new List<Candle>();
    private Decimal? _lastPurchasedPerCoinPrice;
    private Decimal CurrentPrice => _candles.Last().Close;

    public ThreeUpsThenDownBot(string coinType, CandleGranularity granularity)
    {
      _config = new ConfigResult
      {
        CoinType = coinType,
        Exchange = CryptoExchange.Coinbase,
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
      // don't buy while I'm holding coins
      if (args.CoinCount > 0m) return default;
      
      // can't buy if I'm out of money
      if (args.Usd <= 0m) return default;
      
      // buy when there are at least 3 decreasing candles followed by an increasing candle
      if (_candles.Count >= 4 &&
          _candles[_candles.Count - 1].IsBullish &&
          _candles[_candles.Count - 2].IsBearish &&
          _candles[_candles.Count - 3].IsBearish &&
          _candles[_candles.Count - 4].IsBearish)
      {
        var bearOpen = _candles[_candles.Count - 4].Open;
        var bearClose = _candles[_candles.Count - 2].Close;
        var bearPercent = 1.0m - (bearClose / bearOpen);

        var bullOpen = _candles[_candles.Count - 1].Open;
        var bullClose = _candles[_candles.Count - 1].Close;
        var bullPercent = (bullClose / bullOpen) - 1.0m;

        return new WantsToBuyResult
        {
          UsdToSpend = args.Usd,
          Note = $"Buying at CurrentPrice ({CurrentPrice.ToString("F2")}) because " + 
            $"after 3 bearish candles ({bearPercent.ToString("P1")} down from {bearOpen.ToString("F2")} to {bearClose.ToString("F2")}) " +
            $"there was a bullish candle ({bullPercent.ToString("P1")} up from {bullOpen.ToString("F2")} to {bullClose.ToString("F2")})",
        };
      }
      
      return default;
    }
    
    // does the bot want to sell coins right now? how many coins to sell? (assumes market price)
    // a non-null return value doesn't complete a sale; it just instructs the system how much to sell if it can
    public WantsToSellResult WantsToSell(WantsToSellArgs args)
    {
      // can't sell if I have no coins
      if (args.CoinCount <= 0m) return default;
      
      // Technically _lastPurchasedPerCoinPrice should be non null once coins are bought
      // but have a reasonable behavior for that scenairo anyway
      if (_lastPurchasedPerCoinPrice == null) return default;
      
      // if at any time the price drops below N% of where I bought, then sell as a safety measure
      if (CurrentPrice < _lastPurchasedPerCoinPrice.Value * (1 - _percentLossToJustSell))
      {
        return new WantsToSellResult
        {
          CoinCountToSell = args.CoinCount,
          Note = $"Selling as safety measure because CurrentPrice ({CurrentPrice.ToString("F2")}) " +
            $"dropped {_percentLossToJustSell.ToString("P1")} or more below _lastPurchasedPerCoinPrice ({_lastPurchasedPerCoinPrice?.ToString("F2")})",
        };
      }
      
      // coinbase takes a 0.2% transaction fee, so don't sell below that point
      // to ensure the bot always makes a profit
      if (CurrentPrice <= _lastPurchasedPerCoinPrice.Value * 1.002m)
      {
        return default;
      }

      // At this point in the logic, we're in the profit zone.
      // Sell when there's a decreasing candle
      if (_candles[_candles.Count - 1].IsBearish)
      {
        var profitPercent = CurrentPrice / _lastPurchasedPerCoinPrice.Value - 1.0m;
        
        var bearOpen = _candles[_candles.Count - 1].Open;
        var bearClose = _candles[_candles.Count - 1].Close;
        var bearPercent = 1.0m - (bearClose / bearOpen);

        return new WantsToSellResult
        {
          CoinCountToSell = args.CoinCount,
          Note = $"Selling at CurrentPrice ({CurrentPrice.ToString("F2")}) because we're " + 
            $"{profitPercent.ToString("P1")} up from LastPurchasePrice ({_lastPurchasedPerCoinPrice?.ToString("F2")}) " + 
            $"and there's a bearish candle ({bearPercent.ToString("P1")} down from {bearOpen.ToString("F2")} to {bearClose.ToString("F2")})",
        };
      }

      return default;
    }
    
    // notifies the bot that it successfully purchased X coins for Y dollars
    public void Bought(BoughtArgs args)
    {
      _lastPurchasedPerCoinPrice = args.PerCoinPriceIncludingFee;
    }
    
    // notifies the bot that it successfully sold X coins for Y dollars
    public void Sold(SoldArgs args)
    {
      _lastPurchasedPerCoinPrice = null;
    }
    
    // notifies the bot of the latest-fetched current crypto price
    public void ApplyCurrentPrice(CurrentPriceArgs args)
    {
      // this will never be invoked because this bot didn't specify that it wanted this info in GetConfig()
    }
    
    // Notifies the bot of the latest-produced ticker candle
    public void ApplyNextCandle(NextCandleArgs args)
    {
      while (_candles.Count > 5) _candles.RemoveAt(0);
      _candles.Add(args.Candle);
    }
  }
}