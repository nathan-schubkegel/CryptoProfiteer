using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using CryptoProfiteer.Models;
using CryptoProfiteer.Services;

namespace CryptoProfiteer.Controllers
{
  [ApiController]
  [Route("api")]
  public class TheController : ControllerBase
  {
    private readonly IServiceProvider _provider;
    private readonly ILogger<TheController> _logger;

    public TheController(IServiceProvider provider, ILogger<TheController> logger)
    {
      _provider = provider;
      _logger = logger;
    }

    [HttpGet("transactions")]
    public IEnumerable<Transaction> GetTransactions()
    {
      var data = _provider.GetRequiredService<IPersistenceService>().Data;
      lock (data)
      {
        return data.Transactions.ToList();
      }
    }
    
    [HttpPost("fillsCsv")]
    public async Task<IActionResult> PostFillsCsv(IFormFile file) //List<IFormFile> files)
    {
      if (file.Length > 0)
      {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;
        using var sr = new StreamReader(ms);
        string line;
        while (null != (line = sr.ReadLine()))
        {
          Console.WriteLine(line);
        }
      }
      
      return Ok();
    }
  }
}
