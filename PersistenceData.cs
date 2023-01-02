using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        using (FileStream file = File.OpenRead(dataFilePath))
        using (StreamReader reader = new StreamReader(file))
        {
          // take the first 100 characters of the document and see if they look like this:
          // file format version [10.2-alpha]
          
          char[] firstLineBuffer = new char[100];
          int firstLineCharCount = reader.Read(firstLineBuffer, 0, 100);
          if (firstLineCharCount == -1) return newData; // the file is empty
          
          var firstLine = new string(firstLineBuffer, 0, firstLineCharCount);
          if (firstLine.StartsWith(newData.FirstLine))
          {
            reader.DiscardBufferedData();
            file.Position = 0;
            var serializer = new JsonSerializer();
            newData = (PersistenceData)serializer.Deserialize(reader, typeof(PersistenceData));
            return newData;
          }
          else if (firstLine.StartsWith(newData.FirstLine_UnknownVersion))
          {
            throw new Exception("This code can't handle persistence data in " + dataFilePath + " with unknown version: " + firstLine);
          }
          else
          {
            // it must be v04 (which didn't have a leading file version line)
            reader.DiscardBufferedData();
            file.Position = 0;
            var serializer = new JsonSerializer();
            var newData2 = (PersistenceData_v04)serializer.Deserialize(reader, typeof(PersistenceData_v04));
            return newData2.ToLatest();
          }
        }
      }
      return newData;
    }
    
    [JsonIgnore]
    // it's pretty lame how this class isn't responsible for saving its own data... sorry
    public string FirstLine { get; } = "// file format version [0.5]";
    
    private string FirstLine_UnknownVersion = "// file format version [";
  }

  public class PersistenceData_v04
  {
    public List<PersistedTransaction_v04> Transactions { get; set; } = new List<PersistedTransaction_v04>();
    
    public List<PersistedTaxAssociation_v04> TaxAssociations { get; set; } = new List<PersistedTaxAssociation_v04>();
    
    public List<PersistedHistoricalCoinPrice_v04> HistoricalCoinPrices { get; set; } = new List<PersistedHistoricalCoinPrice_v04>();
    
    public PersistenceData ToLatest() => new PersistenceData
    {
      Transactions = Transactions.Select(x => x.ToLatest()).ToList(),
      TaxAssociations = TaxAssociations.Select(x => x.ToLatest()).ToList(),
      HistoricalCoinPrices = HistoricalCoinPrices.Select(x => x.ToLatest()).ToList(),
    };
  }
}