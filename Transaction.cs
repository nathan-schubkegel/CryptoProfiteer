using System;

namespace CryptoProfiteer
{
  // NOTE: this type is JSON serialized/deserialized
  public enum TransactionType { Buy, Sell }

  // NOTE: this type is JSON serialized/deserialized
  public class Transaction
  {
    // TODO: use a JsonConstructor and make these fields immutable, for what it's worth
    
    public string TradeId { get; set; }
    public TransactionType TransactionType { get; set; }
    public DateTimeOffset Time { get; set; }
    public string CoinType { get; set; }
    public Decimal CoinCount { get; set; }
    public Decimal PerCoinCost { get; set; }
    public Decimal Fee { get; set; }
    public Decimal TotalCost { get; set; }
  }
}
