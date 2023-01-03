using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace CryptoProfiteer
{
  public class PersistenceService : BackgroundService
  {
    private readonly ILogger<PersistenceService> _logger;
    private readonly IDataService _dataService;
    private readonly IHistoricalCoinPriceService _historicalCoinPriceService;
    private readonly string _dataFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data.json");
    private string _lastSavedData;

    public PersistenceService(ILogger<PersistenceService> logger, IDataService dataService,
      IHistoricalCoinPriceService historicalCoinPriceService)
    {
      _logger = logger;
      _dataService = dataService;
      _historicalCoinPriceService = historicalCoinPriceService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      var newData = PersistenceData.LoadFrom(_dataFilePath);
      _dataService.ImportTransactions(newData.Transactions);
      _dataService.ImportTaxAssociations(newData.TaxAssociations);
      _historicalCoinPriceService.ImportPersistedData(newData.HistoricalCoinPrices);

      // update _lastSavedData from loaded data
      TrySave(noTouchieFileSystem: true, (newData.Transactions, newData.TaxAssociations), newData.HistoricalCoinPrices);

      // NOTE: other services don't start until this method awaits
      // and that is leveraged to 
      // 1.) defeat race condition of missing _dataService changes
      // 2.) make the application not start if it fails to load
      await Task.Yield();
      
      try
      {
        while (!stoppingToken.IsCancellationRequested)
        {
          TrySave(noTouchieFileSystem: false, _dataService.ClonePersistedData(), _historicalCoinPriceService.ClonePersistedData());

          await Task.Delay(1000, stoppingToken);
        }
      }
      finally
      {
        TrySave(noTouchieFileSystem: false, _dataService.ClonePersistedData(), _historicalCoinPriceService.ClonePersistedData());
      }
    }

    private void TrySave(bool noTouchieFileSystem, 
      (IEnumerable<PersistedTransaction> Transactions, IEnumerable<PersistedTaxAssociation> TaxAssociations) dataToSave,
      IEnumerable<PersistedHistoricalCoinPrice> historicalCoinPrices)
    {
      var toSave = new PersistenceData
      {
        Transactions = dataToSave.Transactions
          .OrderBy(x => x.Time)
          .ThenBy(x => x.Id)
          .ToList(),
          
        TaxAssociations = dataToSave.TaxAssociations
          .OrderBy(x => x.Id)
          .ToList(),
          
        HistoricalCoinPrices = historicalCoinPrices
          .OrderBy(x => x.Time)
          .ThenBy(x => x.CoinType)
          .ToList()
      };
      
      // TODO: I'd really love to NOT be serializing all of my system state every second
      // just to see if it needs to be saved... but this works...
      var newData = JsonConvert.SerializeObject(toSave);
      if (newData == _lastSavedData) return;
      
      _lastSavedData = newData;
      
      if (noTouchieFileSystem) return;

      _logger.LogInformation("Saving " + _dataFilePath);
      try
      {
        File.WriteAllLines(_dataFilePath, new[]{ toSave.FirstLine, newData });
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Failed to save " + _dataFilePath);
      }
    }
  }
}