using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CryptoProfiteer
{
  // NOTE: this type is JSON serialized/deserialized
  [JsonConverter(typeof(StringEnumConverter))] 
  public enum TransactionType { Buy, Sell }

  // NOTE: this type is JSON serialized/deserialized
  [JsonConverter(typeof(StringEnumConverter))] 
  public enum CryptoExchange { Coinbase, Kucoin }

  // NOTE: this type is JSON serialized/deserialized
  public class PersistedTransaction
  {
    public string TradeId { get; set; }
    public TransactionType TransactionType { get; set; }
    public CryptoExchange Exchange { get; set; }
    public DateTime Time { get; set; }
    public string CoinType { get; set; }
    public string PaymentCoinType { get; set; }
    public Decimal CoinCount { get; set; }
    public Decimal PerCoinCost { get; set; }
    public Decimal Fee { get; set; }
    public Decimal TotalCost { get; set; }
  }

  public class Transaction
  {
    private readonly PersistedTransaction _data;
    private readonly FriendlyName _friendlyName;
    private readonly IHistoricalCoinPriceService _currencyConverter;
    public IHistoricalCoinPriceService CurrencyConverter => _currencyConverter;
    
    private Decimal? _perCoinCostUsd;
    private Decimal? _feeUsd;
    private Decimal? _totalCostUsd;

    public Transaction(PersistedTransaction data, FriendlyName friendlyName, IHistoricalCoinPriceService currencyConverter)
    {
      _data = data;
      _friendlyName = friendlyName;
      _currencyConverter = currencyConverter;

      // start work to learn historical prices
      _ = PerCoinCostUsd;
    }
    
    public string TradeId => _data.TradeId;
    public TransactionType TransactionType => _data.TransactionType;
    public CryptoExchange Exchange => _data.Exchange;
    public DateTime Time => _data.Time;
    public string CoinType => _data.CoinType;
    public string PaymentCoinType => _data.PaymentCoinType;
    public string FriendlyName => _friendlyName.Value;
    public Decimal CoinCount => _data.CoinCount;
    public Decimal PerCoinCost => _data.PerCoinCost;
    public Decimal Fee => _data.Fee;
    public Decimal TotalCost => _data.TotalCost;

    public Decimal? PerCoinCostUsd => _perCoinCostUsd == null ? (_perCoinCostUsd = _currencyConverter.ToUsd(PerCoinCost, PaymentCoinType, Time, Exchange)) : _perCoinCostUsd;
    public Decimal? FeeUsd => _feeUsd == null ? (_feeUsd = _currencyConverter.ToUsd(Fee, PaymentCoinType, Time, Exchange)) : _feeUsd;
    public Decimal? TotalCostUsd => _totalCostUsd == null ? (_totalCostUsd = _currencyConverter.ToUsd(TotalCost, PaymentCoinType, Time, Exchange)) : _totalCostUsd;

    public PersistedTransaction GetPersistedData() => _data;
  }
}
