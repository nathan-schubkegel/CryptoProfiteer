
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace CryptoProfiteer
{
  // This bot tries to buy when a candle goes bearish after 3 bullish candles,
  // and tries to sell when a candle goes bearish
  public class ThreeUpsThenDownBot : ITradeBot
  {
    private readonly ILogger _logger;
    
    public ThreeUpsThenDownBot(ILogger logger, Decimal initialFunds)
    {
      _logger = logger;
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
    public Decimal? WantsToBuy()
    {
      // don't buy while I'm holding coins
      if (CoinCount > 0m) return null;
      
      // can't buy if I'm out of money
      if (Usd <= 0m) return null;
      
      // don't buy if I've had a 20% losing streak; consider the bot sunk at that point
      if (IsSunk) return null;

      // buy when there are at least 3 decreasing candles followed by an increasing candle
      if (_candles.Count >= 4 &&
          _candles[_candles.Count - 1].IsBullish &&
          _candles[_candles.Count - 2].IsBearish &&
          _candles[_candles.Count - 3].IsBearish &&
          _candles[_candles.Count - 4].IsBearish)
      {
        _logger.LogInformation("Buying because we're at a bullish candle after 3 bearish candles"); 
        return Usd;
      }
      
      return null;
    }
    
    // does the bot want to sell coins right now? how many coins to sell? (assumes market price)
    // a non-null return value doesn't complete a sale; it just instructs the system how much to sell if it can
    public Decimal? WantsToSell()
    {
      // can't sell if I have no coins
      if (CoinCount <= 0m) return null;
      
      // I don't know why these would be null... I just gotta ignore these scenarios for now...
      if (CurrentPrice == null) return null;
      if (LastPurchasedPerCoinPrice == null) return null;
      
      // if at any time the price drops below 5% of where I bought, then sell as a safety measure
      if (CurrentPrice.Value * 0.95m > LastPurchasedPerCoinPrice.Value)
      {
        _logger.LogInformation($"Selling because CurrentPrice ({CurrentPrice}) * 0.95m > LastPurchasedPerCoinPrice ({LastPurchasedPerCoinPrice})");
        return CoinCount;
      }
      
      // if at least 0.4% profit has been possible at some point
      if (MaxObservedPotentialPerCoinPrice.Value >= 1.004m * LastPurchasedPerCoinPrice.Value)
      {
        // if at any time the profit drops below 50% of the max possible profit
        // then sell as a profit-guaranteeing measure
        var limit = (MaxObservedPotentialPerCoinPrice.Value - LastPurchasedPerCoinPrice.Value) * 0.5m + LastPurchasedPerCoinPrice.Value;
        if (CurrentPrice.Value <= limit)
        {
          _logger.LogInformation($"Selling because CurrentPrice ({CurrentPrice}) <= limit ({limit})");
          return CoinCount;
        }
        else
        {
          _logger.LogInformation($"max observed price >= 0.4% up, but CurrentPrice ({CurrentPrice}) > ");
        }
      }

      /*
      // sell when there's a decreasing candle
      if (_candles[_candles.Count - 1].IsBearish)
      {
        _logger.LogInformation($"Selling because there's a bearish candle");
        return CoinCount;
      }
      */
      
      return null;
    }
    
    // notifies the bot that it successfully purchased X coins for Y dollars
    public void Bought(Decimal coinCount, Decimal usd)
    {
      Usd = Math.Max(0m, Usd - usd);
      MaxUsdHeld = Math.Max(MaxUsdHeld, Usd);
      CoinCount = Math.Max(0m, CoinCount + coinCount);
      LastPurchasedPerCoinPrice = usd / coinCount;
      MaxObservedPotentialPerCoinPrice = LastPurchasedPerCoinPrice;
    }
    
    // notifies the bot that it successfully sold X coins for Y dollars
    public void Sold(Decimal coinCount, Decimal usd)
    {
      Usd = Math.Max(0m, Usd + usd);
      MaxUsdHeld = Math.Max(MaxUsdHeld, Usd);
      CoinCount = Math.Max(0m, CoinCount - coinCount);
      LastPurchasedPerCoinPrice = null;
      MaxObservedPotentialPerCoinPrice = null;
    }
    
    // notifies the bot of the latest-fetched current crypto price
    public void ApplyCurrentPrice(DateTime time, Decimal price)
    {
      CurrentPrice = price;
      CurrentPriceTime = time;
      if (MaxObservedPotentialPerCoinPrice != null)
      {
        MaxObservedPotentialPerCoinPrice = Math.Max(MaxObservedPotentialPerCoinPrice.Value, CurrentPrice.Value);
      }
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
    public Decimal? MaxObservedPotentialPerCoinPrice { get; private set; }
  }
}