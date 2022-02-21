using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace CryptoProfiteer.Pages
{
  public class BotProofModel : PageModel
  {
    private readonly ILogger<BotProofModel> _logger;
    private readonly ICandleService _candleService;
    private readonly IHistoricalCoinPriceService _historicalCoinPriceService;
    private readonly IFriendlyNameService _friendlyNameService;

    public BotProofModel(ILogger<BotProofModel> logger,
      ICandleService candleService,
      IHistoricalCoinPriceService historicalCoinPriceService,
      IFriendlyNameService friendlyNameService)
    {
      _logger = logger;
      _candleService = candleService;
      _historicalCoinPriceService = historicalCoinPriceService;
      _friendlyNameService = friendlyNameService;
    }
    
    public ICandleService CandleService => _candleService;
    
    public IEnumerable<(string MachineName, string FriendlyName)> CoinTypes => 
      _historicalCoinPriceService.CoinbaseCoinTypes.Select(x => (x, _friendlyNameService.GetOrCreateFriendlyName(x).Value));

    public void OnGet()
    {
    }
  }
}
