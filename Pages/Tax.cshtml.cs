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
    
    public IEnumerable<Order> SalesNeedingTaxAssociation(string sortBy = null)
    {
      var coveredPurchases = new HashSet<string>(_data.TaxAssociations.Values.Select(t => t.Sale.Order.Id));
      var values = _data.Orders.Values.Where(o => o.TransactionType == TransactionType.Sell && !coveredPurchases.Contains(o.Id));
      switch (sortBy)
      {
        default:
        case "date": return values.OrderByDescending(x => x.Time).ThenBy(x => x.CoinType);
        case "dateAscending": return values.OrderBy(x => x.Time).ThenBy(x => x.CoinType);
        case "coinType": return values.OrderBy(x => x.CoinType).ThenByDescending(x => x.Time);
        case "coinTypeDescending": return values.OrderByDescending(x => x.CoinType).ThenByDescending(x => x.Time);
      }
    }
    
    public IEnumerable<(Order order, Decimal coinCountRemaining, int? costRemaining)> PurchasesNeedingTaxAssociation(string sortBy = null)
    {
      var coinCountUsedPerPurchase = new Dictionary<string, Decimal>();
      var costUsedPerPurchase = new Dictionary<string, int>();
      foreach (var taxAssociation in _data.TaxAssociations.Values)
      {
        foreach (var purchase in taxAssociation.Purchases)
        {
          var coinCount = coinCountUsedPerPurchase.GetValueOrDefault(purchase.Order.Id, 0m);
          coinCountUsedPerPurchase[purchase.Order.Id] = coinCount + purchase.ContributingCoinCount;
          
          var cost = costUsedPerPurchase.GetValueOrDefault(purchase.Order.Id, 0);
          costUsedPerPurchase[purchase.Order.Id] = cost + purchase.ContributingCost;
        }
      }

      var values = _data.Orders.Values
        .Where(o => o.TransactionType == TransactionType.Buy)
        .Where(o => !coinCountUsedPerPurchase.TryGetValue(o.Id, out var coinCountUsed) ||
                    coinCountUsed != o.CoinCount)
        .Select(o => 
          (
            order: o,
            coinCountRemaining: o.CoinCount - coinCountUsedPerPurchase.GetValueOrDefault(o.Id, 0m),
            costRemaining: o.TaxableTotalCostUsd == null ? (int?)null : o.TaxableTotalCostUsd.Value - costUsedPerPurchase.GetValueOrDefault(o.Id, 0)
          ));

      switch (sortBy)
      {
        default:
        case "date": return values.OrderByDescending(x => x.order.Time).ThenBy(x => x.order.CoinType);
        case "dateAscending": return values.OrderBy(x => x.order.Time).ThenBy(x => x.order.CoinType);
        case "coinType": return values.OrderBy(x => x.order.CoinType).ThenByDescending(x => x.order.Time);
        case "coinTypeDescending": return values.OrderByDescending(x => x.order.CoinType).ThenByDescending(x => x.order.Time);
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

    public void OnGet()
    {
    }
  }
}
