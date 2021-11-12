using System;

namespace CryptoProfiteer
{
  public enum TransactionType { Buy, Sell }

  public class Transaction
  {
    public string TradeId { get; set; }
    public TransactionType TransactionType { get; set; }
    public DateTimeOffset Time { get; set; }
    public string CoinType { get; set; }
    public Decimal CoinCount { get; set; }
    public Decimal PerCoinCost { get; set; }
    public Decimal Fee { get; set; }
    public Decimal TotalCost { get; set; }

    public void Cleanse()
    {
      // shrug - shoulda been right from the start
    }
  }
}
