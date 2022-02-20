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
  public struct PersistedCandleRangeId
  {
    public string CoinType { get; set; }
    public CryptoExchange Exchange { get; set; }
    public DateTime StartTime { get; set; }
    public int Count { get; set; }
    public CandleGranularity Granularity { get; set; }
    public TimeSpan TimeLength => TimeSpan.FromSeconds((int)Granularity * Count);
    public DateTime EndTime => StartTime + TimeLength;
    
    public const int MaxCoinbaseCount = 300;
    public const int MaxKucoinCount = 1500;

    public string ToFileName() => $"{CoinType} {Exchange} {StartTime.ToString("o").Replace(":", "_")} {Count} {Granularity}.candle";

    public static PersistedCandleRangeId? FromFileName(string fileName)
    {
      if (!fileName.EndsWith(".candle")) return null;
      fileName = fileName.Substring(0, fileName.Length - ".txt".Length);
      var parts = fileName.Split(' ');
      try
      {
        return new PersistedCandleRangeId
        {
          CoinType = parts[0],
          Exchange = Enum.Parse<CryptoExchange>(parts[1]),
          StartTime = DateTime.Parse(parts[2].Replace("_", ":"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
          Count = int.Parse(parts[3]),
          Granularity = Enum.Parse<CandleGranularity>(parts[4])
        };
      }
      catch
      {
        return null;
      }
    }
    
    public static PersistedCandleRangeId Coinbase(string coinType, DateTime startTime, CandleGranularity granularity)
    {
      return new PersistedCandleRangeId
      {
        CoinType = coinType,
        Exchange = CryptoExchange.Coinbase,
        StartTime = startTime,
        Count = MaxCoinbaseCount - 1,
        Granularity = granularity
      };
    }
  }
  
  public class PersistedCandleRange
  {
    public PersistedCandleRangeId Id { get; set; }
    
    // NOTE: this list contains null arrays where the exchange has gaps in trade data
    // NOTE: this property is persisted to JSON on disk
    public List<Decimal[]> Candles { get; set; }

    public int Count => Candles.Count;

    public Candle? TryGetCandle(int i)
    {
      if (i < 0 || i >= Candles.Count) return null;
      var data = Candles[i];
      if (data == null) return null;
      return new Candle(data);
    }
  }
  
  public struct Candle
  {
    public Decimal Open;
    public Decimal Close;
    public Decimal High;
    public Decimal Low;
    
    public Candle(Decimal[] persistedData)
    {
      Open = persistedData[OpenIndex];
      Close = persistedData[CloseIndex];
      High = persistedData[HighIndex];
      Low = persistedData[LowIndex];
    }

    public Decimal[] ToPersistedData() => new [] { Open, Close, High, Low };
    
    public const int OpenIndex = 0;
    public const int CloseIndex = 1;
    public const int HighIndex = 2;
    public const int LowIndex = 3;
    
    public bool IsBearish => Close < Open;
    public bool IsBullish => Close > Open;
  }
  
  public enum CandleGranularity
  {
    Minutes = 60, 
    Minutes5 = 300, 
    Minutes15 = 900, 
    Hours = 3600, 
    Hours6 = 21600, 
    Days = 86400
  }
  
  public interface ICandleService
  {
    Task<PersistedCandleRange> TryGetCandleRangeAsync(PersistedCandleRangeId id, CancellationToken stoppingToken);
  }

  public class CandleService : ICandleService
  {
    private readonly IHttpClientSingleton _httpClientSingleton;
    private readonly ILogger<CandleService> _logger;
    private readonly object _fileLock = new object();
    

    private ICollection<string> _supportedCoinTypes = new HashSet<string>();

    public CandleService(ILogger<CandleService> logger, IHttpClientSingleton httpClientSingleton)
    {
      _logger = logger;
      _httpClientSingleton = httpClientSingleton;
    }

    public Task<PersistedCandleRange> TryGetCandleRangeAsync(PersistedCandleRangeId id, CancellationToken stoppingToken) => Task.Run(async () =>
    {
      try
      {
        // normalize requested times
        id.StartTime = id.StartTime.ChopSecondsAndSmaller();
        
        bool allDataIsPast = id.EndTime < DateTime.UtcNow;
        
        var dirPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "candles");
        var filePath = Path.Combine(dirPath, id.ToFileName());
        List<Decimal[]> data = null;
        lock(_fileLock)
        {
          if (Directory.Exists(dirPath) && File.Exists(filePath))
          {
            var jsonBlob = File.ReadAllText(filePath);
            data = JsonConvert.DeserializeObject<List<Decimal[]>>(jsonBlob);
          }
        }
        
        if (data == null)
        {
          if (id.Exchange != CryptoExchange.Coinbase) throw new Exception("no can do, hoss; coinbase only for now.");

          string responseBody = null;
          var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.exchange.coinbase.com/products/{id.CoinType}-USD/candles" +
            $"?granularity={(int)id.Granularity}" +
            $"&start={id.StartTime.ToString("o")}" +
            $"&end={id.EndTime.ToString("o")}");
          request.Headers.Add("Accept", "application/json");
          request.Headers.Add("User-Agent", HttpClientSingleton.UserAgent);
          await _httpClientSingleton.UseAsync($"fetching {id.Granularity} candles for {id.CoinType} at {id.StartTime}", stoppingToken, async http => 
          {
            var response = await http.SendAsync(request);
            responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
              throw new HttpRequestException($"coinbase pro api for {id.Granularity} candles for {id.CoinType} at={id.StartTime} returned {response.StatusCode}: {responseBody}");
            }
          });
          
          data = new List<Decimal[]>();
          var jArray = JArray.Parse(responseBody);
          if (jArray.Count == 0)
          {
            // This means there were no trades on record for this time period
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
            foreach (JArray candleArray in jArray)
            {
              var unixEpochSeconds = candleArray[0].Value<long>();
              var candleTime = SomeUtils.UnixEpochSecondsToDateTime(unixEpochSeconds);
              var candle = new Candle
              {
                Low = candleArray[1].Value<Decimal>(),
                High = candleArray[2].Value<Decimal>(),
                Open = candleArray[3].Value<Decimal>(),
                Close = candleArray[4].Value<Decimal>()
              };
              var dataIndex = (int)Math.Round((candleTime - id.StartTime).TotalSeconds, MidpointRounding.AwayFromZero) / (int)id.Granularity;
              if (dataIndex > id.Count)
              {
                throw new Exception($"Received candle data for {candleTime} but that's outside the requested range {id.StartTime} to {id.EndTime}");
              }
                
              // put nulls where coinbase has no data
              while (dataIndex >= data.Count) {
                data.Add(null);
              }
              data[dataIndex] = candle.ToPersistedData();
            }
          }
          
          // put nulls where coinbase has no data
          while (data.Count < id.Count) {
            data.Add(null);
          }

          // avoid polluting our cache: only save data to disk if none of it was "in the future" when we asked for it
          if (allDataIsPast)
          {
            var jsonBlob = JsonConvert.SerializeObject(data);
            lock(_fileLock)
            {
              Directory.CreateDirectory(dirPath);
              File.WriteAllText(filePath, jsonBlob);
            }
          }
        }
        
        return new PersistedCandleRange
        {
          Id = id,
          Candles = data,
        };
      }
      catch (OperationCanceledException)
      {
        // this is our fault for shutting down. Don't bother logging it.
        return null;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, $"{ex.GetType().Name} while fetching candles: {ex.Message}");
        return null;
      }
    });
  }
}