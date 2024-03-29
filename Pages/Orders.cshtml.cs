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
    private readonly IPriceService _priceService;

    public OrdersModel(ILogger<OrdersModel> logger, IDataService data, IPriceService priceService)
    {
      _logger = logger;
      _data = data;
      _priceService = priceService;
    }

    public IEnumerable<Order> Orders(string sortBy = null)
    {
      var values = _data.Orders.Values;
      switch (sortBy)
      {
        default:
        case "date": return values.OrderByDescending(x => x.Time);
        case "dateAscending": return values.OrderBy(x => x.Time);
      }
    }

    public IEnumerable<CoinPrice> CoinPrices => _data.CoinTypes
      .Select(x => _priceService.TryGetCoinPrice(x)).Where(x => x != null);

    public void OnGet()
    {
    }
  }
}
