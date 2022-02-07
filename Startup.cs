using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CryptoProfiteer
{
  public class Startup
  {
    public Startup(IConfiguration configuration)
    {
      Configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    // This method gets called by the runtime. Use this method to add services to the container.
    public void ConfigureServices(IServiceCollection services)
    {
      services.AddRazorPages();
      services.AddControllersWithViews();

      services.AddHostedService<PersistenceService>();
      
      services.AddSingleton<PriceService>();
      services.AddSingleton<IPriceService>(sp => sp.GetRequiredService<PriceService>());
      services.AddHostedService(sp => sp.GetRequiredService<PriceService>());

      services.AddSingleton<FriendlyNameService>();
      services.AddSingleton<IFriendlyNameService>(sp => sp.GetRequiredService<FriendlyNameService>());
      services.AddHostedService(sp => sp.GetRequiredService<FriendlyNameService>());

      services.AddHostedService<BrowserLauncherService>();

      services.AddSingleton<HistoricalCoinPriceService>();
      services.AddSingleton<IHistoricalCoinPriceService>(sp => sp.GetRequiredService<HistoricalCoinPriceService>());
      services.AddHostedService(sp => sp.GetRequiredService<HistoricalCoinPriceService>());

      services.AddSingleton<IDataService, DataService>();
      services.AddSingleton<IAltCoinAlertService, AltCoinAlertService>();
      services.AddSingleton<IHttpClientSingleton, HttpClientSingleton>();
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
      if (env.IsDevelopment())
      {
        app.UseDeveloperExceptionPage();
      }
      else
      {
        app.UseExceptionHandler("/Error");
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
      }

      app.UseStaticFiles();

      app.UseRouting();

      app.UseEndpoints(endpoints =>
      {
        endpoints.MapRazorPages();
        endpoints.MapControllerRoute(
          name: "default",
          pattern: "{controller=TheController}/{action=Index}/{id?}");
      });
    }
  }
}
