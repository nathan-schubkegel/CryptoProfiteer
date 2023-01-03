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
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace CryptoProfiteer
{
  public class BrowserLauncherService: BackgroundService
  {
    private readonly ILogger<BrowserLauncherService> _logger;
    private readonly IServer _server;

    public BrowserLauncherService(ILogger<BrowserLauncherService> logger, IServer server)
    {
      _logger = logger;
      _server = server;
    }
    
    private string GetServerAddress()
    {
      var addresses = _server?.Features.Get<IServerAddressesFeature>();
      return addresses?.Addresses?.FirstOrDefault(x => x.StartsWith("http://"));
    }
    
    private async Task LaunchBrowser(CancellationToken stoppingToken)
    {
      stoppingToken.ThrowIfCancellationRequested();
      try
      {
        var args = $"{GetServerAddress()} --new-window";
        var path = @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe";
        if (!File.Exists(path))
        {
          path = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
          if (!File.Exists(path))
          {
            // use the default browser
            path = "cmd.exe";
            args = $"/c explorer {GetServerAddress()}";
          }
        }
        
        _logger.LogInformation("Launching new browser window. Press 'Enter' in command line to launch another.");
        using var p = Process.Start(new ProcessStartInfo(path, args));
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, $"{ex.GetType().Name} while trying to launch a new browser window");
        await Task.Delay(5000, stoppingToken);
      }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      // Other services can't start until this service awaits, so await now!
      await Task.Yield();
      
      // wait until the server's address is known
      while (!stoppingToken.IsCancellationRequested)
      {
        try
        {
          var address = GetServerAddress();
          if (address == null)
          {
            await Task.Delay(100, stoppingToken);
          }
          else
          {
            break;
          }
        }
        catch (OperationCanceledException)
        {
          // this is our fault for shutting down. Don't bother logging it.
          throw;
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, $"{ex.GetType().Name} while attempting to determine server address");
          await Task.Delay(5000, stoppingToken);
        }
      }

      // launch a browser
      await LaunchBrowser(stoppingToken);

      // whenever the user presses 'Enter' in the console window, launch another browser
      while (!stoppingToken.IsCancellationRequested)
      {
        try
        {
          var tcs = new TaskCompletionSource<object>();
          using var r = stoppingToken.Register(() => tcs.TrySetCanceled());
          var readLine = Task.Run(() => Console.ReadLine());
          var result = await Task.WhenAny(tcs.Task, readLine);
          if (result == readLine)
          {
            // if the application has no console for some reason, then just give up (don't go into an infinite loop opening chrome!)
            if (!readLine.IsCompletedSuccessfully) break;

            // the application interprets Ctrl+C as a request to shut down
            // so wait 500ms to see if stoppingToken becomes signaled before launching another browser
            await Task.Delay(500, stoppingToken);
            await LaunchBrowser(stoppingToken);
          }
        }
        catch (OperationCanceledException)
        {
          // this is our fault for shutting down. Don't bother logging it.
          throw;
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, $"{ex.GetType().Name} in BrowserLauncherService");
          await Task.Delay(5000, stoppingToken);
        }
      }
    }
  }
}