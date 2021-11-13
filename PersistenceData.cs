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
  public class PersistenceData
  {
    public IReadOnlyDictionary<string, Transaction> Transactions { get; set; } = new Dictionary<string, Transaction>();
    public IReadOnlyDictionary<string, Order> Orders { get; set; } = new Dictionary<string, Order>();

    public void TakeFrom(PersistenceData other)
    {
      Transactions = other.Transactions;
      Orders = other.Orders;
    }

    public void Cleanse()
    {
      Transactions ??= new Dictionary<string, Transaction>();
      Orders ??= new Dictionary<string, Order>();
    }

    public void AddTransactions(List<Transaction> transactions)
    {
      // NOTE: assuming that all transactions that belong in an order are present when imported
      var newTransactions = transactions.Where(t => !Transactions.ContainsKey(t.TradeId))
        .OrderBy(x => x.Time)
        .ToList();
      
      var newOrders = new List<Order>();
      var orderTransactions = new List<Transaction>();
      foreach (var t in newTransactions)
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
        
        newOrders.Add(Order.Create(orderTransactions));
        orderTransactions.Clear();
        orderTransactions.Add(t);
      }
      if (orderTransactions.Count > 0)
      {
        newOrders.Add(Order.Create(orderTransactions));
        orderTransactions.Clear();
      }
      
      Transactions = Transactions.Values.Concat(newTransactions).ToDictionary(x => x.TradeId);
      Orders = Orders.Values.Concat(newOrders).ToDictionary(x => x.Id);
    }
  }
}