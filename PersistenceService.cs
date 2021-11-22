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
    private readonly string _dataFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data.json");
    private string _lastSavedData;

    public PersistenceService(ILogger<PersistenceService> logger, IDataService dataService)
    {
      _logger = logger;
      _dataService = dataService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      var newData = PersistenceData.LoadFrom(_dataFilePath);
      _dataService.ImportTransactions(newData.Transactions);

      // update _lastSavedData from loaded data
      TrySave(noTouchieFileSystem: true, newData.Transactions);

      // NOTE: other services don't start until this method awaits
      // and that is leveraged to 
      // 1.) defeat race condition of missing _dataService changes
      // 2.) make the application not start if it fails to load
      await Task.Yield();
      
      try
      {
        while (!stoppingToken.IsCancellationRequested)
        {
          TrySave(
            noTouchieFileSystem: false,
            transactions: _dataService.Transactions.Values.Select(x => x.GetPersistedData()));

          await Task.Delay(1000, stoppingToken);
        }
      }
      finally
      {
        TrySave(
          noTouchieFileSystem: false,
          transactions: _dataService.Transactions.Values.Select(x => x.GetPersistedData()));
      }
    }

    private void TrySave(bool noTouchieFileSystem, IEnumerable<PersistedTransaction> transactions)
    {
      var toSave = new PersistenceData
      {
        Transactions = transactions
          .OrderBy(x => x.Time)
          .ThenBy(x => x.TradeId)
          .ToList(),
      };
      
      var newData = JsonConvert.SerializeObject(toSave);
      if (newData == _lastSavedData) return;
      
      _lastSavedData = newData;
      
      if (noTouchieFileSystem) return;

      _logger.LogInformation("Saving " + _dataFilePath);
      try
      {
        File.WriteAllText(_dataFilePath, newData);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Failed to save " + _dataFilePath);
      }
    }
  }
}