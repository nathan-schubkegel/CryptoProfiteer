using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace CryptoProfiteer.Pages
{
  public class TaxModel: PageModel
  {
    private readonly ILogger<TaxModel> _logger;
    private readonly IDataService _data;

    public TaxModel(ILogger<TaxModel> logger, IDataService data)
    {
      _logger = logger;
      _data = data;
    }
    
    public IEnumerable<Order> OrdersNeedingTaxAssociation(string sortBy = null)
    {
      var coveredOrders = new HashSet<string>(_data.TaxAssociations.Values.SelectMany(t => t.Parts).Select(p => p.Order.Id));
      var values = _data.Orders.Values.Where(o => o.TransactionType == TransactionType.Sell && !coveredOrders.Contains(o.Id));
      switch (sortBy)
      {
        default:
        case "date": return values.OrderByDescending(x => x.Time).ThenBy(x => x.CoinType);
        case "dateAscending": return values.OrderBy(x => x.Time).ThenBy(x => x.CoinType);
        case "coinType": return values.OrderBy(x => x.CoinType).ThenByDescending(x => x.Time);
        case "coinTypeDescending": return values.OrderByDescending(x => x.CoinType).ThenByDescending(x => x.Time);
      }
    }
    
    public IEnumerable<Order> UnassociatedPurchaseOrders(string sortBy = null)
    {
      var coveredOrders = new HashSet<string>(_data.TaxAssociations.Values.SelectMany(t => t.Parts).Select(p => p.Order.Id));
      var values = _data.Orders.Values.Where(o => o.TransactionType == TransactionType.Buy && !coveredOrders.Contains(o.Id));
      switch (sortBy)
      {
        default:
        case "date": return values.OrderByDescending(x => x.Time).ThenBy(x => x.CoinType);
        case "dateAscending": return values.OrderBy(x => x.Time).ThenBy(x => x.CoinType);
        case "coinType": return values.OrderBy(x => x.CoinType).ThenByDescending(x => x.Time);
        case "coinTypeDescending": return values.OrderByDescending(x => x.CoinType).ThenByDescending(x => x.Time);
      }
    }

    public IEnumerable<TaxAssociation> TaxAssociations(string sortBy = null)
    {
      var values = _data.TaxAssociations.Values;
      switch (sortBy)
      {
        default:
        case "date": return values.OrderByDescending(x => x.Time).ThenBy(x => x.CoinType);
        case "dateAscending": return values.OrderBy(x => x.Time).ThenBy(x => x.CoinType);
        case "coinType": return values.OrderBy(x => x.CoinType).ThenByDescending(x => x.Time);
        case "coinTypeDescending": return values.OrderByDescending(x => x.CoinType).ThenByDescending(x => x.Time);
      }
    }
    
    //public IEnumerable<CoinPrice> CoinPrices => _data.Orders.Values.Select(x => x.CoinType).Distinct()
//      .Select(x => _data.CoinPrices.TryGetValue(x, out var price) ? price : null).Where(x => x != null);

    public void OnGet()
    {
    }
  }
}
