using System.Collections.Generic;
using System.IO;
using System;
using Newtonsoft.Json;

namespace CryptoProfiteer
{
  // NOTE: this type is JSON serialized/deserialized
  public class PersistenceData
  {
    public List<PersistedTransaction> Transactions { get; set; } = new List<PersistedTransaction>();
    
    public List<PersistedTaxAssociation> TaxAssociations { get; set; } = new List<PersistedTaxAssociation>();
    
    public List<PersistedHistoricalCoinPrice> HistoricalCoinPrices { get; set; } = new List<PersistedHistoricalCoinPrice>();

    public static PersistenceData LoadFrom(string dataFilePath)
    {
      var newData = new PersistenceData();
      if (File.Exists(dataFilePath))
      {
        using (StreamReader file = File.OpenText(dataFilePath))
        {
          var serializer = new JsonSerializer();
          newData = (PersistenceData)serializer.Deserialize(file, typeof(PersistenceData));
        }
      }
      return newData;
    }
  }
}