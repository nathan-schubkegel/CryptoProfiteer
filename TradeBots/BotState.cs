using System;

namespace CryptoProfiteer.TradeBots
{
  // NOTE: this class is serialized to JSON for the front-end
  public class BotState
  {
    public string Action { get; set; }
    public DateTime Time { get; set; }
    public Decimal Usd { get; set; }
    public Decimal CoinCount { get; set; }
    public string Note { get; set; }
  }
}