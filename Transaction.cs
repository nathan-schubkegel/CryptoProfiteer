using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CryptoProfiteer
{
  public class Transaction
  {
    private readonly PersistedTransaction _data;
    private readonly Services _services;
    
    private Decimal? _receivedValueUsd;
    private Decimal? _paymentValueUsd;

    public Transaction(PersistedTransaction data, Services services)
    {
      _data = data;
      _services = services;

      // start work to learn historical prices
      _ = ReceivedValueUsd;
      _ = PaymentValueUsd;
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

    public Decimal? ReceivedValueUsd => _receivedValueUsd == null ? (_receivedValueUsd = _services.HistoricalCoinPriceService.ToUsd(ReceivedCoinCount, ReceivedCoinType, Time, Exchange)) : _receivedValueUsd;
    public Decimal? PaymentValueUsd => _paymentValueUsd == null ? (_paymentValueUsd = _services.HistoricalCoinPriceService.ToUsd(PaymentCoinCount, PaymentCoinType, Time, Exchange)) : _paymentValueUsd;
    
    // TODO: these probably make sense, but it's hard for me to reason about them
    //public Decimal PaymentCoinExchangeRate => 
    //public Decimal ReceivedCoinExchangeRate => 
    
    // TODO: same here
    //public Decimal? PaymentCoinExchangeRateUsd => 
    //public Decimal? ReceivedCoinExchangeRateUsd => 

    public PersistedTransaction ClonePersistedData() => _data.Clone();
  }
}
