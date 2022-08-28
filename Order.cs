using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace CryptoProfiteer
{
  public class Order
  {
    private readonly Services _services;
    private Decimal? _receivedValueUsd;
    private Decimal? _paymentValueUsd;
    private Decimal? _receivedPerCoinCostUsd;
    private Decimal? _paymentPerCoinCostUsd;

    public string Id { get; }
    public string[] TransactionIds { get; }
    public TransactionType TransactionType { get; }
    public CryptoExchange Exchange { get; }
    public DateTime Time { get; }

    public string ReceivedCoinType { get; }
    public string PaymentCoinType { get; }

    public Decimal ReceivedCoinCount { get; }
    public Decimal PaymentCoinCount { get; }

    public Decimal? ReceivedValueUsd => _receivedValueUsd == null ? (_receivedValueUsd = _services.HistoricalCoinPriceService.ToUsd(ReceivedCoinCount, ReceivedCoinType, Time, Exchange)) : _receivedValueUsd;
    public Decimal? PaymentValueUsd => _paymentValueUsd == null ? (_paymentValueUsd = _services.HistoricalCoinPriceService.ToUsd(PaymentCoinCount, PaymentCoinType, Time, Exchange)) : _paymentValueUsd;

    public Decimal? ReceivedPerCoinCostUsd => _receivedPerCoinCostUsd == null ? (_receivedValueUsd == null ? null : MathOrNull(() => _receivedPerCoinCostUsd = _receivedValueUsd / ReceivedCoinCount)) : _receivedPerCoinCostUsd;
    public Decimal? PaymentPerCoinCostUsd => _paymentPerCoinCostUsd == null ? (_paymentValueUsd == null ? null : MathOrNull(() => _paymentPerCoinCostUsd = _paymentValueUsd / PaymentCoinCount)) : _paymentPerCoinCostUsd;

    private Decimal? MathOrNull(Func<Decimal> math)
    {
      try
      {
        return math();
      }
      catch
      {
        return (Decimal?)null;
      }
    }

    // TODO: some int? Tax cost
    //public int? TaxableTotalCostUsd => _taxableTotalCostUsd == null ? 
    //  TotalCostUsd != null ? (_taxableTotalCostUsd = (int)Math.Round(TotalCostUsd.Value, MidpointRounding.AwayFromZero)) : null
    //  : _taxableTotalCostUsd;
      
    public Order(List<Transaction> transactions, Services services)
    {
      _services = services;
      TransactionIds = transactions.Select(x => x.Id).ToArray();
      Id = TransactionIds.OrderBy(x => x).First(); // NOTE: PersistedTaxAssociation depends on this ID strategy
      foreach (var t in transactions)
      {
        // FUTURE: could be sanity-checking that all transactions have these fields the same enough
        Exchange = t.Exchange;
        TransactionType = t.TransactionType;
        Time = t.Time;
        ReceivedCoinType = t.ReceivedCoinType;
        PaymentCoinType = t.PaymentCoinType;

        PaymentCoinCount += t.PaymentCoinCount;
        ReceivedCoinCount += t.ReceivedCoinCount;
      }
    }
  }
}
