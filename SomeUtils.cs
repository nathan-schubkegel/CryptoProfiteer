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
  public static class SomeUtils
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
    
    public static DateTime ChopSecondsAndSmaller(this DateTime time)
    {
      // expected format: 2021-01-06T06:07:54.31Z
      var s = time.ToString("o");

      // get everything before and after the seconds and milliseconds
      var beforeSeconds = s.Substring(0, 17);
      var end = 17; while (end < s.Length) { if (s[end] == '.' || (s[end] >= '0' && s[end] <= '9')) end++; else break; }
      var remainder = s.Substring(end, s.Length - end);

      // Change the seconds and milliseconds to 00
      return DateTime.Parse(beforeSeconds + "00" + remainder, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }
    
    public static DateTime UnixEpochSecondsToDateTime(long unixEpochSeconds)
    {
      DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
      return dateTime.AddSeconds(unixEpochSeconds);
    }
    
    public static Decimal SetMaxDecimals(this Decimal input, int maxDecimalDigits)
    {
      if (maxDecimalDigits > 0)
      {
        string c = input.ToString(CultureInfo.InvariantCulture);
        int i = c.IndexOf('.');
        if (i >= 0)
        {
          int decimalDigits = c.Length - i - 1;
          if (decimalDigits > maxDecimalDigits)
          {
            c = c.Substring(0, c.Length - (decimalDigits - maxDecimalDigits));
            return decimal.Parse(c, NumberStyles.Float, CultureInfo.InvariantCulture);
          }
        }
        return input;
      }
      else
      {
        return Math.Round(input);
      }
      
      // NOTE: Hans Passant suggested it can be done like this (where 100 = 2 decimal places)
      // value = Math.Truncate(100 * value) / 100;
      // but I like my ugly code better - less potential loss - ha, as if that matters... 29 digits of room to use!
    }
  }
}