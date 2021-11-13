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
    private readonly IPersistenceService _p;

    public OrdersModel(ILogger<OrdersModel> logger, IPersistenceService p)
    {
      _logger = logger;
      _p = p;
    }

    public IEnumerable<Order> OrderedOrders =>  _p.Data.Orders.Values
      .OrderByDescending(x => x.Time).ThenBy(x => x.Id);

    public void OnGet()
    {
    }
  }
}
