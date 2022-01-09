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

    public Decimal? ToUsd(Decimal coinCount, string coinType, DateTime time, CryptoExchange exchange)
    {
      if (coinType == "USD" || coinType == "USDT")
      {
        return coinCount;
      }
      
      if (_pricePerCoinUsd.TryGetValue((coinType, time, exchange), out var pricePerCoin))
      {
        return coinCount * pricePerCoin;
      }
      else
      {
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
          
        again:
          var (coinType, time, exchange) = _neededPrices.Keys.FirstOrDefault();
          if (coinType != null && !_pricePerCoinUsd.ContainsKey((coinType, time, exchange)))
          {
            _neededPrices.Remove((coinType, time, exchange), out _);
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.exchange.coinbase.com/products/{coinType}-USD/candles" +
              $"?granularity=60" +
              $"&start={time.Subtract(TimeSpan.FromMinutes(2)).ToString("o")}" +
              $"&end={time.Add(TimeSpan.FromMinutes(3)).ToString("o")}");
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("User-Agent", "CryptoProfiteer/0.3.0");
            await HttpClientSingleton.UseAsync(stoppingToken, async http => 
            {
              var response = await http.SendAsync(request);
              string responseBody = await response.Content.ReadAsStringAsync();
              if (!response.IsSuccessStatusCode)
              {
                throw new HttpRequestException($"coinbase pro api for {coinType} candle at={time.ToString("o")} returned {response.StatusCode}: {responseBody}");
              }
              
              _logger.LogInformation($"HistoricalCoinPriceService processed request for {coinType} at {time} from {exchange}, producing this data:"
                + Environment.NewLine + responseBody);

              var jArray = JArray.Parse(responseBody);
              if (jArray.Count == 0)
              {
                throw new Exception($"coinbase pro api for {coinType} candle at={time.ToString("o")} returned empty candle set");
              }

              /*
              Apparently it just returns an array of arrays of numbers.
              0 - time - bucket start time
              1 - low - lowest price during the bucket interval
              2 - high - highest price during the bucket interval
              3 - open - opening price (first trade) in the bucket interval
              4 - close - closing price (last trade) in the bucket interval
              5 - volume - volume of trading activity during the bucket interval
              */
              var candle = (JArray)jArray[jArray.Count / 2];
              var low = candle[1].Value<Decimal>();
              var high = candle[2].Value<Decimal>();
              var average = (low + high) / 2;
              _pricePerCoinUsd[(coinType, time, exchange)] = average;
              _logger.LogInformation($"determined that pricePerCoinUsd = {average} for {coinType} at {time}");
            });
            
            goto again;
          }
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