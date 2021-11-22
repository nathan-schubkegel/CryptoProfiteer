using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace CryptoProfiteer
{
  // NOTE: this type is JSON serialized/deserialized
  public class PersistenceData
  {
    public List<PersistedTransaction> Transactions { get; set; } = new List<PersistedTransaction>();
    
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