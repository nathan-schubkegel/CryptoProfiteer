using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace CryptoProfiteer
{
  public class Order : ITransactionish
  {
    private readonly Services _services;
    private Decimal? _receivedValueUsd;
    private Decimal? _paymentValueUsd;
    private int? _taxablePaymentValueUsd;
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

    public Decimal? ReceivedValueUsd => _receivedValueUsd == null
      ? (_receivedValueUsd = _services.HistoricalCoinPriceService.ToUsd(ReceivedCoinCount, ReceivedCoinType, Time, Exchange))
      : _receivedValueUsd;

    public Decimal? PaymentValueUsd => _paymentValueUsd == null
      ? (_paymentValueUsd = _services.HistoricalCoinPriceService.ToUsd(PaymentCoinCount, PaymentCoinType, Time, Exchange))
      : _paymentValueUsd;

    public int? TaxablePaymentValueUsd => _taxablePaymentValueUsd == null ?
      PaymentValueUsd != null 
        ? (_taxablePaymentValueUsd = (int)Math.Round(PaymentValueUsd.Value, MidpointRounding.AwayFromZero)) 
        : null
      : _taxablePaymentValueUsd;

    public Decimal? ReceivedPerCoinCostUsd => _receivedPerCoinCostUsd == null ? 
      ReceivedValueUsd == null 
        ? null 
        : MathOrNull(() => _receivedPerCoinCostUsd = ReceivedValueUsd.Value / ReceivedCoinCount)
      : _receivedPerCoinCostUsd;

    public Decimal? PaymentPerCoinCostUsd => _paymentPerCoinCostUsd == null ? 
      PaymentValueUsd == null 
        ? null 
        : MathOrNull(() => _paymentPerCoinCostUsd = PaymentValueUsd.Value / PaymentCoinCount)
      : _paymentPerCoinCostUsd;

    private static Decimal? MathOrNull(Func<Decimal?> math)
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
    
    public bool IsTaxableSale => TransactionType == TransactionType.Trade && PaymentCoinType != "USD";
    public bool IsTaxablePurchase => TransactionType == TransactionType.Trade && ReceivedCoinType != "USD";

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
