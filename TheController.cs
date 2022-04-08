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
using CryptoProfiteer.TradeBots;

namespace CryptoProfiteer
{
  [ApiController]
  [Route("api")]
  public class TheController : ControllerBase
  {
    private readonly IDataService _dataService;
    private readonly IBotProvingService _botProvingService;
    private readonly ILogger<TheController> _logger;

    public TheController(IDataService dataService, IBotProvingService botProvingService, ILogger<TheController> logger)
    {
      _dataService = dataService;
      _botProvingService = botProvingService;
      _logger = logger;
    }

    [HttpPost("uploadCsv")]
    public async Task<IActionResult> UploadCsv(IFormFile file)
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

      while (string.IsNullOrEmpty(lines.FirstOrDefault())) lines.RemoveAt(0);
      if (lines.Count == 0)
      {
        return Ok();
      }
      
      const string coinbaseProFillsHeaderLine = "portfolio,trade id,product,side,created at,size,size unit,price,fee,total,price/fee/total unit";
      const string kucoinTradesHeaderLine = "tradeCreatedAt,orderId,symbol,side,price,size,funds,fee,liquidity,feeCurrency,orderType,";
      const string kucoinOrdersHeaderLine = "orderCreatedAt,id,clientOid,symbol,side,type,stopPrice,price,size,dealSize,dealFunds,averagePrice,fee,feeCurrency,remark,tags,orderStatus,";
      const string coinbaseOrdersHeaderLine = "Timestamp,Transaction Type,Asset,Quantity Transacted,Spot Price Currency,Spot Price at Transaction,Subtotal,Total (inclusive of fees),Fees,Notes";
      const string decentralizedFillsHeaderLine = "id,date,transaction type,sent total,sent fee,sent coin type,received amount,received coin type,notes";
      var headerFields = Csv.Parse(lines[0]).Select(x => x.Trim()).ToList();
      if (headerFields.SequenceEqual(Csv.Parse(coinbaseProFillsHeaderLine)))
      {
        PostCoinbaseProFillsCsv(lines);
      }
      else if (headerFields.SequenceEqual(Csv.Parse(kucoinTradesHeaderLine)))
      {
        PostKucoinTradesCsv(lines);
      }
      else if (headerFields.SequenceEqual(Csv.Parse(kucoinOrdersHeaderLine)))
      {
        throw new Exception("Invalid CSV header - looks like a Kucoin Orders export, but this software only accepts Kucoin Trades exports");
      }
      else if (headerFields.SequenceEqual(Csv.Parse(decentralizedFillsHeaderLine)))
      {
        PostDecentralizedFillsCsv(lines);
      }
      else
      {
        // coinbase order files have some junk at the top, so gotta skip that and find the header line
        var coinbaseHeaders = Csv.Parse(coinbaseOrdersHeaderLine);
        int lineNumberOffset = 0;
        while (lines.Count > 0 && !Csv.Parse(lines[0]).Select(x => x.Trim()).SequenceEqual(coinbaseHeaders))
        {
          lineNumberOffset++;
          lines.RemoveAt(0);
        }
        if (lines.Count > 0 && Csv.Parse(lines[0]).Select(x => x.Trim()).SequenceEqual(coinbaseHeaders))
        {
          PostCoinbaseOrdersCsv(lines, lineNumberOffset);
        }
        else
        {
          throw new Exception("Unable to determine csv format from csv file header line; expected one of the following:" +
            string.Join(Environment.NewLine, new[]
            {
              "",
              "Coinbase orders CSV: " + coinbaseOrdersHeaderLine,
              "Coinbase Pro fills CSV: " + coinbaseProFillsHeaderLine,
              "Kucoin trades CSV: " + kucoinTradesHeaderLine,
              "Decentralized fills CSV: " + decentralizedFillsHeaderLine,
            }));
        }
      }
      
      return Ok();
    }
    
