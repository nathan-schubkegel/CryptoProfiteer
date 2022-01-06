using Microsoft.Extensions.Hosting;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace CryptoProfiteer
{
  public interface IHistoricalCoinPriceService
  {
    Decimal? ToUsd(Decimal cost, string coinType, DateTime time, CryptoExchange exchange);
  }

  public class HistoricalCoinPriceService : BackgroundService, IHistoricalCoinPriceService
  {
    private readonly ILogger<HistoricalCoinPriceService> _logger;
    private readonly Channel<object> _signal = Channel.CreateBounded<object>(1);
    
    private readonly ConcurrentDictionary<(string CoinType, DateTime Time, CryptoExchange Exchange), Decimal> _pricePerCoinUsd =
      new ConcurrentDictionary<(string CoinType, DateTime Time, CryptoExchange Exchange), Decimal>();
      
    private readonly ConcurrentDictionary<(string CoinType, DateTime Time, CryptoExchange Exchange), object> _neededPrices =
      new ConcurrentDictionary<(string CoinType, DateTime Time, CryptoExchange Exchange), object>();
    
    public HistoricalCoinPriceService(ILogger<HistoricalCoinPriceService> logger)
    {
      _logger = logger;
    }

    public Decimal? ToUsd(Decimal cost, string coinType, DateTime time, CryptoExchange exchange)
    {
      if (coinType == "USD" || coinType == "USDT")
      {
        _logger.LogInformation($"simple answer for USD/USDT cost of {cost}");
        return cost;
      }
      
      if (_pricePerCoinUsd.TryGetValue((coinType, time, exchange), out var pricePerCoin))
      {
        try
        {
          var answer = cost / pricePerCoin;
          _logger.LogInformation($"fetched answer for {cost} {coinType}: pricePerCoin= ${pricePerCoin}, answer = ${answer}");
          return answer;
        }
        catch
        {
          _logger.LogInformation($"division failure for {cost} {coinType}");
          return null;
        }
      }
      else
      {
        _logger.LogInformation($"signaling needs prices for {cost} {coinType}");
        _neededPrices[(coinType, time, exchange)] = null;
        _signal.Writer.TryWrite(null);
        return null;
      }
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      // Other services can't start until this service awaits, so await now!
      await Task.Yield();

      while (!stoppingToken.IsCancellationRequested)
      {
        try
        {
          await _signal.Reader.ReadAsync(stoppingToken);
          _logger.LogInformation($"encountered a signal!");
          
        again:
          var (coinType, time, exchange) = _neededPrices.Keys.FirstOrDefault();
          if (coinType != null)
          {
            if (exchange == CryptoExchange.Coinbase)
            {
              var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.exchange.coinbase.com/products/{coinType}-USD/candles" +
                $"?granularity=60" +
                $"&start={time.Subtract(TimeSpan.FromMinutes(2)).ToString("o")}" +
                $"&end={time.Add(TimeSpan.FromMinutes(3)).ToString("o")}");
              request.Headers.Add("Accept", "application/json");
              await HttpClientSingleton.UseAsync(stoppingToken, async http => 
              {
                _logger.LogInformation($"got into UseAsync");
                var response = await http.SendAsync(request);
                string responseBody = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                  throw new HttpRequestException($"coinbase pro api for {coinType} candle at={time.ToString("o")} returned {response.StatusCode}: {responseBody}");
                }
                var jArray = JArray.Parse(responseBody);
                if (jArray.Count == 0)
                {
                  throw new Exception($"coinbase pro api for {coinType} candle at={time.ToString("o")} returned empty candle set");
                }
                var candle = (JObject)jArray[jArray.Count / 2];
                var low = candle["low"].Value<Decimal>();
                var high = candle["high"].Value<Decimal>();
                var average = low + high / 2;
                _pricePerCoinUsd[(coinType, time, exchange)] = average;
                _neededPrices.Remove((coinType, time, exchange), out _);
              });
            }
            
            // future - handle kucoin
            
            goto again;
          }
          _logger.LogInformation($"out of requests");
        }
        catch (OperationCanceledException)
        {
          // this is our fault for shutting down. Don't bother logging it.
          throw;
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, $"{ex.GetType().Name} while fetching historical coin prices: {ex.Message}");
        }
      }
    }
  }
}