using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
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
    Decimal? ToUsd(Decimal coinCount, string coinType, DateTime time);

    IEnumerable<PersistedHistoricalCoinPrice> ClonePersistedData();

    void ImportPersistedData(IEnumerable<PersistedHistoricalCoinPrice> persistedData);
    
    IEnumerable<string> CoinbaseCoinTypes { get; }
  }

  public class HistoricalCoinPriceService : BackgroundService, IHistoricalCoinPriceService
  {
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientSingleton _httpClientSingleton;
    private readonly ILogger<HistoricalCoinPriceService> _logger;
    private readonly Channel<object> _signal = Channel.CreateBounded<object>(1);
    private readonly object _historicalPricesUpdaterGuard = new object();

    private Dictionary<string, HashSet<CryptoExchange>> _coinTypesSupportedByWhichExchanges = new Dictionary<string, HashSet<CryptoExchange>>();

    public IEnumerable<string> CoinbaseCoinTypes => _coinTypesSupportedByWhichExchanges
      .Where(x => x.Value.Contains(CryptoExchange.Coinbase))
      .Select(x => x.Key);
    
    // null PriceUsd means all exchanges have no data for the coin at that time
    private IReadOnlyDictionary<(string CoinType, DateTime Time), (CryptoExchange Exchange, Decimal? PriceUsd)> _historicalPrices =
      new Dictionary<(string CoinType, DateTime Time), (CryptoExchange Exchange, Decimal? PriceUsd)>();
      
    private ImmutableHashSet<(string CoinType, DateTime Time)> _neededPrices =
      ImmutableHashSet<(string CoinType, DateTime Time)>.Empty;
      
    private ImmutableHashSet<(string CoinType, DateTime Time)> _exchangeFailedPrices =
      ImmutableHashSet<(string CoinType, DateTime Time)>.Empty;
      
    public HistoricalCoinPriceService(IServiceProvider serviceProvider, ILogger<HistoricalCoinPriceService> logger, IHttpClientSingleton httpClientSingleton)
    {
      _serviceProvider = serviceProvider;
      _logger = logger;
      _httpClientSingleton = httpClientSingleton;
    }
    
    private void ReplaceHistoricalPrices(Action<Dictionary<(string CoinType, DateTime Time), (CryptoExchange Exchange, Decimal? PriceUsd)>> mutator)
    {
      lock (_historicalPricesUpdaterGuard)
      {
        var newDictionary = new Dictionary<(string CoinType, DateTime Time), (CryptoExchange Exchange, Decimal? PriceUsd)>(_historicalPrices);
        mutator(newDictionary);
        _historicalPrices = newDictionary;
      }
    }

#if false
    const string coinTypeToLog = "SAND";
#else
    const string coinTypeToLog = null;
#endif

    public Decimal? ToUsd(Decimal coinCount, string coinType, DateTime time)
    {
      time = time.ChopSecondsAndSmaller();

      if (coinType == "USD")
      {
        return coinCount;
      }

      if (time > DateTime.UtcNow)
      {
        if (coinType == coinTypeToLog) _logger.LogInformation($"Declining ToUsd({coinTypeToLog}, {time}) because time is in the future");
        return null;
      }

      if (_historicalPrices.TryGetValue((coinType, time), out var historicalPrice))
      {
        if (historicalPrice.PriceUsd == null)
        {
          // this means all exchanges had no data for that coin for that time
          if (coinType == coinTypeToLog) _logger.LogInformation($"Declining ToUsd({coinTypeToLog}, {time}) because the exchanges have no data for the requested time");
          return null;
        }
      
        if (coinType == coinTypeToLog) _logger.LogInformation($"Successfully returning stored ToUsd({coinTypeToLog}, {time})");
        return coinCount * historicalPrice.PriceUsd.Value;
      }

      if (_exchangeFailedPrices.Contains((coinType, time)))
      {
        // this means all exchanges rejected the request for that coin at that time
        if (coinType == coinTypeToLog) _logger.LogInformation($"Declining ToUsd({coinTypeToLog}, {time}) because the exchanges have no data for the requested time");
        return null;
      }

      if (!_coinTypesSupportedByWhichExchanges.ContainsKey(coinType))
      {
        if (coinType == coinTypeToLog) _logger.LogInformation($"Declining ToUsd({coinTypeToLog}, {time}) because _coinTypesSupportedByWhichExchanges doesn't have {coinTypeToLog}");
        return null;
      }
      
      if (coinType == coinTypeToLog) _logger.LogInformation($"Declining ToUsd({coinTypeToLog}, {time}) because that coin+time hasn't been fetched yet; queuing...");

      ImmutableInterlocked.Update(ref _neededPrices, t => t.Add((coinType, time)));
      _signal.Writer.TryWrite(null);

      return null;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      // Other services can't start until this service awaits, so await now!
      await Task.Yield();
      
      using var stopRegistration = stoppingToken.Register(() => _signal.Writer.TryComplete());
      
      // get coinbase supported coin types
      try
      {
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.exchange.coinbase.com/products");
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("User-Agent", HttpClientSingleton.UserAgent);
        await _httpClientSingleton.UseAsync("fetching coinbase trading pairs (for historical price service)", stoppingToken, async http => 
        {
          var response = await http.SendAsync(request, stoppingToken);
          string responseBody = await response.Content.ReadAsStringAsync(stoppingToken);
          if (!response.IsSuccessStatusCode)
          {
            throw new HttpRequestException($"coinbase pro api for all known trading pairs returned {response.StatusCode}: {responseBody}");
          }
          
          _logger.LogTrace("HistoricalCoinPriceService processed request for coinbase pro trading pairs, producing this data: {0}", responseBody);
            
          // FUTURE: The quote_increment field specifies the min order price as well as the price increment.
          // which we should be using when displaying the price-per-coin, or something maybe?

          var jArray = JArray.Parse(responseBody);

          // typical response:
          //  {
          //    "id": "AVT-USD",
          //    "base_currency": "AVT",
          //    "quote_currency": "USD",
          //    "quote_increment": "0.01",
          //    "base_increment": "0.01",
          //    "display_name": "AVT/USD",
          //    "min_market_funds": "1",
          //    "margin_enabled": false,
          //    "post_only": false,
          //    "limit_only": false,
          //    "cancel_only": false,
          //    "status": "online",
          //    "status_message": "",
          //    "trading_disabled": false,
          //    "fx_stablecoin": false,
          //    "max_slippage_percentage": "0.03000000",
          //    "auction_mode": false
          //  },
          
          var results = new HashSet<string>();
          foreach (JObject o in jArray) {
            if (o["quote_currency"]?.Value<string>() == "USD") {
              results.Add(o["base_currency"].Value<string>());
            }
          }
          AddToCoinTypesSupportedByWhichExchanges(CryptoExchange.CoinbasePro, results);
          AddToCoinTypesSupportedByWhichExchanges(CryptoExchange.Coinbase, results);
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
      
      // get kucoin supported coin types
      try
      {
        var url = $"https://api.kucoin.com/api/v1/symbols";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("User-Agent", HttpClientSingleton.UserAgent);
        await _httpClientSingleton.UseAsync("fetching kucoin trading pairs (for historical price service)", stoppingToken, async http => 
        {
          var response = await http.SendAsync(request, stoppingToken);
          string responseBody = await response.Content.ReadAsStringAsync(stoppingToken);
          if (!response.IsSuccessStatusCode)
          {
            throw new HttpRequestException($"kucoin api for all known trading pairs returned {response.StatusCode}: {responseBody}");
          }
          
          _logger.LogTrace("HistoricalCoinPriceService processed request for kucoin trading pairs, producing this data: {0}", responseBody);
            
          // FUTURE: The quote_increment field specifies the min order price as well as the price increment.
          // which we should be using when displaying the price-per-coin, or something maybe?

          var root = JObject.Parse(responseBody);
          var code = root.SelectToken("code")?.Value<string>();
          if (code != "200000")
          {
            throw new HttpRequestException($"kucoin candles api returned non-success code=\"{code}\" with response body={responseBody}");
          }
          var data = (JArray)root["data"];

          // typical response:
          //    {
          //      "symbol": "MEM-USDT",
          //      "name": "MEM-USDT",
          //      "baseCurrency": "MEM",
          //      "quoteCurrency": "USDT",
          //      "feeCurrency": "USDT",
          //      "market": "USDS",
          //      "baseMinSize": "0.1",
          //      "quoteMinSize": "0.1",
          //      "baseMaxSize": "10000000000",
          //      "quoteMaxSize": "99999999",
          //      "baseIncrement": "0.0001",
          //      "quoteIncrement": "0.00001",
          //      "priceIncrement": "0.00001",
          //      "priceLimitRate": "0.1",
          //      "minFunds": "0.1",
          //      "isMarginEnabled": false,
          //      "enableTrading": true
          //    },
          
          var results = new HashSet<string>();
          foreach (JObject o in data) {
            if (o["quoteCurrency"]?.Value<string>() == "USDT") {
              results.Add(o["baseCurrency"].Value<string>());
            }
          }
          AddToCoinTypesSupportedByWhichExchanges(CryptoExchange.Kucoin, results);
        });
      }
      catch (OperationCanceledException)
      {
        // this is our fault for shutting down. Don't bother logging it.
        throw;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, $"{ex.GetType().Name} while fetching kucoin trading pairs: {ex.Message}");
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

          foreach (var (coinType, time) in neededPrices)
          {
            try
            {
              if (_historicalPrices.ContainsKey((coinType, time)))
              {
                continue;
              }
            
              decimal? newPrice = await FetchHistoricalPriceFromExchanges(coinType, time, stoppingToken);
              if (newPrice == null)
              {
                newPrice = await FetchInferredHistoricalPrice(coinType, time, stoppingToken);
              }
            }
            catch (OperationCanceledException)
            {
              // this is our fault for shutting down. Don't bother logging it.
              throw;
            }
            catch (Exception ex)
            {
              _logger.LogError(ex, $"Ignoring unhandled exception in HistoricalCoinPriceService neededPrices worker loop");
            }
            finally
            {
              ImmutableInterlocked.Update(ref _neededPrices, p => p.Remove((coinType, time)));
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
    
    private readonly HashSet<CryptoExchange> _emptyExchangeHashSet = new HashSet<CryptoExchange>();

    private async Task<Decimal?> FetchHistoricalPriceFromExchanges(string coinType, DateTime time, CancellationToken stoppingToken)
    {
      // don't bother asking the exchanges multiple times for something they rejected
      if (_exchangeFailedPrices.Contains((coinType, time)))
      {
        return null;
      }
      
      CryptoExchange exchange = default;
      decimal? newPrice = null;
      var supportedExchanges = _coinTypesSupportedByWhichExchanges.GetValueOrDefault(coinType) ?? _emptyExchangeHashSet;

      async Task FetchFromKucoin()
      {
        if (newPrice == null && supportedExchanges.Contains(CryptoExchange.Kucoin))
        {
          exchange = CryptoExchange.Kucoin;
          newPrice = await FetchKucoinHistoricalPrice(coinType, time, stoppingToken);
        }
      }

      async Task FetchFromCoinbasePro()
      {
        if (supportedExchanges.Contains(CryptoExchange.CoinbasePro) || supportedExchanges.Contains(CryptoExchange.Coinbase))
        {
          exchange = CryptoExchange.CoinbasePro;
          newPrice = await FetchCoinbaseHistoricalPrice(coinType, time, stoppingToken);
        }
      }

      try
      {
        try
        {
          await FetchFromCoinbasePro();
        }
        catch
        {
          await FetchFromKucoin();
          if (newPrice == null) throw;
        }
        
        if (newPrice == null)
        {
          await FetchFromKucoin();
        }
      }
      catch
      {
        _logger.LogInformation($"Failed to fetch price of {coinType} due to exception; will not try again until the application has restarted.");
        _exchangeFailedPrices = _exchangeFailedPrices.Add((coinType, time));
        throw;
      }
      
      ReplaceHistoricalPrices(newDictionary => newDictionary[(coinType, time)] = (exchange, newPrice));
      _logger.LogInformation($"determined pricePerCoinUsd = {(newPrice?.ToString() ?? "null")} for {coinType} at {time} from {exchange}");

      return newPrice;
    }
    
    private async Task<Decimal?> FetchCoinbaseHistoricalPrice(string coinType, DateTime time, CancellationToken stoppingToken)
    {
      Decimal? newPrice = null;

      var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.exchange.coinbase.com/products/{coinType}-USD/candles" +
        $"?granularity=60" +
        $"&start={time.Subtract(TimeSpan.FromMinutes(2)).ToString("o")}" +
        $"&end={time.Add(TimeSpan.FromMinutes(3)).ToString("o")}");
      request.Headers.Add("Accept", "application/json");
      request.Headers.Add("User-Agent", HttpClientSingleton.UserAgent);
      await _httpClientSingleton.UseAsync($"fetching coinbase historical price of {coinType} at {time}", stoppingToken, async http => 
      {
        var response = await http.SendAsync(request, stoppingToken);
        string responseBody = await response.Content.ReadAsStringAsync(stoppingToken);
        if (!response.IsSuccessStatusCode)
        {
          throw new HttpRequestException($"coinbase pro api for {coinType} candle at={time.ToString("o")} returned {response.StatusCode}: {responseBody}");
        }
        
        _logger.LogTrace("HistoricalCoinPriceService processed coinbase request for {0} at {1} from {2}, producing this data: {3}",
          coinType, time, CryptoExchange.Coinbase, responseBody);

        var jArray = JArray.Parse(responseBody);
        if (jArray.Count == 0)
        {
          // This means there are no trades on record for this time period
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
          if (jArray.Count == 0) throw new Exception($"No sale history found for {coinType} at {time} on {CryptoExchange.Coinbase}");
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
      });
      
      return newPrice;
    }
    
    private async Task<Decimal?> FetchKucoinHistoricalPrice(string coinType, DateTime time, CancellationToken stoppingToken)
    {
      await Task.Delay(2000, stoppingToken); // wait even more than _httpClientSingleton forces
      // to try to avoid 429 "too many requests" errors. Kucoin seems to be stingy with candles requests.
      // this might go away if I plugged in an API key to the request?

      Decimal? newPrice = null;

      var url = $"https://api.kucoin.com/api/v1/market/candles";
      var request = new HttpRequestMessage(HttpMethod.Get, url +
        // Type of candlestick patterns: 1min, 3min, 5min, 15min, 30min, 1hour, 2hour, 4hour, 6hour, 8hour, 12hour, 1day, 1week
        $"?type=1min" +
        // long	[Optional] Start time (second), default is 0
        $"&startAt={time.Subtract(TimeSpan.FromMinutes(2)).ToUnixEpochSeconds()}" +
        // long [Optional] End time (second), default is 0
        $"&endAt={time.Add(TimeSpan.FromMinutes(3)).ToUnixEpochSeconds()}" +
        $"&symbol={coinType}-USDT");
      request.Headers.Add("Accept", "application/json");
      request.Headers.Add("User-Agent", HttpClientSingleton.UserAgent);
      await _httpClientSingleton.UseAsync($"fetching kucoin historical price of {coinType} at {time}", stoppingToken, async http => 
      {
        var response = await http.SendAsync(request, stoppingToken);
        string responseBody = await response.Content.ReadAsStringAsync(stoppingToken);
        if (!response.IsSuccessStatusCode)
        {
          throw new HttpRequestException($"kucoin api for {coinType} candle at={time.ToString("o")} returned {response.StatusCode}: {responseBody}");
        }
        
        _logger.LogTrace("HistoricalCoinPriceService processed kucoin request for {0} at {1} from {2}, producing this data: {3}",
          coinType, time, CryptoExchange.Kucoin, responseBody);
          
        var root = JObject.Parse(responseBody);
        var code = root.SelectToken("code")?.Value<string>();
        if (code != "200000")
        {
          throw new HttpRequestException($"kucoin candles api returned non-success code=\"{code}\" with response body={responseBody}");
        }
        var data = (JArray)root["data"];

        if (data.Count == 0)
        {
          // This means there are no trades on record for this time period
          newPrice = null;
        }
        else
        {
          /*
          Apparently it just returns an array of arrays of numbers.
          [
              "1545904980",             //Start time of the candle cycle (seconds since unix epoch)
              "0.058",                  //opening price
              "0.049",                  //closing price
              "0.058",                  //highest price
              "0.049",                  //lowest price
              "0.018",                  //Transaction volume
              "0.000945"                //Transaction amount
          ],
          */
          Decimal bestPrice = 0m;
          TimeSpan closestDifference = DateTime.MaxValue.Subtract(DateTime.MinValue);
          if (data.Count == 0) throw new Exception($"No sale history found for {coinType} at {time} on {CryptoExchange.Kucoin}");
          foreach (JArray candle in data)
          {
            var unixEpochSeconds = candle[0].Value<long>();
            var candleTime = SomeUtils.UnixEpochSecondsToDateTime(unixEpochSeconds);
            var difference = candleTime.Subtract(time);
            if (difference < closestDifference)
            {
              var low = candle[4].Value<Decimal>();
              var high = candle[3].Value<Decimal>();
              bestPrice = (low + high) / 2;
              closestDifference = difference;
            }
          }
          newPrice = bestPrice;
        }
      });

      return newPrice;
    }

    // This method is for when Coinbase and Kucoin refuse to acknowledge the price of LUNA before it crashed.
    // We can extrapolate its value from the BTC that I traded for it back then
    private async Task<Decimal?> FetchInferredHistoricalPrice(string coinType, DateTime time, CancellationToken stoppingToken)
    {
      // NOTE: this has already been done for every time that enters this service; see ToUsd()
      // time = time.ChopSecondsAndSmaller();
      var dataService = _serviceProvider.GetRequiredService<IDataService>();
      foreach (var x in dataService.Transactions.Values
        .Where(x => x.TransactionType == TransactionType.Trade &&
               x.Time.ChopSecondsAndSmaller() == time && // FUTURE: could probably relax this to "same day" if ever needed
               (x.ReceivedCoinType == coinType || x.PaymentCoinType == coinType) &&
               (x.ReceivedCoinType != x.PaymentCoinType) && // whut? oh this is just paranoia
               (x.ReceivedCoinsPerPaymentCoinListPrice != 0) && // same here; paranoia, but this happens for futures PNL transactions and unpopulated data
               (x.PaymentCoinsPerReceivedCoinListPrice != 0)))
      {
        // ListPrice is "how many of this coin for 1 of the other kind of coin"
        Decimal listPrice;
        string otherCoinType;
        if (coinType == x.PaymentCoinType)
        {
          listPrice = x.ReceivedCoinsPerPaymentCoinListPrice;
          otherCoinType = x.ReceivedCoinType;
        }
        else // if (coinType == x.ReceivedCoinType)
        {
          listPrice = x.PaymentCoinsPerReceivedCoinListPrice;
          otherCoinType = x.PaymentCoinType;
        }

        Decimal? otherCoinPriceUsd = null;
        if (_historicalPrices.TryGetValue((otherCoinType, time), out var historicalPrice))
        {
          otherCoinPriceUsd = historicalPrice.PriceUsd;
        }
        
        if (otherCoinPriceUsd == null)
        {
          otherCoinPriceUsd = await FetchHistoricalPriceFromExchanges(otherCoinType, time, stoppingToken);
        }

        if (otherCoinPriceUsd != null)
        {
          // (otherCoinPriceUsd)      (listPrice)       (answer)
          //        N usd             W otherCoins       W*N USD
          // ------------------- * ----------------- = ----------
          //    1 otherCoin             1 coin           1 coin
          Decimal? newPrice = otherCoinPriceUsd * listPrice;
          ReplaceHistoricalPrices(newDictionary => newDictionary[(coinType, time)] = (CryptoExchange.None, newPrice));
          _logger.LogInformation($"determined pricePerCoinUsd = {(newPrice?.ToString() ?? "null")} for {coinType} at {time} by inference from list price of related {otherCoinType} transaction");
          return newPrice;
        }
      }
      
      return null;
    }

    private void AddToCoinTypesSupportedByWhichExchanges(CryptoExchange exchange, HashSet<string> coinTypes)
    {
      var newDictionary = new Dictionary<string, HashSet<CryptoExchange>>(_coinTypesSupportedByWhichExchanges);
      foreach (var coinType in coinTypes)
      {
        newDictionary.AddToBucket(coinType, exchange);
      }
      _coinTypesSupportedByWhichExchanges = newDictionary;
    }
    
    public IEnumerable<PersistedHistoricalCoinPrice> ClonePersistedData()
    {
      return _historicalPrices
        .Select(x => new PersistedHistoricalCoinPrice
        {
          CoinType = x.Key.CoinType,
          Time = x.Key.Time,
          PricePerCoinUsd = x.Value.PriceUsd,
          Exchange = x.Value.Exchange,
        });
    }
    
    public void ImportPersistedData(IEnumerable<PersistedHistoricalCoinPrice> persistedData)
    {
      var newDictionary = new Dictionary<(string CoinType, DateTime Time), (CryptoExchange Exchange, Decimal? PriceUsd)>(_historicalPrices);
      foreach (var p in persistedData)
      {
        newDictionary[(p.CoinType, p.Time)] = (p.Exchange, p.PricePerCoinUsd);
      }
      _historicalPrices = newDictionary;
    }
  }
}