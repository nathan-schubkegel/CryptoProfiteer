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
using System.Globalization;

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
          // figure out which exchanges know about which cryptos
          var exchanges = new Dictionary<string, List<CryptoExchange>>();
          foreach (var transaction in _dataService.Transactions.Values)
          {
            if (!exchanges.TryGetValue(transaction.CoinType, out var bucket))
            {
              bucket = new List<CryptoExchange>();
              exchanges[transaction.CoinType] = bucket;
            }
            if (!bucket.Contains(transaction.Exchange))
            {
              bucket.Add(transaction.Exchange);
            }
          }

          // Ask Kucoin for all prices it knows about, and save all that we know about
          Dictionary<string, Decimal> kucoinPrices;
          {
            var url = $"https://api.kucoin.com/api/v1/prices?base=USD";
            var response = await http.GetAsync(url);
            string responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
              throw new HttpRequestException($"kucoin prices api returned {response.StatusCode}: {responseBody}");
            }
            var json = JObject.Parse(responseBody);
            var systemCode = json.SelectToken("code")?.Value<string>();
            if (systemCode != "200000")
            {
              throw new HttpRequestException($"kucoin prices api returned system code {systemCode}: {responseBody}");
            }
            kucoinPrices = ((JObject)json.SelectToken("data"))
              .ToObject<Dictionary<string, string>>()
              .ToDictionary(p => p.Key, p => Decimal.Parse(p.Value, NumberStyles.Float, CultureInfo.InvariantCulture));

            var oldSummaries = _dataService.CoinSummaries;
            var newPrices = kucoinPrices.Where(p => oldSummaries.ContainsKey(p.Key))
              .Select(p => new CoinbaseCoinPrice { CoinType = p.Key, PerCoinCost = p.Value })
              .ToArray();
            _dataService.UpdateCoinPrices(newPrices);
          }
          
          // all other coins... ask their individual exchanges
          foreach (var coinSummary in _dataService.CoinSummaries.Values)
          {
            if (kucoinPrices.ContainsKey(coinSummary.CoinType))
            {
              continue;
            }

            var coinExchanges = exchanges[coinSummary.CoinType];
            if (coinExchanges.Contains(CryptoExchange.Coinbase))
            {
              var url = $"https://api.coinbase.com/v2/prices/{coinSummary.CoinType}-USD/spot";
              var response = await http.GetAsync(url);
              string responseBody = await response.Content.ReadAsStringAsync();
              if (!response.IsSuccessStatusCode)
              {
                throw new HttpRequestException($"coinbase price api for {coinSummary.CoinType} returned {response.StatusCode}: {responseBody}");
              }
              var json = JObject.Parse(responseBody);
              var perCoinCost = Decimal.Parse(json.SelectToken("data.amount").Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture);
              var newPrice = new CoinbaseCoinPrice { CoinType = coinSummary.CoinType, PerCoinCost = perCoinCost };
              _dataService.UpdateCoinPrices(new[]{newPrice});
              await Task.Delay(1000, stoppingToken);
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
          _logger.LogError(ex, $"{ex.GetType().Name} while fetching coin prices: {ex.Message}");
        }
        await Task.Delay(30000, stoppingToken);
      }
    }
  }
}