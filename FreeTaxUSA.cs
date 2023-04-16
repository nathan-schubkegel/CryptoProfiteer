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
Click, 847 483
Sleep, 500

; ""save and continue"" 
Click, 1156 666
Sleep, 1500

; ""both"" (Nathan and Rachel) 
Click, 438 517
Sleep, 500

; ""save and continue"" 
Click, 1171 609
Sleep, 1500

; ""one at a time"" 
Click, 742 530
Sleep, 500

; ""save and continue""
Click, 1173 741
Sleep, 1500

; description textbox 
Click, 900 475
Sleep, 500
Send, {0}

; date acquired box  (month/day/year)
Click, 902 663
Sleep, 500
Send, {1}

; date sold (just month/day)
Click, 891 744
Sleep, 500
Send, {2}

; sale proceeds 
Click, 946 828
Sleep, 500
Send, {3}

; cost basis
Click, 938 927
Sleep, 500
Send, {4}

; pagedown a few times
Send, {{PgDn}}
Send, {{PgDn}}
Sleep, 1500

; ""not reported on 1099-B"" 
Click, 727 726
Sleep, 500

; ""save and continue"" 
Click, 1170 1007
Sleep, 1500

; ""save and continue"" 
if (({3} = 0) and ({4} = 0))
{{
  Click, 1180 957
}}
else if ({3} = 0)
{{
  MsgBox, Where is it?
}}
else if ({4} = 0)
{{
  Click, 1180 759
}}
else
{{
  Click, 1180 634
}}
Sleep, 5000

; press end key
Send, {{End}}
Sleep, 1500

; ""add another"" button 
Click, 528 822
Sleep, `500

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
      }
      builder.AppendLine(scriptTrailer);
      
      return new MemoryStream(Encoding.UTF8.GetBytes(builder.ToString()));
    }
  }
}