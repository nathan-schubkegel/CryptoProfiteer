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

    IEnumerable<PersistedHistoricalCoinPrice> ClonePersistedData();

    void ImportPersistedData(IEnumerable<PersistedHistoricalCoinPrice> persistedData);
    
    public ICollection<string> CoinbaseCoinTypes { get; }
  }

  public class HistoricalCoinPriceService : BackgroundService, IHistoricalCoinPriceService
  {
    private readonly IHttpClientSingleton _httpClientSingleton;
    private readonly ILogger<HistoricalCoinPriceService> _logger;
    private readonly Channel<object> _signal = Channel.CreateBounded<object>(1);
    
    private ICollection<string> _supportedCoinTypes = new HashSet<string>();
    
    public ICollection<string> CoinbaseCoinTypes => _supportedCoinTypes;
    
    private IReadOnlyDictionary<(string CoinType, DateTime Time, CryptoExchange Exchange), Decimal?> _historicalPrices =
      new Dictionary<(string CoinType, DateTime Time, CryptoExchange Exchange), Decimal?>();
      
    private ImmutableHashSet<(string CoinType, DateTime Time, CryptoExchange Exchange)> _neededPrices =
      ImmutableHashSet<(string CoinType, DateTime Time, CryptoExchange Exchange)>.Empty;
      
    private ImmutableDictionary<(string CoinType, DateTime Time, CryptoExchange Exchange), DateTime> _failedPrices =
      ImmutableDictionary<(string CoinType, DateTime Time, CryptoExchange Exchange), DateTime>.Empty;
    
    public HistoricalCoinPriceService(ILogger<HistoricalCoinPriceService> logger, IHttpClientSingleton httpClientSingleton)
    {
      _logger = logger;
      _httpClientSingleton = httpClientSingleton;
    }
    
    public Decimal? ToUsd(Decimal coinCount, string coinType, DateTime time, CryptoExchange exchange)
    {
      time = time.ChopSecondsAndSmaller();
      
      if (coinType == "USD")
      {
        return coinCount;
      }
      
      if (!_supportedCoinTypes.Contains(coinType))
      {
        return null;
      }
      
      if (time > DateTime.UtcNow)
      {
        return null;
      }
      
      if (_historicalPrices.TryGetValue((coinType, time, exchange), out var pricePerCoin))
      {
        if (pricePerCoin == null) return null;
        return coinCount * pricePerCoin;
      }
      
      if (_failedPrices.TryGetValue((coinType, time, exchange), out var whenLastTried))
      {
        // for now, just never try twice
        return null;
      }

      ImmutableInterlocked.Update(ref _neededPrices, t => t.Add((coinType, time, exchange)));
      _signal.Writer.TryWrite(null);
      return null;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      // Other services can't start until this service awaits, so await now!
      await Task.Yield();
      
      try
      {
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.exchange.coinbase.com/products");
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("User-Agent", HttpClientSingleton.UserAgent);
        await _httpClientSingleton.UseAsync("fetching coinbase trading pairs (for historical price service)", stoppingToken, async http => 
        {
          var response = await http.SendAsync(request);
          string responseBody = await response.Content.ReadAsStringAsync();
          if (!response.IsSuccessStatusCode)
          {
            throw new HttpRequestException($"coinbase pro api for all known trading pairs returned {response.StatusCode}: {responseBody}");
          }
          
          _logger.LogTrace("HistoricalCoinPriceService processed request for trading pairs, producing this data: {0}", responseBody);
            
          // FUTURE: The quote_increment field specifies the min order price as well as the price increment.
          // which we should be using when displaying the price-per-coin, or something maybe?

          var jArray = JArray.Parse(responseBody);

          // look for these
          //  "base_currency": "BTC",
          //  "quote_currency": "USD",
          
          var results = new HashSet<string>();
          foreach (JObject o in jArray) {
            if (o["quote_currency"]?.Value<string>() == "USD") {
              results.Add(o["base_currency"].Value<string>());
            }
          }
          _supportedCoinTypes = results;
        });
      }
      catch (OperationCanceledException)
      {
        // this is our fault for shutting down. Don't bother logging it.
        throw;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, $"{ex.GetType().Name} while fetching coinbase trading pairs: {ex.Message}");
      }

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

          foreach (var (coinType, time, exchange) in neededPrices.Where(x => !_historicalPrices.ContainsKey(x)))
          {
            try
            {
              var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.exchange.coinbase.com/products/{coinType}-USD/candles" +
                $"?granularity=60" +
                $"&start={time.Subtract(TimeSpan.FromMinutes(2)).ToString("o")}" +
                $"&end={time.Add(TimeSpan.FromMinutes(3)).ToString("o")}");
              request.Headers.Add("Accept", "application/json");
              request.Headers.Add("User-Agent", HttpClientSingleton.UserAgent);
              await _httpClientSingleton.UseAsync($"fetching historical price of {coinType} at {time}", stoppingToken, async http => 
              {
                var response = await http.SendAsync(request);
                string responseBody = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                  throw new HttpRequestException($"coinbase pro api for {coinType} candle at={time.ToString("o")} returned {response.StatusCode}: {responseBody}");
                }
                
                _logger.LogTrace("HistoricalCoinPriceService processed request for {0} at {1} from {2}, producing this data: {3}",
                  coinType, time, exchange, responseBody);

                Decimal? newPrice;
                var jArray = JArray.Parse(responseBody);
                if (jArray.Count == 0)
                {
                  // I think this means there were no trades (on record) for this time period
                  newPrice = null;
                }
                else
                {
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
                    var candleTime = SomeUtils.UnixEpochSecondsToDateTime(unixEpochSeconds);
                    var difference = candleTime.Subtract(time);
                    if (difference < closestDifference)
                    {
                      var low = candle[1].Value<Decimal>();
                      var high = candle[2].Value<Decimal>();
                      bestPrice = (low + high) / 2;
                      closestDifference = difference;
                    }
                  }
                  newPrice = bestPrice;
                }

                // TODO: could maybe try to synchronize changes to _historicalPrices here vs. in ImportPersistedData()
                var newDictionary = new Dictionary<(string CoinType, DateTime Time, CryptoExchange Exchange), Decimal?>(_historicalPrices);
                newDictionary[(coinType, time, exchange)] = newPrice;
                _historicalPrices = newDictionary;
                _logger.LogInformation($"determined pricePerCoinUsd = {(newPrice?.ToString() ?? "null")} for {coinType} at {time}");
              });
            }
            catch
            {
              ImmutableInterlocked.Update(ref _failedPrices, p => p.SetItem((coinType, time, exchange), DateTime.UtcNow));
              throw;
            }
            finally
            {
              ImmutableInterlocked.Update(ref _neededPrices, p => p.Remove((coinType, time, exchange)));
            }
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
    
    public IEnumerable<PersistedHistoricalCoinPrice> ClonePersistedData()
    {
      return _historicalPrices.Select(x => new PersistedHistoricalCoinPrice
      {
        CoinType = x.Key.CoinType,
        Time = x.Key.Time,
        Exchange = x.Key.Exchange,
        PricePerCoinUsd = x.Value
      });
    }
    
    public void ImportPersistedData(IEnumerable<PersistedHistoricalCoinPrice> persistedData)
    {
      // TODO: could maybe try to synchronize changes to _historicalPrices here vs. in ExecuteAsync()
      var newDictionary = new Dictionary<(string CoinType, DateTime Time, CryptoExchange Exchange), Decimal?>(_historicalPrices);
      foreach (var p in persistedData)
      {
        newDictionary[(p.CoinType, p.Time, p.Exchange)] = p.PricePerCoinUsd;
      }
      _historicalPrices = newDictionary;
    }
  }
}