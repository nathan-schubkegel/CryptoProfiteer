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

    public IEnumerable<CoinSummary> Summaries(string sortBy = null)
    {
      var values = _data.CoinSummaries.Values;
      switch (sortBy)
      {
        default:
        case "cashValue": return values.OrderByDescending(x => x.CashValue).ThenBy(x => x.CoinType);
        case "cashValueAscending": return values.OrderBy(x => x.CashValue).ThenBy(x => x.CoinType);
        case "coinTypeDescending": return values.OrderByDescending(x => x.CoinType);
        case "coinType": return values.OrderBy(x => x.CoinType);
      }
    }
    
    public Dictionary<string, List<Order>> OrdersByCoinType(string sortBy = null)
    {
      var result = new Dictionary<string, List<Order>>();
      var orders = _data.Orders;
      foreach (var o in orders.Values)
      {
        // account for received coin type
        if (!result.TryGetValue(o.ReceivedCoinType, out var bucket))
        {
          bucket = new List<Order>();
          result[o.ReceivedCoinType] = bucket;
        }
        bucket.Add(o);
        
        // account for payment coin type
        if (!result.TryGetValue(o.PaymentCoinType, out bucket))
        {
          bucket = new List<Order>();
          result[o.PaymentCoinType] = bucket;
        }
        bucket.Add(o);
        
        // account for adjustments to USD
        if (bucket.Count >= 2 && bucket[bucket.Count - 1] == bucket[bucket.Count - 2]) bucket.RemoveAt(bucket.Count - 1);
      }
      
      foreach (var coinType in result.Keys.ToList())
      {
        var values = result[coinType];
        switch (sortBy)
        {
          default:
          case "date": result[coinType] = values.OrderByDescending(x => x.Time).ToList(); break;
          case "dateAscending": result[coinType] = values.OrderBy(x => x.Time).ToList(); break;
        }
      }
      
      return result;
    }

    public void OnGet()
    {

    }
  }
}
