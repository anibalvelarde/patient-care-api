using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Neurocorp.Api.Web.Middleware.HealthChecks;

namespace Neurocorp.Api.Web;

public class Program
{
    public static void Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

        var startupCheck = host.Services.GetService<StartupHealthCheck>();
        if (startupCheck != null)
        {
            startupCheck.StartupCompleted = true;
        }
        host.Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>();
            });
}