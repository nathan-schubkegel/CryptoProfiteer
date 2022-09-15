using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace CryptoProfiteer.Pages
{
  public class CandlesModel : PageModel
  {
    private readonly ILogger<CandlesModel> _logger;
    private readonly ICandleService _candleService;

    public CandlesModel(ILogger<CandlesModel> logger, ICandleService candleService)
    {
      _logger = logger;
      _candleService = candleService;
    }
    
    public ICandleService CandleService => _candleService;

    public void OnGet()
    {
    }
  }
}
