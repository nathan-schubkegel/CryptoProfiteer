using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace CryptoProfiteer
{
  public static class StringUtils
  {
    public static IEnumerable<string> GetLines(this string source)
    {
      var reader = new StringReader(source ?? string.Empty);
      string line;
      while (null != (line = reader.ReadLine()))
      {
        yield return line;
      }
      reader.Dispose();
    }
  }
}