using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoProfiteer
{
  [ApiController]
  [Route("api")]
  public class TheController : ControllerBase
  {
    private readonly IServiceProvider _provider;
    private readonly ILogger<TheController> _logger;

    public TheController(IServiceProvider provider, ILogger<TheController> logger)
    {
      _provider = provider;
      _logger = logger;
    }

    [HttpGet("transactions")]
    public IEnumerable<Transaction> GetTransactions()
    {
      var data = _provider.GetRequiredService<IPersistenceService>().Data;
      lock (data)
      {
        return data.Transactions.ToList();
      }
    }
    
    [HttpPost("fillsCsv")]
    public async Task<IActionResult> PostFillsCsv(IFormFile file)
    {
      var lines = new List<string>();
      if (file.Length > 0)
      {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;
        using var sr = new StreamReader(ms);
        string line;
        _logger.LogInformation($"Importing {(file.FileName ?? "fills.csv")}...");
        while (null != (line = sr.ReadLine()))
        {
          lines.Add(line);
        }
      }

      var transactions = new List<Transaction>();
      if (lines.Count > 0)
      {
        var headerFields = Csv.Parse(lines[0]);

        int IndexOrBust(string name)
        {
          int index = headerFields.IndexOf(name);
          if (index < 0)
          {
            throw new Exception("CSV header lacks required '" + name + "' field");
          }
          return index;
        }

        var tradeIdIndex = IndexOrBust("trade id");
        var buySellIndex = IndexOrBust("side");
        var createdAtIndex = IndexOrBust("created at");
        var coinCountIndex = IndexOrBust("size");
        var coinTypeIndex = IndexOrBust("size unit");
        var perCoinPriceIndex = IndexOrBust("price");
        var feeIndex = IndexOrBust("fee");
        var totalCostIndex = IndexOrBust("total");
        
        int lineNumber = 1;
        foreach (var line in lines.Skip(1))
        {
          lineNumber++;
          var fields = Csv.Parse(line);
          if (fields.Count == 0) continue;
          if (fields.Count != headerFields.Count)
          {
            throw new Exception($"CSV line {lineNumber} has {fields.Count} fields; different from header line which has {headerFields.Count} fields; aborting.");
          }

          if (!Enum.TryParse<TransactionType>(fields[buySellIndex], ignoreCase: true, out var transactionType))
          {
            throw new Exception($"CSV line {lineNumber} has unrecognized field {buySellIndex + 1} \"{fields[buySellIndex]}\"; expected one of " + string.Join(",", Enum.GetNames(typeof(TransactionType)).Select(x => "\"" + x + "\"")));
          }

          if (!DateTimeOffset.TryParse(fields[createdAtIndex], out var createdAtTime))
          {
            throw new Exception($"CSV line {lineNumber} has non-date/time field {createdAtIndex + 1} \"{fields[createdAtIndex]}\"; expected date/time such as \"{DateTimeOffset.Now.ToString("o")}\"");
          }

          if (!Decimal.TryParse(fields[coinCountIndex], out var coinCount))
          {
            throw new Exception($"CSV line {lineNumber} has non-numeric field {coinCountIndex + 1} \"{fields[coinCountIndex]}\"; expected numeric value such as \"3.17\"");
          }

          if (!Decimal.TryParse(fields[perCoinPriceIndex], out var perCoinPrice))
          {
            throw new Exception($"CSV line {lineNumber} has non-numeric field {perCoinPriceIndex + 1} \"{fields[perCoinPriceIndex]}\"; expected numeric value such as \"3.17\"");
          }

          if (!Decimal.TryParse(fields[feeIndex], out var fee))
          {
            throw new Exception($"CSV line {lineNumber} has non-numeric field {feeIndex + 1} \"{fields[feeIndex]}\"; expected numeric value such as \"3.17\"");
          }

          if (!Decimal.TryParse(fields[totalCostIndex], out var totalCost))
          {
            throw new Exception($"CSV line {lineNumber} has non-numeric field {totalCostIndex + 1} \"{fields[totalCostIndex]}\"; expected numeric value such as \"3.17\"");
          }

          var transaction = new Transaction
          {
            TradeId = fields[tradeIdIndex],
            TransactionType = transactionType,
            Time = createdAtTime,
            CoinType = fields[coinTypeIndex],
            CoinCount = coinCount,
            PerCoinCost = perCoinPrice,
            Fee = fee,
            TotalCost = totalCost,
          };
          transactions.Add(transaction);
        }
      }

      if (transactions.Count > 0)
      {
        var service = _provider.GetRequiredService<IPersistenceService>();
        lock (service.Data)
        {
          var knownTradeIds = new HashSet<string>(service.Data.Transactions.Select(x => x.TradeId));
          service.Data.Transactions = service.Data.Transactions
            .Concat(transactions.Where(t => !knownTradeIds.Contains(t.TradeId)))
            .ToArray();
          service.MarkDirty();
        }
      }

      return Ok();
    }
  }
}
