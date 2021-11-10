using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
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
  }
}
