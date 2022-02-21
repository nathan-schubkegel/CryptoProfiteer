using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace CryptoProfiteer
{
  public class Order
  {
    private readonly FriendlyName _friendlyName;
    private readonly IHistoricalCoinPriceService _currencyConverter;

    public string Id { get; }
    public string[] TradeIds { get; }
    public TransactionType TransactionType { get; }
    public CryptoExchange Exchange { get; }
    public DateTime Time { get; }
    public string CoinType { get; }
    public string PaymentCoinType { get; }
    public string FriendlyName => _friendlyName.Value;
    public Decimal CoinCount { get; }
    public Decimal PerCoinCost { get; }
    public Decimal Fee { get; }
    public Decimal TotalCost { get; }

    private Decimal? _perCoinCostUsd;
    private Decimal? _feeUsd;
    private Decimal? _totalCostUsd;
    private int? _taxableTotalCostUsd;

    public Decimal? PerCoinCostUsd => _perCoinCostUsd == null ? 
      TotalCostUsd != null ? (_perCoinCostUsd = Math.Abs(TotalCostUsd.Value / CoinCount).SetMaxDecimals(2)) : null
        // TODO: 2 isn't always right for max decimals... we need some clever way to know what's more right...
        // But this will fly for a while because I'm only buying other coins with BTC and ETH for now. --nathschu
      : _perCoinCostUsd;
    public Decimal? FeeUsd => _feeUsd == null ? (_feeUsd = _currencyConverter.ToUsd(Fee, PaymentCoinType, Time, Exchange)) : _feeUsd;
    public Decimal? TotalCostUsd => _totalCostUsd == null ? (_totalCostUsd = _currencyConverter.ToUsd(TotalCost, PaymentCoinType, Time, Exchange)) : _totalCostUsd;
    public int? TaxableTotalCostUsd => _taxableTotalCostUsd == null ? 
      TotalCostUsd != null ? (_taxableTotalCostUsd = (int)Math.Round(TotalCostUsd.Value, MidpointRounding.AwayFromZero)) : null
      : _taxableTotalCostUsd;

    public Order(List<Transaction> transactions, FriendlyName friendlyName)
    {
      _currencyConverter = transactions.First().CurrencyConverter;
      TradeIds = transactions.Select(x => x.TradeId).ToArray();
      Id = TradeIds.OrderBy(x => x).First(); // NOTE: PersistedTaxAssociation depends on this ID strategy
      _friendlyName = friendlyName;
      int maxDecimalDigits = 0;
      foreach (var t in transactions)
      {
        // FUTURE: could be sanity-checking that all transactions have these fields the same enough
        Exchange = t.Exchange;
        TransactionType = t.TransactionType;
        Time = t.Time;
        CoinType = t.CoinType;
        PaymentCoinType = t.PaymentCoinType;

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
      PerCoinCost = PerCoinCost.SetMaxDecimals(maxDecimalDigits);
    }
  }
}
