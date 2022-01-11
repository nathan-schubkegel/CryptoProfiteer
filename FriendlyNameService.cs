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
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CryptoProfiteer
{
  public class FriendlyNameService : BackgroundService
  {
    private readonly ILogger<FriendlyNameService> _logger;
    private readonly IDataService _dataService;

    public FriendlyNameService(ILogger<FriendlyNameService> logger, IDataService dataService)
    {
      _logger = logger;
      _dataService = dataService;
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
          _dataService.UpdateFriendlyNames(coinFriendlyNames);
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
          _dataService.UpdateFriendlyNames(coinFriendlyNames);
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