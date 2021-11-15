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
    private readonly JsonSerializer _serializer = new JsonSerializer() { Formatting = Formatting.Indented };
    private readonly IDataService _dataService;
    private readonly string _dataFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data.json");
    private IReadOnlyDictionary<string, Transaction> _lastObservedTransactions;

    public PersistenceService(ILogger<PersistenceService> logger, IDataService dataService)
    {
      _logger = logger;
      _dataService = dataService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      var newData = PersistenceData.LoadFrom(_dataFilePath);
      _dataService.ImportTransactions(newData.Transactions);
      _lastObservedTransactions = _dataService.Transactions;
      
      // NOTE: other services don't continue loading until this method awaits
      // and that is leveraged for:
      // 1.) defeat the race condition of missing changes between ImportTransactions() and reading _dataService.Transactions
      // 2.) make the application not start if it fails to load
      await Task.Yield();
      
      try
      {
        while (!stoppingToken.IsCancellationRequested)
        {
          if (_lastObservedTransactions != _dataService.Transactions)
          {
            Save();
          }
          await Task.Delay(1000, stoppingToken);
        }
      }
      finally
      {
        if (_lastObservedTransactions != _dataService.Transactions)
        {
          Save();
        }
      }
    }

    private void Save()
    {
      // NOTE: overwriting _lastObservedTransactions here (before saving) means that
      // this class only makes 1 attempt to save for a given change
      _lastObservedTransactions = _dataService.Transactions;
      
      PersistenceData toSave = new PersistenceData
      {
        Transactions = _lastObservedTransactions.Values.OrderBy(x => x.Time).ThenBy(x => x.TradeId).ToList(),
      };

      _logger.LogInformation("Saving " + _dataFilePath);
      try
      {
        using (StreamWriter file = File.CreateText(_dataFilePath))
        {
          _serializer.Serialize(file, toSave);
        }
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Failed to save " + _dataFilePath);
      }
    }
  }
}