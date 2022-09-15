using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace CryptoProfiteer.Pages
{
  public class AltCoinAlertsModel: PageModel
  {
    private readonly ILogger<AltCoinAlertsModel> _logger;
    private readonly IDataService _data;
    private readonly IAltCoinAlertService _alerts;
    private readonly IHistoricalCoinPriceService _historicalPrices;
    private readonly IPriceService _currentPrices;
    private readonly IFriendlyNameService _friendlyNames;

    public AltCoinAlertsModel(ILogger<AltCoinAlertsModel> logger,
      IDataService data,
      IAltCoinAlertService alerts,
      IHistoricalCoinPriceService historicalPrices,
      IPriceService currentPrices,
      IFriendlyNameService friendlyNames)
    {
      _logger = logger;
      _data = data;
      _alerts = alerts;
      _historicalPrices = historicalPrices;
      _currentPrices = currentPrices;
      _friendlyNames = friendlyNames;
    }
    
    public IEnumerable<AltCoinAlert> Alerts(string sortBy = null)
    {
      var values = _alerts.Alerts;
      switch (sortBy)
      {
        default:
        case "date": return values.OrderByDescending(x => x.Date);
        case "dateAscending": return values.OrderBy(x => x.Date);
      }
    }
    
    public string GetFriendlyName(string coinType) => _friendlyNames.GetOrCreateFriendlyName(coinType).Value;
    
    public Decimal? GetHistoricalPrice(string coinType, DateTime date) {
      var basecoins = _friendlyNames.GetExchangeCurrencies(CryptoExchange.CoinbasePro);
      var kucoins = _friendlyNames.GetExchangeCurrencies(CryptoExchange.Kucoin);
      if (basecoins.Contains(coinType))
      {
        return _historicalPrices.ToUsd(1m, coinType, date, CryptoExchange.CoinbasePro);
      }
      else if (kucoins.Contains(coinType))
      {
        return _historicalPrices.ToUsd(1m, coinType, date, CryptoExchange.Kucoin);
      }
      else
      {
        return null;
      }
    }
    
    public Decimal? GetCurrentPrice(string coinType) {
      return _currentPrices.TryGetCoinPrice(coinType)?.PerCoinCostUsd;
    }

    public void OnGet()
    {
    }
  }
}
