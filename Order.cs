using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoProfiteer
{
  public class Order
  {
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string[] TradeIds { get; set; }
    public TransactionType TransactionType { get; set; }
    public DateTimeOffset Time { get; set; }
    public string CoinType { get; set; }
    public Decimal CoinCount { get; set; }
    public Decimal PerCoinCost { get; set; }
    public Decimal Fee { get; set; }
    public Decimal TotalCost { get; set; }
    
    public static Order Create(List<Transaction> transactions)
    {
      var order = new Order();
      order.TradeIds = transactions.Select(x => x.TradeId).ToArray();
      foreach (var t in transactions)
      {
        // FUTURE: could be sanity-checking that all transactions have these fields the same enough
        order.TransactionType = t.TransactionType;
        order.Time = t.Time;
        order.CoinType = t.CoinType;
        
        // TODO: average this
        order.PerCoinCost = t.PerCoinCost;

        order.CoinCount += t.CoinCount;
        order.Fee += t.Fee;
        order.TotalCost += t.TotalCost;
      }
      
      return order;
    }
  }
}
