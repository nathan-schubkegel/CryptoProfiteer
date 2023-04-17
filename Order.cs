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
      // For losses on futures, the received value must be reported as zero or else the tax page will think the cost basis of the 0 USDT received was 144 USDT (wrong! the cost basis was zero!)
      TransactionType == TransactionType.FuturesPnl && ReceivedCoinCount == 0m ? 0m :
      // if we traded for USD, then the fair market value was the USD we traded
      PaymentCoinType == "USD" ? PaymentCoinCount :
      _receivedValueUsd == null ? (
        // if we traded for something USD-ish, then use the fair market value of the USD-ish coin
        // because I've received wildly different market prices (like 1.19 vs 1.3) from asking Kucoin's price history... don't stinkin' trust it anymore...
        SomeUtils.IsBasicallyUsd(PaymentCoinType)
          ? (_receivedValueUsd = _services.HistoricalCoinPriceService.ToUsd(PaymentCoinCount, PaymentCoinType, Time))
          : (_receivedValueUsd = _services.HistoricalCoinPriceService.ToUsd(ReceivedCoinCount, ReceivedCoinType, Time))
      ) : _receivedValueUsd;

    public Decimal? PaymentValueUsd =>
      // For gains on futures, the payment value must be reported as zero or else the tax page will think the cost basis of the 144 USDT received was 144 USDT (wrong! The cost basis was zero!)
      TransactionType == TransactionType.FuturesPnl && PaymentCoinCount == 0m ? 0m :
      // if we traded for USD, then the fair market value was the USD we traded
      ReceivedCoinType == "USD" ? ReceivedCoinCount :
      _paymentValueUsd == null ? (
        // if we traded for something USD-ish, then use the fair market value of the USD-ish coin
        // because I've received wildly different market prices (like 1.19 vs 1.3) from asking Kucoin's price history... don't stinkin' trust it anymore...
        SomeUtils.IsBasicallyUsd(ReceivedCoinType)
          ? (_paymentValueUsd = _services.HistoricalCoinPriceService.ToUsd(ReceivedCoinCount, ReceivedCoinType, Time))
          : (_paymentValueUsd = _services.HistoricalCoinPriceService.ToUsd(PaymentCoinCount, PaymentCoinType, Time))
      ) : _paymentValueUsd;

    public int? TaxableReceivedValueUsd => _taxableReceivedValueUsd == null ?
      PaymentValueUsd != null
        ? (_taxableReceivedValueUsd = (int)Math.Round(ReceivedValueUsd.Value, MidpointRounding.AwayFromZero))
        : null
      : _taxableReceivedValueUsd;

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
    
    public bool IsTaxableSale => (TransactionType == TransactionType.Trade && PaymentCoinType != "USD") || (TransactionType == TransactionType.FuturesPnl);
    public bool IsTaxablePurchase => (TransactionType == TransactionType.Trade && ReceivedCoinType != "USD") || (TransactionType == TransactionType.FuturesPnl);

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
