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
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace CryptoProfiteer
{
  public interface IHistoricalCoinPriceService
  {
    Decimal? ToUsd(Decimal cost, string coinType, DateTime time, CryptoExchange exchange);

    IEnumerable<PersistedHistoricalCoinPrice> GetPersistedData();

    void ImportPersistedData(IEnumerable<PersistedHistoricalCoinPrice> persistedData);
  }

  public class HistoricalCoinPriceService : BackgroundService, IHistoricalCoinPriceService
  {
    private readonly ILogger<HistoricalCoinPriceService> _logger;
    private readonly Channel<object> _signal = Channel.CreateBounded<object>(1);
    
    private volatile IReadOnlyDictionary<(string CoinType, DateTime Time, CryptoExchange Exchange), Decimal> _pricePerCoinUsd =
      new Dictionary<(string CoinType, DateTime Time, CryptoExchange Exchange), Decimal>();
      
    private ImmutableHashSet<(string CoinType, DateTime Time, CryptoExchange Exchange)> _neededPrices =
      ImmutableHashSet<(string CoinType, DateTime Time, CryptoExchange Exchange)>.Empty;
    
    public HistoricalCoinPriceService(ILogger<HistoricalCoinPriceService> logger)
    {
      _logger = logger;
    }
    
    private DateTime ChopSecondsAndSmaller(DateTime time)
    {
      // expected format: 2021-01-06T06:07:54.31Z
      var s = time.ToString("o");

      // get everything before and after the seconds and milliseconds
      var beforeSeconds = s.Substring(0, 17);
      var end = 17; while (end < s.Length) { if (s[end] == '.' || (s[end] >= '0' && s[end] <= '9')) end++; else break; }
      var remainder = s.Substring(end, s.Length - end);

      // Change the seconds and milliseconds to 00
      return DateTime.Parse(beforeSeconds + "00" + remainder, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    public Decimal? ToUsd(Decimal coinCount, string coinType, DateTime time, CryptoExchange exchange)
    {
      time = ChopSecondsAndSmaller(time);
      
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
        ImmutableInterlocked.Update(ref _neededPrices, t => t.Add((coinType, time, exchange)));
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
          var neededPrices = Interlocked.CompareExchange(ref _neededPrices, null, null);
          if (neededPrices.Count == 0)
          {
            await _signal.Reader.ReadAsync(stoppingToken);
            continue;
          }

          foreach (var (coinType, time, exchange) in neededPrices.Where(x => !_pricePerCoinUsd.ContainsKey(x)))
          {
            ImmutableInterlocked.Update(ref _neededPrices, p => p.Remove((coinType, time, exchange)));
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
              
              _logger.LogTrace("HistoricalCoinPriceService processed request for {0} at {1} from {2}, producing this data: {3}",
                coinType, time, exchange, responseBody);

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
              Decimal bestPrice = 0m;
              TimeSpan closestDifference = DateTime.MaxValue.Subtract(DateTime.MinValue);
              if (jArray.Count == 0) throw new Exception($"No sale history found for {coinType} at {time} on {exchange}");
              foreach (JArray candle in jArray)
              {
                var unixEpochSeconds = candle[0].Value<long>();
                var candleTime = UnixEpochSecondsToDateTime(unixEpochSeconds);
                var difference = candleTime.Subtract(time);
                if (difference < closestDifference)
                {
                  var low = candle[1].Value<Decimal>();
                  var high = candle[2].Value<Decimal>();
                  bestPrice = (low + high) / 2;
                  closestDifference = difference;
                }
              }

              var newDictionary = new Dictionary<(string CoinType, DateTime Time, CryptoExchange Exchange), Decimal>(_pricePerCoinUsd);
              newDictionary[(coinType, time, exchange)] = bestPrice;
              _pricePerCoinUsd = newDictionary;
              _logger.LogInformation($"determined pricePerCoinUsd = {bestPrice} for {coinType} at {time}");
            });
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
    
    private static DateTime UnixEpochSecondsToDateTime(long unixEpochSeconds)
    {
      DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
      return dateTime.AddSeconds(unixEpochSeconds);
    }
    
    public IEnumerable<PersistedHistoricalCoinPrice> GetPersistedData()
    {
      return _pricePerCoinUsd.Select(x => new PersistedHistoricalCoinPrice
      {
        CoinType = x.Key.CoinType,
        Time = x.Key.Time,
        Exchange = x.Key.Exchange,
        PricePerCoinUsd = x.Value
      });
    }
    
    public void ImportPersistedData(IEnumerable<PersistedHistoricalCoinPrice> persistedData)
    {
      var newDictionary = new Dictionary<(string CoinType, DateTime Time, CryptoExchange Exchange), Decimal>(_pricePerCoinUsd);
      foreach (var p in persistedData)
      {
        newDictionary[(p.CoinType, p.Time, p.Exchange)] = p.PricePerCoinUsd;
      }
      _pricePerCoinUsd = newDictionary;
    }
  }
}