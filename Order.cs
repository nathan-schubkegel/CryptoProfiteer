using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace CryptoProfiteer
{
  public class Order
  {
    private readonly FriendlyName _friendlyName;

    public string Id { get; }
    public string[] TradeIds { get; }
    public TransactionType TransactionType { get; }
    public DateTimeOffset Time { get; }
    public string CoinType { get; }
    public string FriendlyName => _friendlyName.Value;
    public Decimal CoinCount { get; }
    public Decimal PerCoinCost { get; }
    public Decimal Fee { get; }
    public Decimal TotalCost { get; }

    public Order(List<Transaction> transactions, FriendlyName friendlyName)
    {
      TradeIds = transactions.Select(x => x.TradeId).ToArray();
      Id = TradeIds.OrderBy(x => x).First(); // NOTE: PersistedTaxAssociation depends on this ID strategy
      _friendlyName = friendlyName;
      int maxDecimalDigits = 0;
      foreach (var t in transactions)
      {
        // FUTURE: could be sanity-checking that all transactions have these fields the same enough
        TransactionType = t.TransactionType;
        Time = t.Time;
        CoinType = t.CoinType;

        CoinCount += t.CoinCount;
        Fee += t.Fee;
        TotalCost += t.TotalCost;
        
        // count digits to the right of decimal point in 'PerCoinCost'
        string c = t.PerCoinCost.ToString(CultureInfo.InvariantCulture);
        int i = c.IndexOf('.');
        if (i >= 0)
        {
          maxDecimalDigits = Math.Max(maxDecimalDigits, c.Length - i - 1);
        }
      }
      
      PerCoinCost = Math.Abs(TotalCost / CoinCount);
      
      // Decimal offers 29 digits of decimal precision, but that's an unnecessary firehose.
      // Trim that down to whatever the original transactions had
      if (maxDecimalDigits > 0)
      {
        string c = PerCoinCost.ToString(CultureInfo.InvariantCulture);
        int i = c.IndexOf('.');
        if (i >= 0)
        {
          int decimalDigits = c.Length - i - 1;
          if (decimalDigits > maxDecimalDigits)
          {
            c = c.Substring(0, c.Length - (decimalDigits - maxDecimalDigits));
            PerCoinCost = decimal.Parse(c, NumberStyles.Float, CultureInfo.InvariantCulture);
          }
        }
      }
      
      // NOTE: Hans Passant suggested it can be done like these (where 100 = 2 decimal places)
      // but I like my ugly code better - less potentially loss - ha, as if that matters... 29 digits of room to use!
      // value = Math.Truncate(100 * value) / 100;
    }
  }
}
