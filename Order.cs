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
    private int? _taxableReceivedValueUsd;
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

    public Decimal? ReceivedValueUsd =>
      // if we traded for USD, then the fair market value was the USD we traded
      PaymentCoinType == "USD" ? PaymentCoinCount :
      _receivedValueUsd == null ? (
        // Futures are simple
        TransactionType == TransactionType.FuturesPnl ? (_receivedValueUsd = _services.HistoricalCoinPriceService.ToUsd(ReceivedCoinCount, ReceivedCoinType, Time)) :
        // if we traded for something USD-ish, then use the fair market value of the USD-ish coin
        // because I've received wildly different market prices (like 1.19 vs 1.3) from asking Kucoin's price history... don't stinkin' trust it anymore...
        SomeUtils.IsBasicallyUsd(PaymentCoinType)
          ? (_receivedValueUsd = _services.HistoricalCoinPriceService.ToUsd(PaymentCoinCount, PaymentCoinType, Time))
          : (_receivedValueUsd = _services.HistoricalCoinPriceService.ToUsd(ReceivedCoinCount, ReceivedCoinType, Time))
      ) : _receivedValueUsd;

    public Decimal? PaymentValueUsd =>
      // if we traded for USD, then the fair market value was the USD we traded
      ReceivedCoinType == "USD" ? ReceivedCoinCount :
      _paymentValueUsd == null ? (
        // Futures are simple
        TransactionType == TransactionType.FuturesPnl ? (_paymentValueUsd = _services.HistoricalCoinPriceService.ToUsd(PaymentCoinCount, PaymentCoinType, Time)) :
        // if we traded for something USD-ish, then use the fair market value of the USD-ish coin
        // because I've received wildly different market prices (like 1.19 vs 1.3) from asking Kucoin's price history... don't stinkin' trust it anymore...
        SomeUtils.IsBasicallyUsd(ReceivedCoinType)
          ? (_paymentValueUsd = _services.HistoricalCoinPriceService.ToUsd(ReceivedCoinCount, ReceivedCoinType, Time))
          : (_paymentValueUsd = _services.HistoricalCoinPriceService.ToUsd(PaymentCoinCount, PaymentCoinType, Time))
      ) : _paymentValueUsd;

    public int? TaxableReceivedValueUsd => _taxableReceivedValueUsd == null ?
      ReceivedValueUsd != null
        ? (_taxableReceivedValueUsd = (int)Math.Round(ReceivedValueUsd.Value, MidpointRounding.AwayFromZero))
        : null
      : _taxableReceivedValueUsd;

    public int? TaxablePaymentValueUsd => _taxablePaymentValueUsd == null ?
      PaymentValueUsd != null 
        ? (_taxablePaymentValueUsd = (int)Math.Round(PaymentValueUsd.Value, MidpointRounding.AwayFromZero)) 
        : null
      : _taxablePaymentValueUsd;

    public Decimal? ReceivedPerCoinCostUsd => _receivedPerCoinCostUsd == null ? 
      (ReceivedValueUsd == null || (double)ReceivedCoinCount == 0)
        ? null 
        : MathOrNull(() => _receivedPerCoinCostUsd = ReceivedValueUsd.Value / ReceivedCoinCount)
      : _receivedPerCoinCostUsd;

    public Decimal? PaymentPerCoinCostUsd => _paymentPerCoinCostUsd == null ? 
      (PaymentValueUsd == null || (double)PaymentCoinCount == 0)
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
    
    public bool IsTaxableFuturesGain => TransactionType == TransactionType.FuturesPnl && ReceivedCoinCount > 0;
    public bool IsTaxableFuturesLoss => TransactionType == TransactionType.FuturesPnl && PaymentCoinCount > 0;
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
