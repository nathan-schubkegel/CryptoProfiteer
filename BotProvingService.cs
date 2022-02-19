using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace CryptoProfiteer
{
  // NOTE: this class is serialized to JSON for the front-end
  public class BotState
  {
    public DateTime Time { get; set; }
    public Decimal Usd { get; set; }
    public Decimal CoinCount { get; set; }
  }
  
  // NOTE: this class is serialized to JSON for the front-end
  public class BotProofResult
  {
    public List<BotState> BotStates { get; set; } = new List<BotState>();
    public Decimal FinalUsd { get; set; }
    public Decimal FinalCoinCount { get; set; }
    public bool IsSunk { get; set; }
  }
  
  public interface IBotProvingService
  {
    Task<BotProofResult> Prove(string botName, string coinType, Decimal initialUsd, DateTime startTime, DateTime endTime, CandleGranularity granularity, CancellationToken stoppingToken);
  }

  public class BotProvingService: IBotProvingService
  {
    private readonly ICandleService _candleService;
    private readonly ILogger<BotProvingService> _logger;
    
    public BotProvingService(ICandleService candleService, ILogger<BotProvingService> logger)
    {
      _candleService = candleService;
      _logger = logger;
    }
    
    public async Task<BotProofResult> Prove(string botName, string coinType, Decimal initialUsd, DateTime startTime, DateTime endTime, CandleGranularity granularity, CancellationToken stoppingToken)
    {
      var result = new BotProofResult();
      
      if (botName != "ThreeUpsThenDownBot") throw new Exception("unsupported bot name; only 'ThreeUpsThenDownBot' is supported now");
      var bot = new ThreeUpsThenDownBot(_logger, initialUsd);
      bot.CoinType = coinType;
      bot.Granularity = granularity;
      
      DateTime currentTime = startTime;
      if (endTime < startTime) throw new Exception("invalid end time earlier than start time");
      if (endTime - startTime > TimeSpan.FromDays(365)) throw new Exception("invalid end time too far after start time; only max 1 year is supported now");
      while (currentTime < endTime)
      {
        var id = PersistedCandleRangeId.Coinbase(coinType, currentTime, granularity);
        var range = await _candleService.TryGetCandleRangeAsync(id, stoppingToken);
        if (range == null)
        {
          throw new Exception("Failed to get candle range; see log for details");
        }
        for (int i = 0; i < range.Count; i++)
        {
          currentTime += TimeSpan.FromSeconds((int)granularity);
          var candle = range.TryGetCandle(i);
          if (candle != null)
          {
            bot.ApplyNextCandle(currentTime, candle.Value);
            var usdToSpend = bot.WantsToBuy();
            if (usdToSpend != null)
            {
              // buy coins with that money
              var price = candle.Value.Close;
              var fee = 0.002m * usdToSpend.Value;
              var coinsPurchased = (usdToSpend.Value - fee) / price;
              bot.Bought(coinsPurchased, usdToSpend.Value);
              // could keep track of coin count and USD to make sure the bot doesn't cheat, but meh
              result.BotStates.Add(new BotState { Time = currentTime, Usd = bot.Usd, CoinCount = bot.CoinCount });
            }
            else
            {
              var coinsToSell = bot.WantsToSell();
              if (coinsToSell != null)
              {
                // sell those coins
                var price = candle.Value.Close;
                var money = price * coinsToSell.Value;
                var fee = 0.002m * money;
                var usdGained = money - fee;
                bot.Sold(coinsToSell.Value, usdGained);
                // could keep track of coin count and USD to make sure the bot doesn't cheat, but meh
                result.BotStates.Add(new BotState { Time = currentTime, Usd = bot.Usd, CoinCount = bot.CoinCount });
              }
            }
          }
        }
      }
      result.FinalCoinCount = bot.CoinCount;
      result.FinalUsd = bot.Usd;
      result.IsSunk = bot.IsSunk;
      return result;
    }
  }
}

