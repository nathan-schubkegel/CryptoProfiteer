using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CryptoProfiteer
{
  public static class FreeTaxUSA
  {
    const string scriptHeader =
      @"
      
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
Click, 957 748
Sleep, 500

; ""save and continue"" 
Click, 1433 1017
Sleep, 2500

";

    const string scriptTrailer =
      @"
}
";

    const string scriptFormat =
      @"

; ""both"" (Nathan and Rachel) 
Click, 541 565
Sleep, 500

; ""Coinbase""
Click, 634 667
Sleep, 500
Send, Coinbase

; ""save and continue"" 
Click, 1424 809
Sleep, 2500

; ""one at a time"" 
Click, 855 581
Sleep, 500

; ""save and continue""
Click, 1430 790
Sleep, 2500

; description textbox 
Click, 1025 792
Sleep, 500
Send, {0}

; date acquired box  (month/day/year)
Click, 1049 1017
Sleep, 500
Send, {1}

; date sold (just month/day)
Sleep, 500
Send, {{Tab}}
Sleep, 500
Send, {2}

; sale proceeds 
Sleep, 500
Send, {{Tab}}
Sleep, 500
Send, {3}

; cost basis
Sleep, 500
Send, {{Tab}}
Sleep, 500
Send, {4}

; pagedown a few times
Click, 180 698
Sleep, 500
Send, {{PgDn}}
Send, {{PgDn}}
Sleep, 1500

; ""basis not reported on 1099-DA"" 
Click, 828 411
Sleep, 500

; ""save and continue"" 
Click, 1429 872
Sleep, 2500

; ""save and continue"" 
if (({3} = 0) and ({4} = 0))
{{
  ; zero adjustments 
  Click, 675 822
  Sleep, 500
  ;Send, {{PgDn}}
  ;Sleep, 500
  Click, 1435 990
}}
else if ({3} = 0)
{{
  MsgBox, need new click points for zero win crypto I think?
  ;Click, 1077 916
}}
else if ({4} = 0)
{{
  MsgBox, need new click points for zero loss crypto I think?
  ;Click, 1077 847
}}
else
{{
  ; no adjustments (1099-DA is correct)
  Click, 637

  ; save and continue
  Click, 1439 782
}}
Sleep, 3000

; press end key
Send, {{End}}
Sleep, 1500

; ""add another"" button 
Click, 629 357
Sleep, 1500

      ";

    public static MemoryStream MakeScript(
      int iStart,
      IEnumerable<(
        string descriptionFormat,
        DateTime dateAcquired,
        DateTime dateSold,
        int saleProceeds,
        int costBasis
      )> data
    )
    {
      StringBuilder builder = new StringBuilder();

      builder.AppendLine(scriptHeader);
      int i = iStart;
      foreach (var item in data)
      {
        var description = string.Format(item.descriptionFormat, i);
        var dateAcquired = item.dateAcquired.ToLocalTime().ToString("MM/dd/yyyy");
        var dateSold = item.dateSold.ToLocalTime().ToString("MM/dd");
        builder.AppendLine(
          string.Format(scriptFormat, description, dateAcquired, dateSold, item.saleProceeds, item.costBasis)
        );
        Console.WriteLine(description);
        i++;
      }
      builder.AppendLine(scriptTrailer);

      return new MemoryStream(Encoding.UTF8.GetBytes(builder.ToString()));
    }
  }
}
