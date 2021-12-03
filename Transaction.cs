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
    public DateTimeOffset Time { get; set; }
    public string CoinType { get; set; }
    public Decimal CoinCount { get; set; }
    public Decimal PerCoinCost { get; set; }
    public Decimal Fee { get; set; }
    public Decimal TotalCost { get; set; }
  }

  public class Transaction
  {
    private readonly PersistedTransaction _data;
    private readonly FriendlyName _friendlyName;

    public Transaction(PersistedTransaction data, FriendlyName friendlyName)
    {
      _data = data;
      _friendlyName = friendlyName;
    }
    
    public string TradeId => _data.TradeId;
    public TransactionType TransactionType => _data.TransactionType;
    public CryptoExchange Exchange => _data.Exchange;
    public DateTimeOffset Time => _data.Time;
    public string CoinType => _data.CoinType;
    public string FriendlyName => _friendlyName.Value;
    public Decimal CoinCount => _data.CoinCount;
    public Decimal PerCoinCost => _data.PerCoinCost;
    public Decimal Fee => _data.Fee;
    public Decimal TotalCost => _data.TotalCost;
    
    public PersistedTransaction GetPersistedData() => _data;
  }
}
