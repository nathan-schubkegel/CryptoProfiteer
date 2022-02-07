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
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace CryptoProfiteer
{
  public interface IPriceService
  {
    CoinPrice TryGetCoinPrice(string coinType);
    event Action CoinPricesUpdated;
  }
  
  public class PriceService : BackgroundService, IPriceService
  {
    private readonly IHttpClientSingleton _httpClientSingleton;
    private readonly ILogger<PriceService> _logger;
    private readonly IFriendlyNameService _friendlyNameService;
    
    // _prices is immutable, and must be completely replaced
    private Dictionary<string, CoinPrice> _prices = new Dictionary<string, CoinPrice>();
    
    // _neededPrices is mutable, and must be locked before being read/modified
    private readonly HashSet<string> _neededPrices = new HashSet<string>();
    
    public event Action CoinPricesUpdated;

    public PriceService(ILogger<PriceService> logger, IFriendlyNameService friendlyNameService, IHttpClientSingleton httpClientSingleton)
    {
      _logger = logger;
      _friendlyNameService = friendlyNameService;
      _httpClientSingleton = httpClientSingleton;
    }
    
    public CoinPrice TryGetCoinPrice(string coinType)
    {
      if (_prices.TryGetValue(coinType, out var result))
      {
        return result;
      }
      else
      {
        lock (_neededPrices)
        {
          _neededPrices.Add(coinType);
          return null;
        }
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
          // Ask Kucoin for all prices it knows about, and save them all
          var kucoins = new HashSet<string>();
          {
            var url = $"https://api.kucoin.com/api/v1/prices?base=USD";
            await _httpClientSingleton.UseAsync("fetching current prices of all cryptos on kucoin", stoppingToken, async http =>
            {
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

              var now = DateTime.UtcNow;
              var newPrices = new Dictionary<string, CoinPrice>(_prices);
              foreach ((string coinType, JToken priceToken) in (JObject)json.SelectToken("data"))
              {
                var price = Decimal.Parse(priceToken.Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture);
                var newPrice = new CoinPrice(coinType, price, _friendlyNameService.GetOrCreateFriendlyName(coinType), now);
                newPrices[coinType] = newPrice;
                kucoins.Add(coinType);
              }
              _prices = newPrices;
            });
            
            NotifyCoinPricesUpdated();
          }

          {
            // Determine which coin prices need + can be fetched from Coinbase
            var coinBaseCurrencies = _friendlyNameService.GetExchangeCurrencies(CryptoExchange.Coinbase).ToHashSet();
            List<string> coinTypesToRequest;
            lock (_neededPrices)
            {
              coinTypesToRequest = _neededPrices.Where(x => !kucoins.Contains(x) && coinBaseCurrencies.Contains(x)).ToList();
            }
          
            // Ask CoinBase for those prices
            foreach (var coinType in coinTypesToRequest)
            {
              await _httpClientSingleton.UseAsync($"fetching current price of {coinType} from coinbase", stoppingToken, async http =>
              {
                var url = $"https://api.coinbase.com/v2/prices/{coinType}-USD/spot";
                var response = await http.GetAsync(url);
                string responseBody = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                  throw new HttpRequestException($"coinbase price api for {coinType} returned {response.StatusCode}: {responseBody}");
                }
                var json = JObject.Parse(responseBody);
                var price = Decimal.Parse(json.SelectToken("data.amount").Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture);
                var now = DateTime.UtcNow;
                var newPrice = new CoinPrice(coinType, price, _friendlyNameService.GetOrCreateFriendlyName(coinType), now);
                var newPrices = new Dictionary<string, CoinPrice>(_prices);
                newPrices[coinType] = newPrice;
                _prices = newPrices;
              });
              
              NotifyCoinPricesUpdated();
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
        
        // TODO: be able to interrupt this when someone requests a price that coinbase has that we don't have yet
        await Task.Delay(30000, stoppingToken);
      }
    }
    
    private void NotifyCoinPricesUpdated()
    {
      try
      {
        CoinPricesUpdated?.Invoke();
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, $"{ex.GetType().Name} while responding to new coin prices: {ex.Message}");
      }
    }
  }
}