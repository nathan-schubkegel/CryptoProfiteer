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
    public List<Transaction> Transactions { get; set; }

    public void Cleanse()
    {
      Transactions ??= new List<Transaction>();
      Transactions.RemoveAll(x => x == null);
      Transactions.ForEach(x => x.Cleanse());
    }

    public void TakeFrom(PersistenceData other)
    {
      Transactions = other.Transactions;
    }
    
    public void SortTransactions()
    {
      Transactions.Sort((a, b) => 
      {
        var r = Comparer<DateTimeOffset>.Default.Compare(a.Time, b.Time);
        if (r != 0) return -r;
        return Comparer<string>.Default.Compare(a.TradeId, b.TradeId);
      });
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

    public PersistenceData Data { get; } = NewFakeData();
    
    public PersistenceService(ILogger<PersistenceService> logger)
    {
      _logger = logger;
    }

    private static PersistenceData NewFakeData()
    {
      return new PersistenceData
      {
        Transactions = new List<Transaction> {
          new Transaction
          {
            TradeId = "POO1",
            TransactionType = TransactionType.Buy,
            CoinType = "POO",
            CoinCount = 2.3m,
            PerCoinCost = 0.0001m,
            Fee = 3m,
            TotalCost = 3.00023m
          },
          new Transaction
          {
            TradeId = "POO2",
            TransactionType = TransactionType.Sell,
            CoinType = "POO",
            CoinCount = 7m,
            PerCoinCost = 0.0002m,
            Fee = 2m,
            TotalCost = 2.00014m
          },
          new Transaction
          {
            TradeId = "POO3",
            TransactionType = TransactionType.Buy,
            CoinType = "BTC",
            CoinCount = 0.00001m,
            PerCoinCost = 60000m,
            Fee = 3m,
            TotalCost = 3.6m
          },
        }
      };
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
      PersistenceData newData = null;
      if (File.Exists(DataFilePath))
      {
        using (StreamReader file = File.OpenText(DataFilePath))
        {
          newData = (PersistenceData)_serializer.Deserialize(file, typeof(PersistenceData));
        }
      }
      newData ??= NewFakeData();
      newData.Cleanse();
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