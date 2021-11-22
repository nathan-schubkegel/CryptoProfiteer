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
    void ImportTransactions(IEnumerable<PersistedTransaction> transactions);
    void UpdateCoinPrices(IEnumerable<CoinbaseCoinPrice> prices);
    void UpdateFriendlyNames(Dictionary<string, string> newFriendlyNames);
  }

  public class DataService : IDataService
  {
    private readonly object _lock = new object();
    private readonly ILogger<DataService> _logger;
    private readonly Dictionary<string, FriendlyName> _friendlyNames = new Dictionary<string, FriendlyName>();

    public IReadOnlyDictionary<string, Transaction> Transactions { get; private set; } = new Dictionary<string, Transaction>();
    public IReadOnlyDictionary<string, Order> Orders { get; private set; } = new Dictionary<string, Order>();
    public IReadOnlyDictionary<string, CoinSummary> CoinSummaries { get; private set; } = new Dictionary<string, CoinSummary>();
    public IReadOnlyDictionary<string, CoinPrice> CoinPrices { get; private set; } = new Dictionary<string, CoinPrice>();
    
    public DataService(ILogger<DataService> logger)
    {
      _logger = logger;
    }
    
    private FriendlyName GetOrCreateFriendlyName(string coinType)
    {
      // NOTE: assuming caller has lock held
      if (!_friendlyNames.TryGetValue(coinType, out var friendlyName))
      {
        friendlyName = new FriendlyName { Value = coinType };
        _friendlyNames[coinType] = friendlyName;
      }
      return friendlyName;
    }
    
    public void UpdateFriendlyNames(Dictionary<string, string> newFriendlyNames)
    {
      lock (_lock)
      {
        foreach ((var coinType, var friendlyName) in newFriendlyNames)
        {
          GetOrCreateFriendlyName(coinType).Value = friendlyName;
        }
      }
    }
    
    public void ImportTransactions(IEnumerable<PersistedTransaction> importedTransactions)
    {
      lock (_lock)
      {
        var newTransactions = new Dictionary<string, Transaction>(Transactions);
        foreach (var t in importedTransactions)
        {
          newTransactions[t.TradeId] = new Transaction(
            t ?? throw new Exception("invalid null transaction"),
            GetOrCreateFriendlyName(t.CoinType));
        }

        var newOrders = BuildOrders(newTransactions);
        var newCoinSummaries = BuildCoinSummaries(newTransactions, CoinPrices);
        
        Transactions = newTransactions;
        Orders = newOrders;
        CoinSummaries = newCoinSummaries;
      }
    }
    
    private Dictionary<string, Order> BuildOrders(
      IReadOnlyDictionary<string, Transaction> transactions)
    {
      var newOrders = new Dictionary<string, Order>();
      var orderTransactions = new List<Transaction>();
      foreach (var t in transactions.Values.OrderBy(x => x.Time))
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
        
        var order = new Order(orderTransactions, GetOrCreateFriendlyName(orderTransactions[0].CoinType));
        newOrders[order.Id] = order;
        orderTransactions.Clear();
        orderTransactions.Add(t);
      }
      if (orderTransactions.Count > 0)
      {
        var order = new Order(orderTransactions, GetOrCreateFriendlyName(orderTransactions[0].CoinType));
        newOrders[order.Id] = order;
      }
      return newOrders;
    }
    
    private Dictionary<string, CoinSummary> BuildCoinSummaries(
      IReadOnlyDictionary<string, Transaction> transactions,
      IReadOnlyDictionary<string, CoinPrice> coinPrices)
    {
      return transactions.Values.GroupBy(x => x.CoinType).ToDictionary(
        x => x.Key,
        x => new CoinSummary(
          coinType: x.Key,
          friendlyName: GetOrCreateFriendlyName(x.Key),
          coinCount: x.Where(y => y.TransactionType == TransactionType.Buy).Select(y => y.CoinCount).Sum()
            - x.Where(y => y.TransactionType == TransactionType.Sell).Select(y => y.CoinCount).Sum(),
          coinPrice: coinPrices.GetValueOrDefault(x.Key))
      );
    }
    
    public void UpdateCoinPrices(IEnumerable<CoinbaseCoinPrice> prices)
    {
      lock (_lock)
      {
        int newCount = 0;
        var newPrices = new Dictionary<string, CoinPrice>(CoinPrices);
        var now = DateTime.Now;
        foreach (var price in prices)
        {
          newPrices[price.CoinType] = new CoinPrice(price, GetOrCreateFriendlyName(price.CoinType), now);
          newCount++;
        }
        
        var newCoinSummaries = BuildCoinSummaries(Transactions, newPrices);
        
        CoinPrices = newPrices;
        CoinSummaries = newCoinSummaries;
      }
    }
  }
}