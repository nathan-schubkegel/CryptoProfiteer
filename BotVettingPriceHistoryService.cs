using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace CryptoProfiteer
{
  public interface IBotVettingPriceHistoryService
  {
  }

  public class BotVettingPriceHistoryService: IBotVettingPriceHistoryService
  {
    private readonly object _lock = new object();

    public BotVettingPriceHistoryService()
    {
    }
  }
}