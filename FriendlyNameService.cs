using Microsoft.Extensions.Hosting;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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

namespace CryptoProfiteer
{
  public interface IFriendlyNameService
  {
    FriendlyName GetOrCreateFriendlyName(string coinType);
    ImmutableArray<string> GetExchangeCurrencies(CryptoExchange exchange);
  }
  
  public class FriendlyNameService : BackgroundService, IFriendlyNameService
  {
    private readonly ILogger<FriendlyNameService> _logger;
    private readonly object _lock = new object();
    
    // this object can be mutated while _lock is held
    private readonly Dictionary<string, FriendlyName> _unresolvedFriendlyNames = new Dictionary<string, FriendlyName>();
    
    // these objects are immutable and must be completely replaced, not mutated
    private Dictionary<string, FriendlyName> _friendlyNames = new Dictionary<string, FriendlyName>();
    private Dictionary<CryptoExchange, ImmutableArray<string>> _exchangeCurrencies = new Dictionary<CryptoExchange, ImmutableArray<string>>();

    public FriendlyNameService(ILogger<FriendlyNameService> logger)
    {
      _logger = logger;
    }
    
    public ImmutableArray<string> GetExchangeCurrencies(CryptoExchange exchange)
    {
      return _exchangeCurrencies.TryGetValue(exchange, out var result) ? result : ImmutableArray<string>.Empty;
    }
    
    public FriendlyName GetOrCreateFriendlyName(string coinType)
    {
      if (_friendlyNames.TryGetValue(coinType, out var result)) return result;
      lock (_lock)
      {
        if (_friendlyNames.TryGetValue(coinType, out result)) return result;
        if (_unresolvedFriendlyNames.TryGetValue(coinType, out result)) return result;
        result = new FriendlyName { Value = coinType };
        _unresolvedFriendlyNames[coinType] = result;
        return result;
      }
    }
    
    private void UpdateFriendlyNames(Dictionary<string, string> newFriendlyNames, CryptoExchange exchange)
    {
      lock (_lock)
      {
        // update _friendlyNames
        var result = new Dictionary<string, FriendlyName>(_friendlyNames);
        foreach ((string coinType, string friendlyNameText) in newFriendlyNames)
        {
          if (!_unresolvedFriendlyNames.TryGetValue(coinType, out var friendlyName) &&
              !_friendlyNames.TryGetValue(coinType, out friendlyName))
          {
            friendlyName = new FriendlyName();
          }
          friendlyName.Value = friendlyNameText;
          result[coinType] = friendlyName;
          _unresolvedFriendlyNames.Remove(coinType);
        }
        _friendlyNames = result;

        // update _exchangeCurrencies
        var newCurrencies = new Dictionary<CryptoExchange, ImmutableArray<string>>(_exchangeCurrencies);
        newCurrencies[exchange] = newCurrencies.GetValueOrDefault(exchange, ImmutableArray<string>.Empty)
          .Concat(newFriendlyNames.Keys)
          .Distinct()
          .ToImmutableArray();
        _exchangeCurrencies = newCurrencies;
      }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      // Other services can't start until this service awaits, so await now!
      await Task.Yield();

      var coinFriendlyNames = new Dictionary<string, string>();
      await HttpClientSingleton.UseAsync(stoppingToken, async http =>
      {
        try
        {
          var url = $"https://api.pro.coinbase.com/currencies";
          var response = await http.GetAsync(url);
          string responseBody = await response.Content.ReadAsStringAsync();
          if (!response.IsSuccessStatusCode)
          {
            throw new HttpRequestException($"coinbase currencies api returned {response.StatusCode}: {responseBody}");
          }
          var data = JArray.Parse(responseBody);
          foreach (var currency in data)
          {
            var type = currency.SelectToken("details.type").Value<string>();
            if (type == "crypto")
            {
              var coinType = currency["id"].Value<string>();
              var friendlyName = currency["name"].Value<string>();
              coinFriendlyNames[coinType] = $"{friendlyName} ({coinType})";
            }
          }
          UpdateFriendlyNames(coinFriendlyNames, CryptoExchange.Coinbase);
        }
        catch (OperationCanceledException)
        {
          // this is our fault for shutting down. Don't bother logging it.
          throw;
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, $"{ex.GetType().Name} while fetching coin friendly names from coinbase: {ex.Message}");
        }
      });
      
      await HttpClientSingleton.UseAsync(stoppingToken, async http =>
      {
        try
        {
          var url = $"https://api.kucoin.com/api/v1/currencies";
          var response = await http.GetAsync(url);
          string responseBody = await response.Content.ReadAsStringAsync();
          if (!response.IsSuccessStatusCode)
          {
            throw new HttpRequestException($"kucoin currencies api returned {response.StatusCode}: {responseBody}");
          }
          var data = JObject.Parse(responseBody);
          var code = data.SelectToken("code")?.Value<string>();
          if (code != "200000")
          {
            throw new HttpRequestException($"kucoin currencies api returned non-success code=\"{code}\"");
          }
          var currencies = (JArray)data["data"];
          foreach (JObject currency in currencies)
          {
            var coinType = currency["currency"].Value<string>();
            var friendlyName = currency["fullName"].Value<string>();
            if (!coinFriendlyNames.ContainsKey(coinType))
            {
              coinFriendlyNames[coinType] = $"{friendlyName} ({coinType})";
            }
          }
          UpdateFriendlyNames(coinFriendlyNames, CryptoExchange.Kucoin);
        }
        catch (OperationCanceledException)
        {
          // this is our fault for shutting down. Don't bother logging it.
          throw;
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, $"{ex.GetType().Name} while fetching coin friendly names from kucoin: {ex.Message}");
        }
      });
    }
  }
}