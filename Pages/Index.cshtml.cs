using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace CryptoProfiteer.Pages
{
  public class IndexModel : PageModel
  {
    private readonly ILogger<IndexModel> _logger;
    private readonly IDataService _data;

    public IndexModel(ILogger<IndexModel> logger, IDataService data)
    {
      _logger = logger;
      _data = data;
    }

    public IEnumerable<CoinSummary> OrderedSummaries =>  _data.CoinSummaries.Values
      .OrderBy(x => x.CoinType);

    public void OnGet()
    {

    }
  }
}
