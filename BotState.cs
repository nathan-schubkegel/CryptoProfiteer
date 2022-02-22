using System;

namespace CryptoProfiteer
{
  // NOTE: this class is serialized to JSON for the front-end
  public class BotState
  {
    public DateTime Time { get; set; }
    public Decimal Usd { get; set; }
    public Decimal CoinCount { get; set; }
    public string Note { get; set; }
  }
}