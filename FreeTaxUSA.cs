using System.Collections.Generic;
using System;
using System.IO;
using System.Text;

namespace CryptoProfiteer
{
  public static class FreeTaxUSA
  {
    const string scriptHeader = @"
      
Add()

Esc::
  Suspend, Off
  Pause, Off, 1
  If (toggle := !toggle) {
    Suspend, On
    Pause, On, 1
  }
  return

Add()
{

MsgBox, Make sure you're at the ""What type of investment did you sell?"" page, and press ""Escape"" to pause if needed
CoordMode, Mouse, Client
";

      const string scriptTrailer = @"
}
";

      const string scriptFormat = @"

if WinExist(""FreeTaxUSA"")
{{
    WinActivate ; Use the window found by WinExist.
    ;WinActivate, ""FreeTaxUSA""
}}
else 
{{
    MsgBox, can't find FreeTaxUSA window
    return
}}

; ""it's a crypto"" button
Click, 636 501
Sleep, 500

; ""save and continue"" 
Click, 1086 750
Sleep, 2500

; ""both"" (Nathan and Rachel) 
Click, 220 545
Sleep, 500

; ""save and continue"" 
Click, 1078 694
Sleep, 2500

; ""one at a time"" 
Click, 536 547
Sleep, 500

; ""save and continue""
Click, 1082 819
Sleep, 2500

; description textbox 
Click, 693 508
Sleep, 500
Send, {0}

; date acquired box  (month/day/year)
Click, 718 692
Sleep, 500
Send, {1}

; date sold (just month/day)
Click, 692 772
Sleep, 500
Send, {2}

; sale proceeds 
Click, 756 861
Sleep, 500
Send, {3}

; cost basis
Click, 753 957
Sleep, 500
Send, {4}

; pagedown a few times
Click, 1000 958
Sleep, 500
Send, {{PgDn}}
Send, {{PgDn}}
Sleep, 1500

; ""not reported on 1099-B"" 
Click, 510 411
Sleep, 500

; ""save and continue"" 
Click, 1073 807
Sleep, 2500

; ""save and continue"" 
if (({3} = 0) and ({4} = 0))
{{
  Click, 1077 752
  Sleep, 500
  Send, {{PgDn}}
  Sleep, 500
  Click, 1077 774
}}
else if ({3} = 0)
{{
  Click, 1077 916
}}
else if ({4} = 0)
{{
  Click, 1077 847
}}
else
{{
  Click, 1077 717
}}
Sleep, 5000

; press end key
Send, {{End}}
Sleep, 1500

; ""add another"" button 
Click, 321 565
Sleep, 1500

      ";
      
    public static MemoryStream MakeScript(int iStart, IEnumerable<(string descriptionFormat, DateTime dateAcquired, DateTime dateSold, int saleProceeds, int costBasis)> data)
    {
      StringBuilder builder = new StringBuilder();

      builder.AppendLine(scriptHeader);
      int i = iStart;
      foreach (var item in data)
      {
        var description = string.Format(item.descriptionFormat, i);
        var dateAcquired = item.dateAcquired.ToLocalTime().ToString("MM/dd/yyyy");
        var dateSold = item.dateSold.ToLocalTime().ToString("MM/dd");
        builder.AppendLine(string.Format(scriptFormat, description, dateAcquired, dateSold, item.saleProceeds, item.costBasis));
        Console.WriteLine(description);
        i++;
      }
      builder.AppendLine(scriptTrailer);
      
      return new MemoryStream(Encoding.UTF8.GetBytes(builder.ToString()));
    }
  }
}