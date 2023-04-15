using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http.Headers;
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
      const string coinbaseProAccountStatementHeaderLine = "portfolio,type,time,amount,balance,amount/balance unit,transfer id,trade id,order id";
      const string kucoinTradesHeaderLine = "tradeCreatedAt,orderId,symbol,side,price,size,funds,fee,liquidity,feeCurrency,orderType,";
      const string kucoinOrdersHeaderLine = "orderCreatedAt,id,clientOid,symbol,side,type,stopPrice,price,size,dealSize,dealFunds,averagePrice,fee,feeCurrency,remark,tags,orderStatus,";
      const string kucoinCompletedTradesHeaderLine = "UID,Account Type,Order ID,Symbol,Side,Order Type,Avg. Filled Price,Filled Amount,Filled Volume,Filled Volume (USDT),Filled Time(UTC+08:00),Fee,Maker/Taker,Fee Currency";
      const string bittrexOrderHistoryHeaderLine = "Uuid,Exchange,TimeStamp,OrderType,Limit,Quantity,QuantityRemaining,Commission,Price,PricePerUnit,IsConditional,Condition,ConditionTarget,ImmediateOrCancel,Closed,TimeInForceTypeId,TimeInForce";
      const string coinbaseOrdersHeaderLine = "Timestamp,Transaction Type,Asset,Quantity Transacted,Spot Price Currency,Spot Price at Transaction,Subtotal,Total (inclusive of fees),Fees,Notes";
      const string decentralizedFillsHeaderLine = "id,date,transaction type,sent total,sent fee,sent coin type,received amount,received coin type,notes";
      var headerFields = Csv.Parse(lines[0]).Select(x => x.Trim()).ToList();
      if (headerFields.SequenceEqual(Csv.Parse(coinbaseProFillsHeaderLine)))
      {
        PostCoinbaseProFillsCsv(lines);
      }
      else if (headerFields.SequenceEqual(Csv.Parse(coinbaseProAccountStatementHeaderLine)))
      {
        PostCoinbaseProAccountStatementCsv(lines);
      }
      else if (headerFields.SequenceEqual(Csv.Parse(kucoinTradesHeaderLine)))
      {
        PostKucoinTradesCsv(lines);
      }
      else if (headerFields.SequenceEqual(Csv.Parse(kucoinOrdersHeaderLine)))
      {
        throw new Exception("Invalid CSV header - looks like a Kucoin Orders export, but this software only accepts Kucoin Trades exports");
      }
      else if (headerFields.SequenceEqual(Csv.Parse(kucoinCompletedTradesHeaderLine)))
      {
        PostKucoinCompletedTradesCsv(lines);
      }
      else if (headerFields.SequenceEqual(Csv.Parse(bittrexOrderHistoryHeaderLine)))
      {
        PostBittrexOrderHistoryCsv(lines);
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
              "Kucoin trades CSV (pre 2023): " + kucoinTradesHeaderLine,
              "Kucoin completed trades XLSM saved as CSV: " + kucoinCompletedTradesHeaderLine,
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
        
        if (!Enum.TryParse<TransactionType_v04>(fields[buySellIndex], ignoreCase: true, out var transactionType) ||
            (transactionType != TransactionType_v04.Buy && transactionType != TransactionType_v04.Sell))
        {
          throw new Exception($"CSV line {lineNumber} has unrecognized field {buySellIndex + 1} \"{fields[buySellIndex]}\"; expected one of " + string.Join(",", Enum.GetNames(typeof(TransactionType_v04)).Select(x => "\"" + x + "\"")));
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

        var transaction = new PersistedTransaction_v04
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
        transactions.Add(transaction.ToLatest());
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

        if (!Enum.TryParse<TransactionType_v04>(fields[buySellIndex], ignoreCase: true, out var transactionType) ||
            (transactionType != TransactionType_v04.Buy && transactionType != TransactionType_v04.Sell))
        {
          throw new Exception($"CSV line {lineNumber} has unrecognized field {buySellIndex + 1} \"{fields[buySellIndex]}\"; expected one of " + string.Join(",", Enum.GetNames(typeof(TransactionType_v04)).Select(x => "\"" + x + "\"")));
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

        var transaction = new PersistedTransaction_v04
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
        transactions.Add(transaction.ToLatest());
      }

      if (transactions.Count > 0)
      {
        _dataService.ImportTransactions(transactions);
      }
    }
    
    private void PostCoinbaseProAccountStatementCsv(List<string> lines)
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

      var typeIndex = IndexOrBust("type");
      var tradeIdIndex = IndexOrBust("trade id");
      var orderIdIndex = IndexOrBust("order id");
      var createdAtIndex = IndexOrBust("time");
      var coinCountIndex = IndexOrBust("amount");
      var coinTypeIndex = IndexOrBust("amount/balance unit");
      
      var conversions = new Dictionary<DateTime, (Decimal CoinCount, string CoinType)>();
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

        var coinType = fields[coinTypeIndex];
        var transactionType = fields[typeIndex];
        var tradeId = fields[tradeIdIndex];
        var orderId = fields[orderIdIndex];

        if (transactionType == "match")
        {
          if (!_dataService.Transactions.TryGetValue("CP-" + tradeId, out var transaction))
          {
            throw new Exception($"CSV line {lineNumber} has field {tradeIdIndex + 1} trade id \"{tradeId}\" referring to unknown transaction. You need to import the coinbase pro fills for this transaction first.");
          }

          var revisedTransaction = transaction.ClonePersistedData();
          revisedTransaction.OrderAggregationId = orderId;
          transactions.Add(revisedTransaction);
        }
        else if (transactionType == "conversion")
        {
          // conversions occur in pairs of two lines - the subtracted amount and the added amount
          if (conversions.TryGetValue(createdAtTime, out var firstConversion))
          {
            var newTransaction = new PersistedTransaction
            {
              Id = "CPC-" + createdAtTime.ToString("o"),
              TransactionType = TransactionType.Trade,
              Exchange = CryptoExchange.CoinbasePro,
              Time = createdAtTime,
              ReceivedCoinType = firstConversion.CoinCount > 0 ? firstConversion.CoinType : coinType,
              PaymentCoinType = firstConversion.CoinCount > 0 ? coinType : firstConversion.CoinType,
              ReceivedCoinCount = firstConversion.CoinCount > 0 ? firstConversion.CoinCount : coinCount,
              PaymentCoinCount = firstConversion.CoinCount > 0 ? coinCount : firstConversion.CoinCount,
            };
            newTransaction.ListPrice = Math.Abs(newTransaction.ReceivedCoinCount / newTransaction.PaymentCoinCount);
            transactions.Add(newTransaction);
            conversions.Remove(createdAtTime);
          }
          else
          {
            conversions.Add(createdAtTime, (CoinCount: coinCount, CoinType: coinType));
          }
        }
        // else it's a fee or deposit or withdrawal - stuff we don't care about yet
      }

      if (transactions.Count > 0)
      {
        _dataService.ImportTransactions(transactions);
      }
    }
    
    private void PostKucoinTradesCsv(List<string> lines)
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
      // they have fill IDs but not order IDs. So this code assumes that all the fills for a single order
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

          if (!Enum.TryParse<TransactionType_v04>(fields[buySellIndex], ignoreCase: true, out var transactionType) ||
              (transactionType != TransactionType_v04.Buy && transactionType != TransactionType_v04.Sell))
          {
            throw new Exception($"CSV line {lineNumber} has unrecognized field {buySellIndex + 1} \"{fields[buySellIndex]}\"; expected one of " + string.Join(",", Enum.GetNames(typeof(TransactionType_v04)).Select(x => "\"" + x + "\"")));
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
          var totalCost = transactionType == TransactionType_v04.Sell ? dealFunds : dealFunds + fee;
          
          // kucoin reports postive values for both buys and sells
          // (but CryptoProfiteer is built assuming negative values for buys, like coinbase reports)
          if (transactionType == TransactionType_v04.Buy) totalCost = -Math.Abs(totalCost);

          var transaction = new PersistedTransaction_v04
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
          transactions.Add(transaction.ToLatest());
        }
      }

      if (transactions.Count > 0)
      {
        _dataService.ImportTransactions(transactions);
      }
    }
    
    private void PostKucoinCompletedTradesCsv(List<string> lines)
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

      var orderIdIndex = IndexOrBust("Order ID");
      var buySellIndex = IndexOrBust("Side");
      var createdAtIndex = IndexOrBust("Filled Time(UTC+08:00)");
      var coinCountIndex = IndexOrBust("Filled Amount");
      var perCoinPriceIndex = IndexOrBust("Avg. Filled Price");
      var feeIndex = IndexOrBust("Fee");
      var productIndex = IndexOrBust("Symbol");
      var tradeTypeIndex = IndexOrBust("Order Type");
      var feeCurrencyIndex = IndexOrBust("Fee Currency");
      var filledVolumeIndex = IndexOrBust("Filled Volume");
      
      var transactions = new List<PersistedTransaction>();
      int lineNumber = 1;
      var fillNumbers = new Dictionary<string, int>();
      foreach (var line in lines.Skip(1))
      {
        lineNumber++;
        var fields = Csv.Parse(line).Select(x => x.Trim()).ToList();
        if (fields.Count == 0) continue;
        if (fields.Count != headerFields.Count)
        {
          throw new Exception($"CSV line {lineNumber} has {fields.Count} fields; different from header line which has {headerFields.Count} fields; aborting.");
        }

        if (!Enum.TryParse<TransactionType_v04>(fields[buySellIndex], ignoreCase: true, out var transactionType) ||
            (transactionType != TransactionType_v04.Buy && transactionType != TransactionType_v04.Sell))
        {
          throw new Exception($"CSV line {lineNumber} has unrecognized field {buySellIndex + 1} \"{fields[buySellIndex]}\"; expected one of " + string.Join(",", Enum.GetNames(typeof(TransactionType_v04)).Select(x => "\"" + x + "\"")));
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
        // NOTE: technically this new report form declares it's timezone, but I just
        // keeping downloading them in UTC+8 so I don't have to change this code
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
        
        if (fields[tradeTypeIndex] != "MARKET" && fields[tradeTypeIndex] != "LIMIT")
        {
          throw new Exception($"CSV line {lineNumber} has unexpected field {tradeTypeIndex + 1} \"{fields[tradeTypeIndex]}\"; currently only \"MARKET\" or \"LIMIT\" is supported");
        }

        if (!Decimal.TryParse(fields[filledVolumeIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var filledVolume))
        {
          throw new Exception($"CSV line {lineNumber} has non-numeric field {filledVolumeIndex + 1} \"{fields[filledVolumeIndex]}\"; expected numeric value such as \"3.17\"");
        }
        
        // kucoin reporting is weird because total price is not strictly reported
        // for "sell" transactions, total price = filledVolume, and that minus fee is added to your value
        // for "buy" transactions, total price = filledVolume + fee
        var totalCost = transactionType == TransactionType_v04.Sell ? filledVolume : filledVolume + fee;
        
        // kucoin reports postive values for both buys and sells
        // (but CryptoProfiteer is built assuming negative values for buys, like coinbase reports)
        if (transactionType == TransactionType_v04.Buy) totalCost = -Math.Abs(totalCost);
        
        // kucoin "Completed Trades" reports don't show the fill IDs
        // so assume all the fills for a given trade are present in the file being uploaded
        var orderId = fields[orderIdIndex];
        var fillNumber = fillNumbers.GetValueOrDefault(orderId, 1);
        fillNumbers[orderId] = fillNumber + 1;

        var transaction = new PersistedTransaction_v04
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
        transactions.Add(transaction.ToLatest());
      }

      if (transactions.Count > 0)
      {
        _dataService.ImportTransactions(transactions);
      }
    }
    
    private void PostBittrexOrderHistoryCsv(List<string> lines)
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

      var orderIdIndex = IndexOrBust("Uuid");
      var buySellIndex = IndexOrBust("OrderType");
      var createdAtIndex = IndexOrBust("TimeStamp");
      var coinCountIndex = IndexOrBust("Quantity");
      var quantityRemainingIndex = IndexOrBust("QuantityRemaining");
      var isConditionalIndex = IndexOrBust("IsConditional");
      var perCoinPriceIndex = IndexOrBust("PricePerUnit");
      var feeIndex = IndexOrBust("Commission");
      var productIndex = IndexOrBust("Exchange");
      var filledVolumeIndex = IndexOrBust("Price");
      
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

        TransactionType_v04 transactionType = fields[buySellIndex] switch
        {
          "MARKET_SELL" => TransactionType_v04.Sell,
          "CEILING_MARKET_BUY" => TransactionType_v04.Buy,
          _ => throw new Exception($"CSV line {lineNumber} has unrecognized field {buySellIndex + 1} \"{fields[buySellIndex]}\"; expected one of MARKET_SELL, CEILING_MARKET_BUY")
        };

        // bittrex times appear to be recorded in UTC? or maybe Pacific? I don't know yet; haven't done enough trades to find out.
        if (!DateTime.TryParseExact(fields[createdAtIndex], "M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var createdAtTime))
        {
          throw new Exception($"CSV line {lineNumber} has non-date/time field {createdAtIndex + 1} \"{fields[createdAtIndex]}\"; expected date/time such as \"{DateTime.Now.ToString("M/d/yyyy h:mm:ss tt")}\"");
        }
        if (createdAtTime.Kind != DateTimeKind.Utc)
        {
          throw new Exception($"CSV line {lineNumber} date/time field was incorrectly interpreted as {createdAtTime.Kind}");
        }

        if (!Decimal.TryParse(fields[coinCountIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var coinCount))
        {
          throw new Exception($"CSV line {lineNumber} has non-numeric field {coinCountIndex + 1} \"{fields[coinCountIndex]}\"; expected numeric value such as \"3.17\"");
        }
        
        if (fields[quantityRemainingIndex] != "0.00000000")
        {
          throw new Exception($"CSV line {lineNumber} has field {coinCountIndex + 1} \"{fields[quantityRemainingIndex]}\" but I don't know what means yet... I've only seen 0.00000000; please confirm how this code needs to be updated");
        }

        if (!Decimal.TryParse(fields[perCoinPriceIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var perCoinPrice))
        {
          throw new Exception($"CSV line {lineNumber} has non-numeric field {perCoinPriceIndex + 1} \"{fields[perCoinPriceIndex]}\"; expected numeric value such as \"3.17\"");
        }

        if (!Decimal.TryParse(fields[feeIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var fee))
        {
          throw new Exception($"CSV line {lineNumber} has non-numeric field {feeIndex + 1} \"{fields[feeIndex]}\"; expected numeric value such as \"3.17\"");
        }

        var product = fields[productIndex];
        var productParts = product.Split('-');
        if (productParts.Length != 2)
        {
          throw new Exception($"CSV line {lineNumber} has unexpected field {productIndex + 1} \"{fields[productIndex]}\"; a value with a single hyphen is expected such as \"BTC-USD\"");
        }
        var coinType = productParts[1];
        var paymentCoinType = productParts[0];

        if (!Decimal.TryParse(fields[filledVolumeIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var filledVolume))
        {
          throw new Exception($"CSV line {lineNumber} has non-numeric field {filledVolumeIndex + 1} \"{fields[filledVolumeIndex]}\"; expected numeric value such as \"3.17\"");
        }
        
        // bittrex reporting...
        // for "sell" transactions, total price = filledVolume, and that minus fee is added to your value
        // for "buy" transactions, total price = filledVolume + fee
        var totalCost = transactionType == TransactionType_v04.Sell ? filledVolume : filledVolume + fee;
        
        // bittrex reports postive values for both buys and sells
        // (but CryptoProfiteer is built assuming negative values for buys, like coinbase reports)
        if (transactionType == TransactionType_v04.Buy) totalCost = -Math.Abs(totalCost);
        
        // bittrex doesn't show fills; every "transaction" is also an "order"
        var orderId = fields[orderIdIndex];
        var transaction = new PersistedTransaction_v04
        {
          TradeId = "B-" + orderId,
          OrderAggregationId = "B-" + orderId,
          TransactionType = transactionType,
          Exchange = CryptoExchange.Bittrex,
          Time = createdAtTime,
          CoinType = coinType,
          CoinCount = coinCount,
          PerCoinCost = perCoinPrice,
          Fee = fee,
          TotalCost = totalCost,
          PaymentCoinType = paymentCoinType
        };
        transactions.Add(transaction.ToLatest());
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

        if (!Enum.TryParse<TransactionType_v04>(fields[transactionTypeIndex], ignoreCase: true, out var transactionType) ||
            (transactionType != TransactionType_v04.Buy && transactionType != TransactionType_v04.Sell))
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

        var transaction = new PersistedTransaction_v04
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
        transactions.Add(transaction.ToLatest());
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
          Decimal.Parse(p.ContributingCoinCount, NumberStyles.Float, CultureInfo.InvariantCulture)
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
      public Dictionary<string, string> BotArgs { get; set; }
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
        botArgs: inputs.BotArgs,
        stoppingToken: HttpContext.RequestAborted
      );
      return new ProveBotOutputs { Result = result };
    }
    
    [HttpGet("FreeTaxUsaScript/{year}")]
    public FileStreamResult DownloadFreeTaxUsaScript(int year)
    {
      StringBuilder builder = new StringBuilder();
      
      const string scriptHeader = @"
      
Add()

Esc::
  Suspend, Off
  Pause, Off, 1
  If (toggle := !toggle) {
    Suspend, On
    Pause, On, 1
  }
  return

Add()
{

MsgBox, Make sure you're at the ""What type of investment did you sell?"" page, and press ""Escape"" to pause if needed
CoordMode, Mouse, Client
";

      const string scriptTrailer = @"
}
";

      const string scriptFormat = @"

if WinExist(""FreeTaxUSA"")
{{
    WinActivate ; Use the window found by WinExist.
    ;WinActivate, ""FreeTaxUSA""
}}
else 
{{
    MsgBox, can't find FreeTaxUSA window
    return
}}

; ""it's a crypto"" button
Click, 847 483
Sleep, 1500

; ""save and continue"" 
Click, 1156 666
Sleep, 2500

; ""both"" (Nathan and Rachel) 
Click, 438 517
Sleep, 1500

; ""save and continue"" 
Click, 1171 609
Sleep, 2500

; ""one at a time"" 
Click, 742 530
Sleep, 1500

; ""save and continue""
Click, 1173 741
Sleep, 2500

; description textbox 
Click, 900 475
Sleep, 1500
Send, {0}

; date acquired box  (month/day/year)
Click, 902 663
Sleep, 1500
Send, {1}

; date sold (just month/day)
Click, 891 744
Sleep, 1500
Send, {2}

; sale proceeds 
Click, 946 828
Sleep, 1500
Send, {3}

; cost basis
Click, 938 927
Sleep, 1500
Send, {4}

; pagedown a few times
Send, {{PgDn}}
Send, {{PgDn}}
Sleep, 1500

; ""not reported on 1099-B"" 
Click, 727 605
Sleep, 1500

; ""save and continue"" 
Click, 1170 885
Sleep, 2500

; ""save and continue"" 
Click, 1180 634
Sleep, 5000

; press end key
Send, {{End}}
Sleep, 3000

; ""add another"" button 
Click, 528 702
Sleep, 2500

      ";

      builder.AppendLine(scriptHeader);
      int i = 1;
      foreach (var taxAssociation in _dataService.TaxAssociations.Values.Where(x => x.Time.Year == year).OrderBy(x => x.Time))
      {
        foreach (var purchase in taxAssociation.Purchases.OrderBy(x => x.Order.Time))
        {
          var description = $"{i++}: {purchase.ContributingCoinCount.FormatMinDecimals()} {taxAssociation.CoinType}";
          var dateAcquired = purchase.Order.Time.ToLocalTime().ToString("MM/dd/yyyy");
          var dateSold = taxAssociation.Time.ToLocalTime().ToString("MM/dd");
          
          double percent = (double)purchase.ContributingCoinCount / (double)taxAssociation.Sale.Order.PaymentCoinCount;
          double amount = (double)taxAssociation.Sale.Order.ReceivedValueUsd.Value * percent;
          int saleProceeds = (int)Math.Round(amount, MidpointRounding.AwayFromZero);

          var costBasis = purchase.ContributingCost.Value;
          
          builder.AppendLine(string.Format(scriptFormat, description, dateAcquired, dateSold, saleProceeds, costBasis));
        }
      }
      builder.AppendLine(scriptTrailer);
      
      var memory = new MemoryStream(Encoding.UTF8.GetBytes(builder.ToString()));
      return new FileStreamResult(memory, "text/plain")
      {
        FileDownloadName = $"{year} FreeTaxUSA data entry script.ahk",
      };
    }
  }
}
