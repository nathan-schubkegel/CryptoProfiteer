using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CryptoProfiteer
{
  // NOTE: this type is JSON serialized/deserialized
  [JsonConverter(typeof(StringEnumConverter))] 
  public enum TransactionType 
  { 
    Buy, 
    Sell, 
    Adjustment 
  }
}