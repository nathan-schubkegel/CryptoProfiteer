using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoProfiteer
{
  public class Order
  {
    public string Id { get; }
    public string[] TradeIds { get; }
    public TransactionType TransactionType { get; }
    public DateTimeOffset Time { get; }
    public string CoinType { get; }
    public Decimal CoinCount { get; }
    public Decimal PerCoinCost { get; }
    public Decimal Fee { get; }
    public Decimal TotalCost { get; }

    public Order(List<Transaction> transactions)
    {
      TradeIds = transactions.Select(x => x.TradeId).ToArray();
      Id = TradeIds.OrderBy(x => x).First();
      foreach (var t in transactions)
      {
        // FUTURE: could be sanity-checking that all transactions have these fields the same enough
        TransactionType = t.TransactionType;
        Time = t.Time;
        CoinType = t.CoinType;

        // TODO: average this
        PerCoinCost = t.PerCoinCost;

        CoinCount += t.CoinCount;
        Fee += t.Fee;
        TotalCost += t.TotalCost;
      }
    }
  }
}