    private void PostCoinbaseOrdersCsv(List<string> lines, int lineNumberOffset)
    {
      var headerFields = Csv.Parse(lines[0]).Select(x => x.Trim()).ToList();

      int IndexOrBust(string name)
      {
        int index = headerFields.IndexOf(name);
        if (index < 0)
        {
          throw new Exception("CSV header lacks required '" + name + "' field");
        }
        return index;
      }

      var notesIndex = IndexOrBust("Notes");
      var buySellIndex = IndexOrBust("Transaction Type");
      var createdAtIndex = IndexOrBust("Timestamp");
      var coinCountIndex = IndexOrBust("Quantity Transacted");
      var coinTypeIndex = IndexOrBust("Asset");
      var perCoinPriceIndex = IndexOrBust("Spot Price at Transaction");
      var feeIndex = IndexOrBust("Fees");
      var totalCostIndex = IndexOrBust("Total (inclusive of fees)");
      var paymentTypeIndex = IndexOrBust("Spot Price Currency");
      
      var transactions = new List<PersistedTransaction>();
      int lineNumber = 1 + lineNumberOffset;
      foreach (var line in lines.Skip(1))
      {
        lineNumber++;
        var fields = Csv.Parse(line).Select(x => x.Trim()).ToList();
        if (fields.Count == 0) continue;
        if (fields.Count != headerFields.Count)
        {
          throw new Exception($"CSV line {lineNumber} has {fields.Count} fields; different from header line which has {headerFields.Count} fields; aborting.");
        }
        
        if (fields[buySellIndex] == "Receive" || fields[buySellIndex] == "Send")
        {
          continue;
        }
        
        if (!Enum.TryParse<TransactionType>(fields[buySellIndex], ignoreCase: true, out var transactionType) ||
            (transactionType != TransactionType.Buy && transactionType != TransactionType.Sell))
        {
          throw new Exception($"CSV line {lineNumber} has unrecognized field {buySellIndex + 1} \"{fields[buySellIndex]}\"; expected one of " + string.Join(",", Enum.GetNames(typeof(TransactionType)).Select(x => "\"" + x + "\"")));
        }

        if (!DateTime.TryParse(fields[createdAtIndex], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var createdAtTime))
        {
          throw new Exception($"CSV line {lineNumber} has non-date/time field {createdAtIndex + 1} \"{fields[createdAtIndex]}\"; expected date/time such as \"{DateTime.Now.ToString("o")}\"");
        }
        if (createdAtTime.Kind != DateTimeKind.Utc)
        {
          throw new Exception($"CSV line {lineNumber} date/time field was incorrectly interpreted as {createdAtTime.Kind}");
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
        
        var coinType = fields[coinTypeIndex];
        var paymentCoinType = fields[paymentTypeIndex];

        var transaction = new PersistedTransaction
        {
          TradeId = "C-" + fields[createdAtIndex], // assuming that my coinbase transactions are all time-unique
          TransactionType = transactionType,
          Exchange = CryptoExchange.Coinbase,
          Time = createdAtTime,
          CoinType = fields[coinTypeIndex],
          CoinCount = coinCount,
          PerCoinCost = perCoinPrice,
          Fee = fee,
          TotalCost = totalCost,
          PaymentCoinType = paymentCoinType,
        };
        transactions.Add(transaction);
      }

      if (transactions.Count > 0)
      {
        _dataService.ImportTransactions(transactions);
      }
    }

    private void PostCoinbaseProFillsCsv(List<string> lines)
    {
      var headerFields = Csv.Parse(lines[0]).Select(x => x.Trim()).ToList();

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
      
      var transactions = new List<PersistedTransaction>();
      int lineNumber = 1;
      foreach (var line in lines.Skip(1))
      {
        lineNumber++;
        var fields = Csv.Parse(line).Select(x => x.Trim()).ToList();
        if (fields.Count == 0) continue;
        if (fields.Count != headerFields.Count)
        {
          throw new Exception($"CSV line {lineNumber} has {fields.Count} fields; different from header line which has {headerFields.Count} fields; aborting.");
        }

        if (!Enum.TryParse<TransactionType>(fields[buySellIndex], ignoreCase: true, out var transactionType) ||
            (transactionType != TransactionType.Buy && transactionType != TransactionType.Sell))
        {
          throw new Exception($"CSV line {lineNumber} has unrecognized field {buySellIndex + 1} \"{fields[buySellIndex]}\"; expected one of " + string.Join(",", Enum.GetNames(typeof(TransactionType)).Select(x => "\"" + x + "\"")));
        }

        if (!DateTime.TryParse(fields[createdAtIndex], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var createdAtTime))
        {
          throw new Exception($"CSV line {lineNumber} has non-date/time field {createdAtIndex + 1} \"{fields[createdAtIndex]}\"; expected date/time such as \"{DateTime.Now.ToString("o")}\"");
        }
        if (createdAtTime.Kind != DateTimeKind.Utc)
        {
          throw new Exception($"CSV line {lineNumber} date/time field was incorrectly interpreted as {createdAtTime.Kind}");
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
        
        var coinType = fields[coinTypeIndex];
        var paymentCoinType = fields[paymentTypeIndex];
        var expectedProduct = $"{coinType}-{paymentCoinType}";
        if (fields[product] != expectedProduct)
        {
          throw new Exception($"CSV line {lineNumber} has unexpected field {product + 1} \"{fields[product]}\"; based on other fields, expected \"{expectedProduct}\"");
        }

        var transaction = new PersistedTransaction
        {
          TradeId = "CP-" + fields[tradeIdIndex],
          TransactionType = transactionType,
          Exchange = CryptoExchange.CoinbasePro,
          Time = createdAtTime,
          CoinType = fields[coinTypeIndex],
          CoinCount = coinCount,
          PerCoinCost = perCoinPrice,
          Fee = fee,
          TotalCost = totalCost,
          PaymentCoinType = paymentCoinType,
        };
        transactions.Add(transaction);
      }

      if (transactions.Count > 0)
      {
        _dataService.ImportTransactions(transactions);
      }
    }
    
    public void PostKucoinTradesCsv(List<string> lines)
    {
      var headerFields = Csv.Parse(lines[0]).Select(x => x.Trim()).ToList();

      int IndexOrBust(string name)
      {
        int index = headerFields.IndexOf(name);
        if (index < 0)
        {
          throw new Exception("CSV header lacks required '" + name + "' field");
        }
        return index;
      }

      var orderIdIndex = IndexOrBust("orderId");
      var buySellIndex = IndexOrBust("side");
      var createdAtIndex = IndexOrBust("tradeCreatedAt");
      var coinCountIndex = IndexOrBust("size");
      var perCoinPriceIndex = IndexOrBust("price");
      var feeIndex = IndexOrBust("fee");
      var productIndex = IndexOrBust("symbol");
      var tradeTypeIndex = IndexOrBust("orderType");
      var feeCurrencyIndex = IndexOrBust("feeCurrency");
      var dealFundsIndex = IndexOrBust("funds");
      
      // Kucoin trade history is really a bummer because individual rows are not uniquely identified;
      // they have OrderID but not fill IDs. So this code assumes that all the fills for a single order
      // are present in the file being uploaded, and gives the fills IDs based on that
      var orderLineNumbers = new Dictionary<string, List<int>>();
      {
        int lineNumber = 1;
        foreach (var line in lines.Skip(1))
        {
          lineNumber++;
          var fields = Csv.Parse(line).Select(x => x.Trim()).ToList();
          if (fields.Count == 0) continue;
          if (fields.Count != headerFields.Count)
          {
            throw new Exception($"CSV line {lineNumber} has {fields.Count} fields; different from header line which has {headerFields.Count} fields; aborting.");
          }
          var orderId = fields[orderIdIndex];
          if (!orderLineNumbers.TryGetValue(orderId, out var bucket))
          {
            bucket = new List<int>();
            orderLineNumbers[orderId] = bucket;
          }
          bucket.Add(lineNumber);
        }
      }

      var transactions = new List<PersistedTransaction>();
      foreach ((var orderId, var lineNumbers) in orderLineNumbers)
      {
        // NOTE: taking a shortcut here. If the order of the transactions differs per upload, then this code
        // will jumble the IDs given to the transactions. But as long as the same set of IDs are ultimately produced
        // then the end result ought to be good enough? I hope? --nathschu
        var fillNumber = 0;
        foreach (var lineNumber in lineNumbers)
        {
          fillNumber++;
          var line = lines[lineNumber - 1];
          var fields = Csv.Parse(line).Select(x => x.Trim()).ToList();
          if (fields.Count == 0) continue;
          if (fields.Count != headerFields.Count)
          {
            throw new Exception($"CSV line {lineNumber} has {fields.Count} fields; different from header line which has {headerFields.Count} fields; aborting.");
          }

          if (!Enum.TryParse<TransactionType>(fields[buySellIndex], ignoreCase: true, out var transactionType) ||
              (transactionType != TransactionType.Buy && transactionType != TransactionType.Sell))
          {
            throw new Exception($"CSV line {lineNumber} has unrecognized field {buySellIndex + 1} \"{fields[buySellIndex]}\"; expected one of " + string.Join(",", Enum.GetNames(typeof(TransactionType)).Select(x => "\"" + x + "\"")));
          }

          if (!DateTime.TryParseExact(fields[createdAtIndex], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var createdAtTime))
          {
            throw new Exception($"CSV line {lineNumber} has non-date/time field {createdAtIndex + 1} \"{fields[createdAtIndex]}\"; expected date/time such as \"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}\"");
          }
          if (createdAtTime.Kind != DateTimeKind.Utc)
          {
            throw new Exception($"CSV line {lineNumber} date/time field was incorrectly interpreted as {createdAtTime.Kind}");
          }
          // Kucoin times are reported 8 hours after UTC, which appears to be the local time in singapore.
          // So subtract 8 hours to compensate
          createdAtTime = createdAtTime - TimeSpan.FromHours(8);

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

          var paymentCoinType = fields[feeCurrencyIndex];
          var product = fields[productIndex];
          var productParts = product.Split('-');
          if (productParts.Length != 2)
          {
            throw new Exception($"CSV line {lineNumber} has unexpected field {productIndex + 1} \"{fields[productIndex]}\"; a value with a single hyphen is expected such as \"BTC-USD\"");
          }
          var coinType = productParts[0];
          if (productParts[1] != paymentCoinType)
          {
            throw new Exception($"CSV line {lineNumber} has unexpected field {productIndex + 1} \"{fields[productIndex]}\"; it didn't match the fee currency \"{paymentCoinType}\"");
          }
          
          if (fields[tradeTypeIndex] != "market" && fields[tradeTypeIndex] != "limit")
          {
            throw new Exception($"CSV line {lineNumber} has unexpected field {tradeTypeIndex + 1} \"{fields[tradeTypeIndex]}\"; currently only \"market\" or \"limit\" is supported");
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
            TradeId = "K-" + orderId + "-" + fillNumber,
            OrderAggregationId = "K-" + orderId,
            TransactionType = transactionType,
            Exchange = CryptoExchange.Kucoin,
            Time = createdAtTime,
            CoinType = coinType,
            CoinCount = coinCount,
            PerCoinCost = perCoinPrice,
            Fee = fee,
            TotalCost = totalCost,
            PaymentCoinType = paymentCoinType
          };
          transactions.Add(transaction);
        }
      }

      if (transactions.Count > 0)
      {
        _dataService.ImportTransactions(transactions);
      }
    }
    
    private void PostDecentralizedFillsCsv(List<string> lines)
    {
      var headerFields = Csv.Parse(lines[0]).Select(x => x.Trim()).ToList();

      int IndexOrBust(string name)
      {
        int index = headerFields.IndexOf(name);
        if (index < 0)
        {
          throw new Exception("CSV header lacks required '" + name + "' field");
        }
        return index;
      }

      var idIndex = IndexOrBust("id");
      var transactionTypeIndex = IndexOrBust("transaction type");
      var dateIndex = IndexOrBust("date");
      var receivedAmountIndex = IndexOrBust("received amount");
      var receivedCoinTypeIndex = IndexOrBust("received coin type");
      var feeIndex = IndexOrBust("sent fee");
      var totalCostIndex = IndexOrBust("sent total");
      var sentCoinTypeIndex = IndexOrBust("sent coin type");

      var transactions = new List<PersistedTransaction>();
      int lineNumber = 1;
      foreach (var line in lines.Skip(1))
      {
        lineNumber++;
        var fields = Csv.Parse(line).Select(x => x.Trim()).ToList();
        if (fields.Count == 0) continue;
        if (fields.Count != headerFields.Count)
        {
          throw new Exception($"CSV line {lineNumber} has {fields.Count} fields; different from header line which has {headerFields.Count} fields; aborting.");
        }

        if (!Enum.TryParse<TransactionType>(fields[transactionTypeIndex], ignoreCase: true, out var transactionType) ||
            (transactionType != TransactionType.Buy && transactionType != TransactionType.Sell))
        {
          throw new Exception($"CSV line {lineNumber} has unrecognized field {transactionTypeIndex + 1} \"{fields[transactionTypeIndex]}\"; expected one of \"buy\", \"sell\"");
        }

        if (!DateTime.TryParse(fields[dateIndex], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
        {
          throw new Exception($"CSV line {lineNumber} has non-date/time field {dateIndex + 1} \"{fields[dateIndex]}\"; expected date/time such as \"{DateTime.Now.ToString("o")}\"");
        }
        if (date.Kind != DateTimeKind.Utc)
        {
          throw new Exception($"CSV line {lineNumber} date/time field was incorrectly interpreted as {date.Kind}");
        }

        if (!Decimal.TryParse(fields[receivedAmountIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var coinCount))
        {
          throw new Exception($"CSV line {lineNumber} has non-numeric field {receivedAmountIndex + 1} \"{fields[receivedAmountIndex]}\"; expected numeric value such as \"3.17\"");
        }
        coinCount = Math.Abs(coinCount);

        if (!Decimal.TryParse(fields[feeIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var fee))
        {
          throw new Exception($"CSV line {lineNumber} has non-numeric field {feeIndex + 1} \"{fields[feeIndex]}\"; expected numeric value such as \"3.17\"");
        }
        fee = Math.Abs(fee);

        if (!Decimal.TryParse(fields[totalCostIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var totalCost))
        {
          throw new Exception($"CSV line {lineNumber} has non-numeric field {totalCostIndex + 1} \"{fields[totalCostIndex]}\"; expected numeric value such as \"3.17\"");
        }
        totalCost = Math.Abs(totalCost);
        
        if (fee > totalCost)
        {
          throw new Exception($"CSV line {lineNumber} has fee (field {feeIndex + 1} \"{fee}\") greater than totalCost (field {totalCostIndex + 1} \"{totalCost}\")");
        }

        Decimal perCoinPrice;
        try { perCoinPrice = (totalCost - fee) / coinCount; } catch { throw new Exception("Unable to determine perCoinPrice for (totalCost - fee) / coinCount at CSV line {lineNumber}"); }
        var coinType = fields[receivedCoinTypeIndex];
        var paymentCoinType = fields[sentCoinTypeIndex];

        var transaction = new PersistedTransaction
        {
          TradeId = "D-" + fields[idIndex],
          TransactionType = transactionType,
          Exchange = CryptoExchange.None,
          Time = date,
          CoinType = coinType,
          CoinCount = coinCount,
          PerCoinCost = perCoinPrice,
          Fee = fee,
          TotalCost = totalCost,
          PaymentCoinType = paymentCoinType,
        };
        transactions.Add(transaction);
      }

      if (transactions.Count > 0)
      {
        _dataService.ImportTransactions(transactions);
      }
    }
    
    public class CreateTaxAssociationInputs
    {
      public string SaleOrderId { get; set; }
      public Purchase[] Purchases { get; set; }
      
      public class Purchase
      {
        public string OrderId { get; set; }
        public string ContributingCoinCount { get; set; }
        public string ContributingCost { get; set; }
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
          Decimal.Parse(p.ContributingCoinCount, NumberStyles.Float, CultureInfo.InvariantCulture),
          int.Parse(p.ContributingCost)
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
    
    public class AddAdjustmentInputs
    {
      public string CoinType { get; set; }
      public string CoinCount { get; set; }
    }
    
    [HttpPost("addAdjustment")]
    public IActionResult AddAdjustment([FromBody]AddAdjustmentInputs inputs)
    {
      var coinCount = Decimal.Parse(inputs.CoinCount, NumberStyles.Float, CultureInfo.InvariantCulture);
      _dataService.AddAdjustment(inputs.CoinType, coinCount);
      return Ok();
    }
    
    public class DeleteAdjustmentInputs
    {
      public string TradeId { get; set; }
    }
    
    [HttpPost("deleteAdjustment")]
    public IActionResult DeleteAdjustment([FromBody]DeleteAdjustmentInputs inputs)
    {
      _dataService.DeleteAdjustment(inputs.TradeId);
      return Ok();
    }
    
    public class ProveBotInputs
    {
      public string StartTime { get; set; }
      public string EndTime { get; set; }
      public string BotName { get; set; }
      public string Granularity { get; set; }
      public string InitialUsd { get; set; }
      public string CoinType { get; set; }
    }
    
    public class ProveBotOutputs
    {
      public BotProofResult Result { get; set; }
    }
    
    [HttpPost("proveBot")]
    public async Task<ActionResult<ProveBotOutputs>> ProveBot([FromBody]ProveBotInputs inputs)
    {
      var result = await _botProvingService.Prove(
        botName: inputs.BotName,
        coinType: inputs.CoinType,
        initialUsd: Decimal.Parse(inputs.InitialUsd, NumberStyles.Float, CultureInfo.InvariantCulture),
        startTime: DateTime.Parse(inputs.StartTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
        endTime: DateTime.Parse(inputs.EndTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
        granularity: Enum.Parse<CandleGranularity>(inputs.Granularity, ignoreCase: true),
        stoppingToken: HttpContext.RequestAborted
      );
      return new ProveBotOutputs { Result = result };
    }
  }
}
