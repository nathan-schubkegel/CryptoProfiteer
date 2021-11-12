using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace CryptoProfiteer
{
  public class PersistenceData
  {
    // array because that's easier to feel like it's immutable
    public Transaction[] Transactions { get; set; } = new Transaction[0];

    public void TakeFrom(PersistenceData other)
    {
      Transactions = other.Transactions;
    }
  }

  public interface IPersistenceService
  {
    PersistenceData Data { get; }
    void MarkDirty();
  }

  public class PersistenceService : BackgroundService, IPersistenceService
  {
    private readonly ILogger<PersistenceService> _logger;
    private readonly JsonSerializer _serializer = new JsonSerializer();
    private volatile bool _dirty;

    public PersistenceData Data { get; } = new PersistenceData();
    
    public PersistenceService(ILogger<PersistenceService> logger)
    {
      _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      // no other services may continue until ExecuteAsync() awaits
      // so use that to sanity-guard loading data
      Load();
      
      await Task.Yield();
      try
      {
        while (!stoppingToken.IsCancellationRequested)
        {
          if (_dirty)
          {
            Save();
          }
          await Task.Delay(1000, stoppingToken);
        }
      }
      finally
      {
        if (_dirty)
        {
          Save();
        }
      }
    }

    public void MarkDirty()
    {
      _dirty = true;
    }

    private string DataFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data.json");

    private void Load()
    {
      var newData = new PersistenceData();
      if (File.Exists(DataFilePath))
      {
        using (StreamReader file = File.OpenText(DataFilePath))
        {
          newData = (PersistenceData)_serializer.Deserialize(file, typeof(PersistenceData));
        }
      }
      lock (Data) Data.TakeFrom(newData);
    }

    private void Save()
    {
      PersistenceData toSave = new PersistenceData();
      lock (Data)
      {
        toSave.TakeFrom(Data);
      }

      _dirty = false;
      _logger.LogInformation("Saving " + DataFilePath);
      using (StreamWriter file = File.CreateText(DataFilePath))
      {
        _serializer.Serialize(file, toSave);
      }
    }
  }
}