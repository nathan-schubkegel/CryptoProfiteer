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

    public IEnumerable<Order> OrderedOrders =>  _data.Orders.Values
      .OrderByDescending(x => x.Time).ThenBy(x => x.Id);

    public void OnGet()
    {
    }
  }
}
