using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace CryptoProfiteer.Pages
{
  public class OrdersModel : PageModel
  {
    private readonly ILogger<OrdersModel> _logger;
    private readonly IDataService _data;

    public OrdersModel(ILogger<OrdersModel> logger, IDataService data)
    {
      _logger = logger;
      _data = data;
    }

    public IEnumerable<Order> Orders(string sortBy = null)
    {
      var values = _data.Orders.Values;
      switch (sortBy)
      {
        default:
        case "coinTypeDescending": return values.OrderByDescending(x => x.CoinType).ThenByDescending(x => x.Time);
        case "coinType": return values.OrderBy(x => x.CoinType).ThenByDescending(x => x.Time);
        case "date": return values.OrderByDescending(x => x.Time).ThenBy(x => x.CoinType);
        case "dateAscending": return values.OrderBy(x => x.Time).ThenBy(x => x.CoinType);
      }
    }

    public void OnGet()
    {
    }
  }
}
