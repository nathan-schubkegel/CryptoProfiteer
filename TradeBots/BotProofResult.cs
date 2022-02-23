using System;
using System.Collections.Generic;

namespace CryptoProfiteer.TradeBots
{
  // NOTE: this class is serialized to JSON for the front-end
  public class BotProofResult
  {
    public List<BotState> BotStates { get; set; } = new List<BotState>();
    public Decimal FinalUsd { get; set; }
    public Decimal FinalCoinCount { get; set; }
    public bool IsSunk { get; set; }
  }
}