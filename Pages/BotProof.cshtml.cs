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

    public BotProofModel(ILogger<BotProofModel> logger, ICandleService candleService)
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
