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
    private readonly IDataService _data;

    public TransactionsModel(ILogger<TransactionsModel> logger, IDataService data)
    {
      _logger = logger;
      _data = data;
    }

    public IEnumerable<Transaction> OrderedTransactions =>  _data.Transactions.Values
      .OrderByDescending(x => x.Time).ThenBy(x => x.TradeId);
      
    public IEnumerable<(string MachineName, string FriendlyName)> GetCoinTypes() =>
      _data.Transactions.Values
      .Select(t => (t.CoinType, t.FriendlyName))
      .Distinct()
      .OrderBy(x => x.FriendlyName);

    public void OnGet()
    {
    }
  }
}
