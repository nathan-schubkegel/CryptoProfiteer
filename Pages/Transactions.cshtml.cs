using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using CryptoProfiteer.Services;

namespace CryptoProfiteer.Pages
{
  public class TransactionsModel : PageModel
  {
    private readonly ILogger<TransactionsModel> _logger;
    private readonly IPersistenceService _p;

    public TransactionsModel(ILogger<TransactionsModel> logger, IPersistenceService p)
    {
      _logger = logger;
      _p = p;
    }

    public PersistenceData Data => _p.Data;

    public void OnGet()
    {
    }
  }
}
