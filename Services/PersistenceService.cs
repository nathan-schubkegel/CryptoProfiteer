using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System;
using System.Collections.Generic;
using CryptoProfiteer.Models;
using Newtonsoft.Json;

namespace CryptoProfiteer.Services
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
  }

  public interface IPersistenceService
  {
    PersistenceData Data { get; }
    void MarkDirty();
  }

  public class PersistenceService : BackgroundService, IPersistenceService
  {
    private readonly JsonSerializer _serializer = new JsonSerializer();
    private volatile bool _dirty;

    public PersistenceData Data { get; } = NewFakeData();

    private static PersistenceData NewFakeData()
    {
      return new PersistenceData
    {
      Transactions = new List<Transaction> {
        new Transaction
        {
          CoinType = "POO",
          CoinCount = 2.3m,
          PerCoinCost = 0.0001m,
          Fee = 3m,
          TotalCost = 3.00023m
        },
        new Transaction
        {
          CoinType = "POO",
          CoinCount = 7m,
          PerCoinCost = 0.0002m,
          Fee = 2m,
          TotalCost = 2.00014m
        },
        new Transaction
        {
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
      
      // TODO: remove this when I want to start saving/loading real data
      Data.TakeFrom(NewFakeData());

      await Task.Yield();
      try
      {
        while (!stoppingToken.IsCancellationRequested)
        {
          if (_dirty)
          {
            _dirty = false;
            Save();
          }
          await Task.Delay(1000, stoppingToken);
        }
      }
      finally
      {
        Save();
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

      // serialize JSON directly to a file
      using (StreamWriter file = File.CreateText(DataFilePath))
      {
        _serializer.Serialize(file, toSave);
      }
    }
  }
}