using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System;

namespace CryptoProfiteer
{
  public static class HttpClientSingleton
  {
    private static readonly HttpClient _client = new HttpClient();
    //private static SemaphoreSlim _semaphore = new SemaphoreSlim(1);

    public static async Task UseAsync(CancellationToken stoppingToken, Func<HttpClient, Task> action)
    {
      //await _semaphore.WaitAsync(stoppingToken);
      try
      {
        await action(_client);
      }
      finally
      {
        await Task.Delay(1000, stoppingToken); // ensure 1 full second between all coinbase api requests
        //_semaphore.Release();
      }
    }
  }
}