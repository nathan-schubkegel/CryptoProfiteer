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
  public class PriceService : BackgroundService
  {
    // polygon.io
    private readonly ILogger<PriceService> _logger;
    private readonly IDataService _dataService;

    public PriceService(ILogger<PriceService> logger, IDataService dataService)
    {
      _logger = logger;
      _dataService = dataService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      await Task.Yield();

      var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "polygonApiKey.txt");
      var lines = File.ReadAllLines(path);
      var key = lines[0];

      using var http = new HttpClient();
      http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(key);

      while (!stoppingToken.IsCancellationRequested)
      {
        try
        {
          var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
          var response = await http.GetAsync($"https://api.polygon.io/v2/aggs/grouped/locale/global/market/crypto/{today}?adjusted=false");
          string responseBody = await response.Content.ReadAsStringAsync();
          if (!response.IsSuccessStatusCode)
          {
            throw new HttpRequestException($"polygon server returned {response.StatusCode}: {responseBody}");
          }
          var result = JArray.Parse(responseBody);
          var now = DateTime.Now;
          var newPrices = result.OfType<JObject>().Select(o =>
          {
            // name typically looks like "X:BTCUSD"
            var id = o["T"].Value<string>();
            var match = Regex.Match(id, "^X:(.*)USD$");
            if (!match.Success) return null;
            var name = match.Groups[1].Value;
            var price = o["c"].Value<Decimal>();
            return new CoinPrice(name, price, now);
          }).Where(x => x != null).ToList();
          _dataService.UpdateCoinPrices(newPrices);
        }
        catch (Exception ex)
        {
          Console.WriteLine($"{ex.GetType().Name} while fetching crypto prices: {ex.Message}");
        }
        await Task.Delay(30000, stoppingToken);
      }
    }
  }
}