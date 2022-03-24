using System.Collections.Generic;
using System.Text;

namespace CryptoProfiteer
{
  public class Csv
  {
    public static List<string> Parse(string line)
    {
      line ??= string.Empty;
      var parts = new List<string>();
      var current = new StringBuilder();
      bool inQuotes = false;
      bool hasAnother = false;
      for (int i = 0; i < line.Length; i++)
      {
        char c = line[i];
        if (c == '\"')
        {
          inQuotes = !inQuotes;
          current.Append(c);
        }
        else if (c == ',' && !inQuotes)
        {
          parts.Add(current.ToString());
          current.Clear();
          hasAnother = true;
        }
        else
        {
          current.Append(c);
        }
      }
      if (current.Length > 0 || hasAnother)
      {
        parts.Add(current.ToString());
      }

      // change items like 
      //    "hello my ""dear"" wife"
      // to
      //    hello my "dear" wife
      for (int i = 0; i < parts.Count; i++)
      {
        string fields = parts[i];
        if (fields.Contains('\"'))
        {
          fields = fields.Trim();
          if (fields.Length >= 2 && fields[0] == '\"' && fields[fields.Length - 1] == '\"')
          {
            fields = fields.Substring(1, fields.Length - 2);
          }
          fields = fields.Replace("\"\"", "\"");
          parts[i] = fields;
        }
      }
      
      return parts;
    }
  }
}
