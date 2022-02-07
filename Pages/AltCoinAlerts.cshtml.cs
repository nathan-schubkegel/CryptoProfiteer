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
    private readonly IFriendlyNameService _friendlyNames;

    public AltCoinAlertsModel(ILogger<AltCoinAlertsModel> logger,
      IDataService data,
      IAltCoinAlertService alerts,
      IHistoricalCoinPriceService historicalPrices,
      IFriendlyNameService friendlyNames)
    {
      _logger = logger;
      _data = data;
      _alerts = alerts;
      _historicalPrices = historicalPrices;
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
    
    public Decimal? GetPrice(string coinType, DateTime date) {
      var basecoins = _friendlyNames.GetExchangeCurrencies(CryptoExchange.Coinbase);
      var kucoins = _friendlyNames.GetExchangeCurrencies(CryptoExchange.Kucoin);
      if (basecoins.Contains(coinType))
      {
        return _historicalPrices.ToUsd(1m, coinType, date, CryptoExchange.Coinbase);
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

    public void OnGet()
    {
    }
  }
}
