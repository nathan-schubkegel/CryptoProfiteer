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
    private readonly Services _services;

    public TransactionsModel(ILogger<TransactionsModel> logger, IDataService data, Services services)
    {
      _logger = logger;
      _data = data;
      _services = services;
    }

    public IEnumerable<Transaction> OrderedTransactions =>  _data.Transactions.Values
      .OrderByDescending(x => x.Time).ThenBy(x => x.Id);
      
    public IEnumerable<(string MachineName, string FriendlyName)> GetCoinTypes() =>
      _data.Transactions.Values
      .SelectMany(t => new[] { t.PaymentCoinType, t.ReceivedCoinType })
      .Select(x => (MachineName: x, FriendlyName: _services.FriendlyNameService.GetOrCreateFriendlyName(x).Value))
      .Distinct()
      .OrderBy(x => x.FriendlyName);

    public void OnGet()
    {
    }
  }
}
