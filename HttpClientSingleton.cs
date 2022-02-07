using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System;

namespace CryptoProfiteer
{
  public interface IHttpClientSingleton
  {
    Task UseAsync(string description, CancellationToken stoppingToken, Func<HttpClient, Task> action);
  }
  
  public class HttpClientSingleton : IHttpClientSingleton
  {
    public static readonly string UserAgent = "CryptoProfiteer/0.3.0";

    private readonly ILogger<HttpClientSingleton> _logger;
    private readonly HttpClient _client = new HttpClient();
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1);

    public HttpClientSingleton(ILogger<HttpClientSingleton> logger)
    {
      _logger = logger;
    }

    public async Task UseAsync(string description, CancellationToken stoppingToken, Func<HttpClient, Task> action)
    {
      await _semaphore.WaitAsync(stoppingToken);
      try
      {
        _logger.LogInformation("HttpClientSingleton now doing: {0}", description);
        await action(_client);
      }
      finally
      {
        await Task.Delay(1000, stoppingToken); // ensure 1 full second between all coinbase api requests
        _semaphore.Release();
      }
    }
  }
}