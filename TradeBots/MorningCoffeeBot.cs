
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using CryptoProfiteer.TradeBots.Messages;

namespace CryptoProfiteer.TradeBots
{
  // This bot tests my hypothesis that
  // 1.) Crypto trades a single direction (up or down) in the morning hours between 5:00am and 8:00am pacific M-F
  // 2.) The direction is established by the 1st hour, and carries into all 3 hours
  // So its behavior is "conditionally buy at 6:00am based on the change since 5:00am, and then sell at 8:00am"
  public class MorningCoffeeBot : ITradeBot
  {
    private readonly ConfigResult _config;
    private Decimal? _fiveAmPrice;
    private Decimal? _sixAmPrice;
    private Decimal? _eightAmPrice;

    public MorningCoffeeBot(string coinType)
    {
      _config = new ConfigResult
      {
        CoinType = coinType,
        Exchange = CryptoExchange.CoinbasePro,
        CandleGranularity = CandleGranularity.Hours,
      };
    }
    
    // the runner asks the bot for this info at the start of a trading session
    // to determine stuff like what coin the runner should watch and try to buy
    public ConfigResult GetConfig() => _config;
    
    // Notifies the bot of the latest-produced ticker candle
    public void ApplyNextCandle(NextCandleArgs args)
    {
      if (args.EndTime.Kind != DateTimeKind.Utc)
      {
        throw new Exception("gotta have utc dates, mang!");
      }
      
      //System.Console.WriteLine("ApplyNextCandle() args.EndTime.TimeOfDay is " + args.EndTime.TimeOfDay);

      // 5 am PST is 1 pm UTC
      if (_fiveAmPrice == null && 
          args.EndTime.TimeOfDay <= TimeSpan.FromHours(13) + TimeSpan.FromMinutes(5) &&
          args.EndTime.TimeOfDay >= TimeSpan.FromHours(13) - TimeSpan.FromMinutes(5))
      {
        _fiveAmPrice = args.Candle.Close;
        //System.Console.WriteLine("found 5am price at " + args.EndTime + " = " + args.Candle.Close);
      }

      if (_sixAmPrice == null && 
          args.EndTime.TimeOfDay <= TimeSpan.FromHours(14) + TimeSpan.FromMinutes(5) &&
          args.EndTime.TimeOfDay >= TimeSpan.FromHours(14) - TimeSpan.FromMinutes(5))
      {
        _sixAmPrice = args.Candle.Close;
        //System.Console.WriteLine("found 6am price at " + args.EndTime + " = " + args.Candle.Close);
      }

      if (_eightAmPrice == null && 
          args.EndTime.TimeOfDay <= TimeSpan.FromHours(16) + TimeSpan.FromMinutes(5) &&
          args.EndTime.TimeOfDay >= TimeSpan.FromHours(16) - TimeSpan.FromMinutes(5))
      {
        _eightAmPrice = args.Candle.Close;
        //System.Console.WriteLine("found 8am price at " + args.EndTime + " = " + args.Candle.Close);
      }
      
      // reset in the later half of the day
      if (args.EndTime.TimeOfDay >= TimeSpan.FromHours(0) &&
          args.EndTime.TimeOfDay <= TimeSpan.FromHours(10) &&
          _fiveAmPrice != null)
      {
        _fiveAmPrice = null;
        _sixAmPrice = null;
        _eightAmPrice = null;
        //System.Console.WriteLine("wiping price knowledge at " + args.EndTime + " = " + args.Candle.Close);
      }
    }

    // does the bot want to buy coins right now? how much money to spend? (assumes market price)
    // a non-null return value doesn't complete a purchase; it just instructs the system how much to buy if it can
    public WantsToBuyResult WantsToBuy(WantsToBuyArgs args)
    {
      // buy when the 6:00am price is higher than the 5:00am price and we haven't bought yet
      if (_fiveAmPrice != null && 
          _sixAmPrice != null && 
          _eightAmPrice == null && 
          _sixAmPrice > _fiveAmPrice * 1.01m &&
          args.CoinCount == 0)
      {
        return new WantsToBuyResult
        {
          UsdToSpend = args.Usd,
          Note = $"Buying because 5:00am price = {_fiveAmPrice} and 6:00am price = {_sixAmPrice}",
        };
      }

      return default;
    }
    
    // does the bot want to sell coins right now? how many coins to sell? (assumes market price)
    // a non-null return value doesn't complete a sale; it just instructs the system how much to sell if it can
    public WantsToSellResult WantsToSell(WantsToSellArgs args)
    {
      // sell if we're holding when 8:00am strikes
      if (_eightAmPrice != null && args.CoinCount > 0)
      {
        return new WantsToSellResult
        {
          CoinCountToSell = args.CoinCount,
          Note = $"Selling blindly at 8:00am price = {_eightAmPrice}",
        };
      }

      return default;
    }
    
    // notifies the bot that it successfully purchased X coins for Y dollars
    public void Bought(BoughtArgs args)
    {
      // ok
    }
    
    // notifies the bot that it successfully sold X coins for Y dollars
    public void Sold(SoldArgs args)
    {
      _fiveAmPrice = null;
      _sixAmPrice = null;
      _eightAmPrice = null;
    }
  }
}