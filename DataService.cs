using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace CryptoProfiteer
{
  public interface IDataService
  {
    IReadOnlyDictionary<string, Transaction> Transactions { get; }
    IReadOnlyDictionary<string, Order> Orders { get; }
    IReadOnlyDictionary<string, CoinSummary> CoinSummaries { get; }
    IReadOnlyDictionary<string, CoinPrice> CoinPrices { get; }
    void ImportTransactions(IEnumerable<Transaction> transactions);
    void UpdateCoinPrices(IEnumerable<CoinPrice> prices);
  }

  public class DataService : IDataService
  {
    private readonly object _lock = new object();
    public IReadOnlyDictionary<string, Transaction> Transactions { get; private set; } = new Dictionary<string, Transaction>();
    public IReadOnlyDictionary<string, Order> Orders { get; private set; } = new Dictionary<string, Order>();
    public IReadOnlyDictionary<string, CoinSummary> CoinSummaries { get; private set; } = new Dictionary<string, CoinSummary>();
    public IReadOnlyDictionary<string, CoinPrice> CoinPrices { get; private set; } = new Dictionary<string, CoinPrice>();

    public void ImportTransactions(IEnumerable<Transaction> transactions)
    {
      lock (_lock)
      {
        var newTransactions = new Dictionary<string, Transaction>(Transactions);
        foreach (var t in transactions)
        {
          newTransactions[t.TradeId] = t ?? throw new Exception("invalid null transaction");
        }

        var newOrders = new Dictionary<string, Order>();
        var orderTransactions = new List<Transaction>();
        foreach (var t in newTransactions.Values.OrderBy(x => x.Time))
        {
          if (orderTransactions.Count == 0)
          {
            orderTransactions.Add(t);
            continue;
          }
          
          var difference = orderTransactions[0].Time - t.Time;
          if (difference < TimeSpan.FromSeconds(1) && difference > TimeSpan.FromSeconds(-1))
          {
            orderTransactions.Add(t);
            continue;
          }
          
          var order = new Order(orderTransactions);
          newOrders[order.Id] = order;
          orderTransactions.Clear();
          orderTransactions.Add(t);
        }
        if (orderTransactions.Count > 0)
        {
          var order = new Order(orderTransactions);
          newOrders[order.Id] = order;
        }
        
        var newCoinSummaries = newTransactions.Values.GroupBy(x => x.CoinType).ToDictionary(
          x => x.Key,
          x => new CoinSummary(
            coinType: x.Key,
            coinCount: x.Where(y => y.TransactionType == TransactionType.Buy).Select(y => y.CoinCount).Sum()
              - x.Where(y => y.TransactionType == TransactionType.Sell).Select(y => y.CoinCount).Sum())
        );
        
        Transactions = newTransactions;
        Orders = newOrders;
        CoinSummaries = newCoinSummaries;
      }
    }
    
    public void UpdateCoinPrices(IEnumerable<CoinPrice> prices)
    {
      lock (_lock)
      {
        var newPrices = new Dictionary<string, CoinPrice>(CoinPrices);
        foreach (var price in prices)
        {
          newPrices[price.CoinType] = price;
        }
        CoinPrices = newPrices;
      }
    }
  }
}