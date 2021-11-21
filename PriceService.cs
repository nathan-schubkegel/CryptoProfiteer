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
    private readonly ILogger<PriceService> _logger;
    private readonly IDataService _dataService;

    public PriceService(ILogger<PriceService> logger, IDataService dataService)
    {
      _logger = logger;
      _dataService = dataService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      // Other services can't start until this service awaits, so await now!
      await Task.Yield();

      using var http = new HttpClient();
      while (!stoppingToken.IsCancellationRequested)
      {
        try
        {
          foreach (var coinSummary in _dataService.CoinSummaries.Values)
          {
            var url = $"https://api.coinbase.com/v2/prices/{coinSummary.CoinType}-USD/spot";
            var response = await http.GetAsync(url);
            string responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
              throw new HttpRequestException($"coinbase returned {response.StatusCode}: {responseBody}");
            }
            var json = JObject.Parse(responseBody);
            var perCoinCost = Decimal.Parse(json.SelectToken("data.amount").Value<string>(), System.Globalization.NumberStyles.Float);
            var newPrice = new CoinPrice(coinSummary.CoinType, perCoinCost, DateTime.Now);
            _dataService.UpdateCoinPrices(new[]{newPrice});
            await Task.Delay(1000, stoppingToken);
          }
        }
        catch (OperationCanceledException ex)
        {
          // this is our fault for shutting down. Don't bother logging it.
          throw;
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, $"{ex.GetType().Name} while fetching crypto prices: {ex.Message}");
        }
        await Task.Delay(30000, stoppingToken);
      }
    }
  }
}