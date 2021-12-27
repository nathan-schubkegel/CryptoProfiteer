using Microsoft.Extensions.Hosting;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System;
using System.Collections.Generic;
//using System.Windows.Automation;
//using FlaUI.Core.AutomationElements;
//using FlaUI.UIA3;
//using FlaUI.Core.Definitions;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace CryptoProfiteer
{
  public class ChromeService: BackgroundService
  {
    private readonly ILogger<ChromeService> _logger;
    private readonly IServer _server;

    public ChromeService(ILogger<ChromeService> logger, IServer server)
    {
      _logger = logger;
      _server = server;
    }
    
    private string GetServerAddress()
    {
      var addresses = _server?.Features.Get<IServerAddressesFeature>();
      return addresses?.Addresses?.FirstOrDefault();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      // Other services can't start until this service awaits, so await now!
      await Task.Yield();
      
      bool needsLaunch = true;
      while (!stoppingToken.IsCancellationRequested)
      {
        try
        {
          var address = GetServerAddress() ?? throw new Exception("Failed to determine server address!");
          if (needsLaunch)
          {
            Console.WriteLine("Launching chrome browser. Press 'Enter' in command line to launch another."
            needsLaunch = false;
            using var p = Process.Start(new ProcessStartInfo("http://www.stackoverflow.net")
            {
              UseShellExecute = true,
            });
          }
          
          var tcs = new TaskCompletionSource<object>();
          using var r = stoppingToken.Register(() => tcs.TrySetCanceled());
          await Task.WhenAny(tcs.Task, Task.Run(() => Console.ReadLine()));
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, $"{ex.GetType().Name} in ChromeService");
          await Task.Delay(1000, stoppingToken);
        }
      }
    }
  }
}