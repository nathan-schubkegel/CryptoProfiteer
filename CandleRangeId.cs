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
    
    public static PersistedCandleRangeId CoinbasePro(string coinType, DateTime startTime, CandleGranularity granularity)
    {
      return new PersistedCandleRangeId
      {
        CoinType = coinType,
        Exchange = CryptoExchange.CoinbasePro,
        StartTime = startTime,
        Count = MaxCoinbaseCount - 1,
        Granularity = granularity
      };
    }
  }
}