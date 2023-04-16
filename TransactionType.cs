using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CryptoProfiteer
{
  // NOTE: this type is JSON serialized/deserialized
  [JsonConverter(typeof(StringEnumConverter))] 
  public enum TransactionType 
  { 
    Trade,
    Adjustment,
    FuturesPnl
  }
  
  // NOTE: this type is JSON serialized/deserialized
  [JsonConverter(typeof(StringEnumConverter))] 
  public enum TransactionType_v04
  { 
    Buy,
    Sell,
    Adjustment 
  }
}