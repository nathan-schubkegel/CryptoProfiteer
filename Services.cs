using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CryptoProfiteer
{
  public class Services
  {
    public IFriendlyNameService FriendlyNameService;
    public IHistoricalCoinPriceService HistoricalCoinPriceService;
  }
}