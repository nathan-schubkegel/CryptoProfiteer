using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

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

    public IEnumerable<Transaction> OrderedTransactions =>  _p.Data.Transactions
      .OrderByDescending(x => x.Time).ThenBy(x => x.TradeId);

    public void OnGet()
    {
    }
  }
}
