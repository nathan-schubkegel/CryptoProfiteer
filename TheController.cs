using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace CryptoProfiteer
{
  [ApiController]
  [Route("api")]
  public class TheController : ControllerBase
  {
    private readonly IDataService _dataService;
    private readonly ILogger<TheController> _logger;

    public TheController(IDataService dataService, ILogger<TheController> logger)
    {
      _dataService = dataService;
      _logger = logger;
    }

    [HttpPost("coinbaseFillsCsv")]
    public async Task<IActionResult> PostCoinbaseFillsCsv(IFormFile file)
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

      var transactions = new List<PersistedTransaction>();
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
        var paymentTypeIndex = IndexOrBust("price/fee/total unit");
        var product = IndexOrBust("product");
        
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

          if (!DateTime.TryParse(fields[createdAtIndex], out var createdAtTime))
          {
            throw new Exception($"CSV line {lineNumber} has non-date/time field {createdAtIndex + 1} \"{fields[createdAtIndex]}\"; expected date/time such as \"{DateTime.Now.ToString("o")}\"");
          }

          if (!Decimal.TryParse(fields[coinCountIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var coinCount))
          {
            throw new Exception($"CSV line {lineNumber} has non-numeric field {coinCountIndex + 1} \"{fields[coinCountIndex]}\"; expected numeric value such as \"3.17\"");
          }

          if (!Decimal.TryParse(fields[perCoinPriceIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var perCoinPrice))
          {
            throw new Exception($"CSV line {lineNumber} has non-numeric field {perCoinPriceIndex + 1} \"{fields[perCoinPriceIndex]}\"; expected numeric value such as \"3.17\"");
          }

          if (!Decimal.TryParse(fields[feeIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var fee))
          {
            throw new Exception($"CSV line {lineNumber} has non-numeric field {feeIndex + 1} \"{fields[feeIndex]}\"; expected numeric value such as \"3.17\"");
          }

          if (!Decimal.TryParse(fields[totalCostIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var totalCost))
          {
            throw new Exception($"CSV line {lineNumber} has non-numeric field {totalCostIndex + 1} \"{fields[totalCostIndex]}\"; expected numeric value such as \"3.17\"");
          }
          
          if (fields[paymentTypeIndex] != "USD")
          {
            throw new Exception($"CSV line {lineNumber} has unexpected field {paymentTypeIndex + 1} \"{fields[paymentTypeIndex]}\"; only USD is currently supported");
          }

          if (!fields[product].EndsWith("-USD"))
          {
            throw new Exception($"CSV line {lineNumber} has unexpected field {product + 1} \"{fields[product]}\"; only values ending in \"-USD\" are currently supported");
          }

          var transaction = new PersistedTransaction
          {
            TradeId = fields[tradeIdIndex],
            TransactionType = transactionType,
            Exchange = CryptoExchange.Coinbase,
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
        _dataService.ImportTransactions(transactions);
      }

      return Ok();
    }
    
    [HttpPost("kucoinOrdersCsv")]
    public async Task<IActionResult> PostKucoinOrdersCsv(IFormFile file)
    {
      var lines = new List<string>();
      if (file.Length > 0)
      {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;
        using var sr = new StreamReader(ms);
        string line;
        _logger.LogInformation($"Importing {(file.FileName ?? "kucoin_orders.csv")}...");
        while (null != (line = sr.ReadLine()))
        {
          lines.Add(line);
        }
      }

      var transactions = new List<PersistedTransaction>();
      if (lines.Count > 0)
      {
        var headerFields = Csv.Parse(lines[0]);
        for (int i = 0; i < headerFields.Count; i++) headerFields[i] = headerFields[i].Trim(); // because I added spaces to some of my data --nathschu

        int IndexOrBust(string name)
        {
          int index = headerFields.IndexOf(name);
          if (index < 0)
          {
            throw new Exception("CSV header lacks required '" + name + "' field");
          }
          return index;
        }

        var tradeIdIndex = IndexOrBust("id");
        var buySellIndex = IndexOrBust("side");
        var createdAtIndex = IndexOrBust("orderCreatedAt");
        var coinCountIndex = IndexOrBust("dealSize");
        var perCoinPriceIndex = IndexOrBust("averagePrice");
        var feeIndex = IndexOrBust("fee");
        var coinTypeIndex = IndexOrBust("symbol");
        var tradeTypeIndex = IndexOrBust("type");
        var feeCurrencyIndex = IndexOrBust("feeCurrency");
        var dealFundsIndex = IndexOrBust("dealFunds");

        int lineNumber = 1;
        foreach (var line in lines.Skip(1))
        {
          lineNumber++;
          var fields = Csv.Parse(line);
          for (int i = 0; i < fields.Count; i++) fields[i] = fields[i].Trim(); // because I added spaces to some of my data --nathschu
          if (fields.Count == 0) continue;
          if (fields.Count != headerFields.Count)
          {
            throw new Exception($"CSV line {lineNumber} has {fields.Count} fields; different from header line which has {headerFields.Count} fields; aborting.");
          }

          if (!Enum.TryParse<TransactionType>(fields[buySellIndex], ignoreCase: true, out var transactionType))
          {
            throw new Exception($"CSV line {lineNumber} has unrecognized field {buySellIndex + 1} \"{fields[buySellIndex]}\"; expected one of " + string.Join(",", Enum.GetNames(typeof(TransactionType)).Select(x => "\"" + x + "\"")));
          }

          if (!DateTime.TryParseExact(fields[createdAtIndex], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var createdAtTime))
          {
            throw new Exception($"CSV line {lineNumber} has non-date/time field {createdAtIndex + 1} \"{fields[createdAtIndex]}\"; expected date/time such as \"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}\"");
          }
          
          // my experience is Kucoin times are reported 16 hours ahead of when I actually bought them
          // maybe kucoin is reporting locale-sensitive time (I'm Pacific -8 hrs) but it's locale-ing the wrong way?
          // oh well. just fix it.
          createdAtTime = createdAtTime - TimeSpan.FromHours(16);

          if (!Decimal.TryParse(fields[coinCountIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var coinCount))
          {
            throw new Exception($"CSV line {lineNumber} has non-numeric field {coinCountIndex + 1} \"{fields[coinCountIndex]}\"; expected numeric value such as \"3.17\"");
          }

          if (!Decimal.TryParse(fields[perCoinPriceIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var perCoinPrice))
          {
            throw new Exception($"CSV line {lineNumber} has non-numeric field {perCoinPriceIndex + 1} \"{fields[perCoinPriceIndex]}\"; expected numeric value such as \"3.17\"");
          }

          if (!Decimal.TryParse(fields[feeIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var fee))
          {
            throw new Exception($"CSV line {lineNumber} has non-numeric field {feeIndex + 1} \"{fields[feeIndex]}\"; expected numeric value such as \"3.17\"");
          }

          var coinType = fields[coinTypeIndex];
          if (!coinType.EndsWith("-USDT"))
          {
            throw new Exception($"CSV line {lineNumber} has unexpected field {coinTypeIndex + 1} \"{fields[coinTypeIndex]}\"; currently only values ending with -USDT are supported such as \"BTC-USDT\"");            
          }
          coinType = coinType.Substring(0, coinType.Length - "-USDT".Length);
          
          if (fields[tradeTypeIndex] != "market" && fields[tradeTypeIndex] != "limit")
          {
            throw new Exception($"CSV line {lineNumber} has unexpected field {tradeTypeIndex + 1} \"{fields[tradeTypeIndex]}\"; currently only \"market\" or \"limit\" is supported");
          }
          
          if (fields[feeCurrencyIndex] != "USDT")
          {
            throw new Exception($"CSV line {lineNumber} has unexpected field {feeCurrencyIndex + 1} \"{fields[feeCurrencyIndex]}\"; currently only USDT is supported");
          }
          
          if (!Decimal.TryParse(fields[dealFundsIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var dealFunds))
          {
            throw new Exception($"CSV line {lineNumber} has non-numeric field {dealFundsIndex + 1} \"{fields[dealFundsIndex]}\"; expected numeric value such as \"3.17\"");
          }
          
          // kucoin reporting is weird because total price is not strictly reported
          // for "sell" transactions, total price = dealFunds, and that minus fee is added to your value
          // for "buy" transactions, total price = dealFunds + fee
          var totalCost = transactionType == TransactionType.Sell ? dealFunds : dealFunds + fee;
          
          // kucoin reports postive values for both buys and sells
          // (but CryptoProfiteer is built assuming negative values for buys, like coinbase reports)
          if (transactionType == TransactionType.Buy) totalCost = -Math.Abs(totalCost);

          var transaction = new PersistedTransaction
          {
            TradeId = fields[tradeIdIndex],
            TransactionType = transactionType,
            Exchange = CryptoExchange.Kucoin,
            Time = createdAtTime,
            CoinType = coinType,
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
        _dataService.ImportTransactions(transactions);
      }

      return Ok();
    }
    
    public class CreateTaxAssociationInputs
    {
      public string SaleOrderId { get; set; }
      public Purchase[] Purchases { get; set; }
      
      public class Purchase
      {
        public string OrderId { get; set; }
        public Decimal ContributingCoinCount { get; set; }
        public int ContributingCost { get; set; }
      }
    }
    
    [HttpPost("createTaxAssociation")]
    public IActionResult CreateTaxAssociation([FromBody]CreateTaxAssociationInputs inputs)
    {
      var taxAssociationId = _dataService.UpdateTaxAssociation(null,
        inputs.SaleOrderId,
        inputs.Purchases.Select(p => 
        (
          p.OrderId,
          p.ContributingCoinCount,
          p.ContributingCost
        )).ToArray());

      return Ok(new { taxAssociationId });
    }
    
    public class DeleteTaxAssociationInputs
    {
      public string TaxAssociationId { get; set; }
    }
    
    [HttpPost("deleteTaxAssociation")]
    public IActionResult DeleteTaxAssociation([FromBody]DeleteTaxAssociationInputs inputs)
    {
      _dataService.DeleteTaxAssociation(inputs.TaxAssociationId);
      return Ok();
    }
  }
}
