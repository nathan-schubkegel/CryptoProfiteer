
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace CryptoProfiteer
{
  // This bot tries to buy when a candle goes bearish after 3 bullish candles,
  // and tries to sell when a candle goes bearish
  public class ThreeUpsThenDownBot : ITradeBot
  {
    public ThreeUpsThenDownBot(Decimal initialFunds)
    {
      Usd = initialFunds;
      MaxUsdHeld = initialFunds;
    }
    
    // which crypto exchange this bot operates on
    public CryptoExchange Exchange { get; } = CryptoExchange.Coinbase;
    
    // what timespan of candles this bot operates on
    public CandleGranularity Granularity { get; set; } = CandleGranularity.Minutes;
    
    // what frequency the bot desires to learn the current price of its crypto
    public int? CurrentPriceFrequencySeconds { get; set; } = 10;
    
    // which crypto this bot buys/sells
    public string CoinType { get; set; } = "BTC";
    
    // how much of the crypto coin this bot currently holds
    public Decimal CoinCount { get; private set; }
    
    // how much cash this bot currently holds
    public Decimal Usd { get; private set; }

    // if true, then the bot has self-destructed from taking on too many losses
    public bool IsSunk => CoinCount <= 0m && Usd <= MaxUsdHeld * 0.8m;

    // does the bot want to buy coins right now? how much money to spend? (assumes market price)
    // a non-null return value doesn't complete a purchase; it just instructs the system how much to buy if it can
    public (Decimal? usd, string note) WantsToBuy()
    {
      // don't buy while I'm holding coins
      if (CoinCount > 0m) return (null, null);
      
      // can't buy if I'm out of money
      if (Usd <= 0m) return (null, null);
      
      // don't buy if I've had a 20% losing streak; consider the bot sunk at that point
      if (IsSunk) return (null, null);

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

        return (Usd, $"Buying at CurrentPrice ({CurrentPrice?.ToString("F2")}) because " + 
          $"after 3 bearish candles ({bearPercent.ToString("P1")} down from {bearOpen.ToString("F2")} to {bearClose.ToString("F2")}) " +
          $"there was a bullish candle ({bullPercent.ToString("P1")} up from {bullOpen.ToString("F2")} to {bullClose.ToString("F2")})");
      }
      
      return (null, null);
    }
    
    // does the bot want to sell coins right now? how many coins to sell? (assumes market price)
    // a non-null return value doesn't complete a sale; it just instructs the system how much to sell if it can
    public (Decimal? coinCount, string note) WantsToSell()
    {
      // can't sell if I have no coins
      if (CoinCount <= 0m) return (null, null);
      
      // Technically CurrentPrice should never be null when WantsToSell() is invoked
      // but have a reasonable behavior for that scenario anyway
      if (CurrentPrice == null) return (null, null);

      // Technically LastPurchasedPerCoinPrice should be non null once coins are bought
      // but have a reasonable behavior for that scenairo anyway
      if (LastPurchasedPerCoinPrice == null) return (null, null);
      
      // if at any time the price drops below 5% of where I bought, then sell as a safety measure
      if (CurrentPrice.Value < LastPurchasedPerCoinPrice.Value * 0.95m)
      {
        return (CoinCount, $"Selling as safety measure because CurrentPrice ({CurrentPrice?.ToString("F2")}) " +
          $"dropped 5% or more below LastPurchasedPerCoinPrice ({LastPurchasedPerCoinPrice?.ToString("F2")})");
      }
      
      // coinbase takes a 0.2% transaction fee, so don't sell below that point
      // to ensure the bot always makes a profit
      if (CurrentPrice.Value <= LastPurchasedPerCoinPrice.Value * 1.002m)
      {
        return (null, null);
      }

      // At this point in the logic, we're in the profit zone.
      // Sell when there's a decreasing candle
      if (_candles[_candles.Count - 1].IsBearish)
      {
        var profitPercent = CurrentPrice.Value / LastPurchasedPerCoinPrice.Value - 1.0m;
        
        var bearOpen = _candles[_candles.Count - 1].Open;
        var bearClose = _candles[_candles.Count - 1].Close;
        var bearPercent = 1.0m - (bearClose / bearOpen);

        return (CoinCount, $"Selling at CurrentPrice ({CurrentPrice?.ToString("F2")}) because we're " + 
          $"{profitPercent.ToString("P1")} up from LastPurchasePrice ({LastPurchasedPerCoinPrice?.ToString("F2")}) " + 
          $"and there's a bearish candle ({bearPercent.ToString("P1")} down from {bearOpen.ToString("F2")} to {bearClose.ToString("F2")})");
      }

      return (null, null);
    }
    
    // notifies the bot that it successfully purchased X coins for Y dollars
    public void Bought(Decimal coinCount, Decimal usd)
    {
      Usd = Math.Max(0m, Usd - usd);
      MaxUsdHeld = Math.Max(MaxUsdHeld, Usd);
      CoinCount = Math.Max(0m, CoinCount + coinCount);
      LastPurchasedPerCoinPrice = usd / coinCount;
    }
    
    // notifies the bot that it successfully sold X coins for Y dollars
    public void Sold(Decimal coinCount, Decimal usd)
    {
      Usd = Math.Max(0m, Usd + usd);
      MaxUsdHeld = Math.Max(MaxUsdHeld, Usd);
      CoinCount = Math.Max(0m, CoinCount - coinCount);
      LastPurchasedPerCoinPrice = null;
    }
    
    // notifies the bot of the latest-fetched current crypto price
    public void ApplyCurrentPrice(DateTime time, Decimal price)
    {
      CurrentPrice = price;
      CurrentPriceTime = time;
    }
    
    // Notifies the bot of the latest-produced ticker candle
    public void ApplyNextCandle(DateTime time, Candle candle)
    {
      while (_candles.Count > 5) _candles.RemoveAt(0);
      _candles.Add(candle);
      
      if (CurrentPriceTime == null || CurrentPriceTime.Value < time)
      {
        ApplyCurrentPrice(time, candle.Close);
      }
    }

    public DateTime? CurrentPriceTime { get; private set; }
    public Decimal? CurrentPrice { get; private set; }
    public Decimal MaxUsdHeld { get; private set; }
    private readonly List<Candle> _candles = new List<Candle>();
    public Decimal? LastPurchasedPerCoinPrice { get; private set; }
  }
}