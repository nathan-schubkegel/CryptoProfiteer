using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CryptoProfiteer
{
  public class Transaction : ITransactionish
  {
    private readonly PersistedTransaction _data;
    private readonly Services _services;
    
    private Decimal? _receivedValueUsd;
    private Decimal? _paymentValueUsd;
    private Decimal? _receivedPerCoinCostUsd;
    private Decimal? _paymentPerCoinCostUsd;
    private Decimal? _paymentCoinsPerReceivedCoinListPrice;
    private Decimal? _receivedCoinsPerPaymentCoinListPrice;

    public Transaction(PersistedTransaction data, Services services)
    {
      _data = data;
      _services = services;

      // start work to learn historical prices
      _ = ReceivedValueUsd;
      _ = PaymentValueUsd;
      _ = ReceivedPerCoinCostUsd;
      _ = PaymentPerCoinCostUsd;
    }
    
    public string Id => _data.Id;
    public string OrderAggregationId => _data.OrderAggregationId;
    public TransactionType TransactionType => _data.TransactionType;
    public CryptoExchange Exchange => _data.Exchange;
    public DateTime Time => _data.Time;
    
    public string ReceivedCoinType => _data.ReceivedCoinType;
    public string PaymentCoinType => _data.PaymentCoinType;

    public Decimal ReceivedCoinCount => _data.ReceivedCoinCount;
    public Decimal PaymentCoinCount => _data.PaymentCoinCount;

    public Decimal? ReceivedValueUsd => _receivedValueUsd == null
      ? (_receivedValueUsd = _services.HistoricalCoinPriceService.ToUsd(ReceivedCoinCount, ReceivedCoinType, Time))
      : _receivedValueUsd;

    public Decimal? PaymentValueUsd => _paymentValueUsd == null
      ? (_paymentValueUsd = _services.HistoricalCoinPriceService.ToUsd(PaymentCoinCount, PaymentCoinType, Time))
      : _paymentValueUsd;
    
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

    // ListPrice is the transfer rate claimed by the exchange, before fees
    // ListPrice is "how many of this coin for 1 of the other kind of coin"
    public Decimal PaymentCoinsPerReceivedCoinListPrice => _paymentCoinsPerReceivedCoinListPrice != null ? _paymentCoinsPerReceivedCoinListPrice.Value :
      (_paymentCoinsPerReceivedCoinListPrice = _data.ListPrice > 0 ? _data.ListPrice.Value : (_data.ListPrice < 0 ? (1 / -_data.ListPrice.Value) : 0m)).Value;

    public Decimal ReceivedCoinsPerPaymentCoinListPrice => _receivedCoinsPerPaymentCoinListPrice != null ? _receivedCoinsPerPaymentCoinListPrice.Value :
      (_receivedCoinsPerPaymentCoinListPrice = _data.ListPrice < 0 ? -_data.ListPrice.Value : (_data.ListPrice > 0 ? (1 / _data.ListPrice.Value) : 0m)).Value;

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

    public PersistedTransaction ClonePersistedData() => _data.Clone();
  }
}
