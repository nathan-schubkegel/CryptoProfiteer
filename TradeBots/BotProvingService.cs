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
using CryptoProfiteer.TradeBots.Messages;

namespace CryptoProfiteer.TradeBots
{
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
      var bot = new ThreeUpsThenDownBot(coinType, granularity);
      
      var heldUsd = initialUsd;
      var heldCoins = 0m;
      var isSunk = false;
      const Decimal feePercent = 0.002m;

      var config = bot.GetConfig();
      if (config.Exchange != CryptoExchange.Coinbase)
      {
        throw new Exception("Unsupported exchange; only 'Coinbase' is supported now.");
      }

      Candle? lastCandle = null;
      DateTime currentTime = startTime;
      if (endTime < startTime) throw new Exception("invalid end time earlier than start time");
      if (endTime - startTime > TimeSpan.FromDays(366)) throw new Exception("invalid end time too far after start time; only max 1 year is supported now");
      while (currentTime < endTime && !isSunk)
      {
        var id = PersistedCandleRangeId.Coinbase(coinType, currentTime, granularity);
        var candleRange = await _candleService.TryGetCandleRangeAsync(id, stoppingToken);
        if (candleRange == null)
        {
          throw new Exception("Failed to get candle range; see log for details");
        }
        for (int i = 0; i < candleRange.Count && currentTime < endTime && !isSunk; i++)
        {
          currentTime += TimeSpan.FromSeconds((int)granularity);
          var candle = candleRange.TryGetCandle(i);
          if (candle != null)
          {
            var perCoinPrice = candle.Value.Close;

            lastCandle = candle;
            bot.ApplyNextCandle(new NextCandleArgs { Time = currentTime, Candle = candle.Value });
            if (heldUsd > 0)
            {
              var buyResult = bot.WantsToBuy(new WantsToBuyArgs
              {
                Usd = heldUsd,
                CoinCount = heldCoins,
                PerCoinPrice = perCoinPrice,
                FeePercent = feePercent,
              });
              if (buyResult.UsdToSpend != null)
              {
                if (buyResult.UsdToSpend <= 0 || buyResult.UsdToSpend > heldUsd)
                {
                  throw new Exception($"Invalid bot operation; it tried to spend ${buyResult.UsdToSpend} when really ${heldUsd} was available.");
                }
                // buy coins with that money
                var fee = feePercent * buyResult.UsdToSpend.Value;
                var coinsPurchased = (buyResult.UsdToSpend.Value - fee) / perCoinPrice;
                heldUsd -= buyResult.UsdToSpend.Value;
                heldCoins += coinsPurchased;
                bot.Bought(new BoughtArgs
                {
                  SpentUsd = buyResult.UsdToSpend.Value,
                  BoughtCoinCount = coinsPurchased,
                  Usd = heldUsd,
                  CoinCount = heldCoins,
                  FeeUsd = fee,
                  PerCoinPriceIncludingFee = buyResult.UsdToSpend.Value / coinsPurchased,
                  PerCoinPriceBeforeFee = perCoinPrice,
                });
                result.BotStates.Add(new BotState
                { 
                  Time = currentTime,
                  Usd = heldUsd,
                  CoinCount = heldCoins,
                  Note = buyResult.Note,
                });
              }
            }
            
            if (heldCoins > 0)
            {
              var sellResult = bot.WantsToSell(new WantsToSellArgs
              {
                Usd = heldUsd,
                CoinCount = heldCoins,
                PerCoinPrice = perCoinPrice,
                FeePercent = feePercent,
              });
              if (sellResult.CoinCountToSell != null)
              {
                if (sellResult.CoinCountToSell <= 0 || sellResult.CoinCountToSell > heldCoins)
                {
                  throw new Exception($"Invalid bot operation; it tried to sell {sellResult.CoinCountToSell} {coinType} when really {heldCoins} {coinType} was available.");
                }
                // sell those coins
                var money = perCoinPrice * sellResult.CoinCountToSell.Value;
                var fee = feePercent * money;
                var usdGained = money - fee;
                heldUsd += usdGained;
                heldCoins -= sellResult.CoinCountToSell.Value;
                bot.Sold(new SoldArgs
                {
                  EarnedUsd = usdGained,
                  SoldCoinCount = sellResult.CoinCountToSell.Value,
                  Usd = heldUsd,
                  CoinCount = heldCoins,
                  FeeUsd = fee,
                  PerCoinPriceIncludingFee = usdGained / sellResult.CoinCountToSell.Value,
                  PerCoinPriceBeforeFee = perCoinPrice,
                });
                result.BotStates.Add(new BotState
                {
                  Time = currentTime,
                  Usd = heldUsd,
                  CoinCount = heldCoins,
                  Note = sellResult.Note,
                });
              }
            }

            var totalValue = heldUsd + heldCoins * perCoinPrice;
            isSunk = totalValue < 0.80m * initialUsd;
          }
        }
      }
      
      // sell any still-held coins
      // so I can see what the bot had (in USD) at the end of the run
      if (heldCoins > 0)
      {
        var coinCountToSell = heldCoins;
        var price = lastCandle.Value.Close;
        var money = price * heldCoins;
        var fee = feePercent * money;
        var usdGained = money - fee;
        heldUsd += usdGained;
        heldCoins = 0;
        bot.Sold(new SoldArgs
        {
          EarnedUsd = usdGained,
          SoldCoinCount = heldCoins,
          Usd = heldUsd,
          CoinCount = heldCoins,
          FeeUsd = fee,
          PerCoinPriceIncludingFee = usdGained / coinCountToSell,
          PerCoinPriceBeforeFee = price,
        });
        result.BotStates.Add(new BotState
        {
          Time = currentTime,
          Usd = heldUsd,
          CoinCount = heldCoins,
          Note = $"Forced to sell at CurrentPrice ({price}) because we're at the end of the simulation",
        });
      }
      
      result.FinalCoinCount = heldCoins;
      result.FinalUsd = heldUsd;
      result.IsSunk = heldUsd < 0.80m * initialUsd;
      return result;
    }
  }
}

