using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using FileGenPackage;
using CustomerWorker;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var host = Host.CreateDefaultBuilder(args)
    .UseConsoleLifetime()
    .ConfigureWebHostDefaults(webBuilder =>
    {
        // Bind Kestrel to a port configurable via env var HEALTH_PORT (default 5000)
        var portStr = Environment.GetEnvironmentVariable("HEALTH_PORT");
        var port = int.TryParse(portStr, out var p) ? p : 5000;
        webBuilder.UseUrls($"http://*:{port}");

        webBuilder.ConfigureKestrel(options => { });
        webBuilder.Configure((context, app) =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
                {
                    Predicate = reg => reg.Name == "readiness",
                    ResponseWriter = async (ctx, report) =>
                    {
                        ctx.Response.ContentType = "application/json";
                        var result = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            status = report.Status.ToString(),
                            checks = report.Entries.Select(e => new { key = e.Key, value = e.Value.Status.ToString() })
                        });
                        await ctx.Response.WriteAsync(result);
                    }
                });

                endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
                {
                    Predicate = reg => reg.Name == "liveness",
                    ResponseWriter = async (ctx, report) =>
                    {
                        ctx.Response.ContentType = "application/json";
                        var result = System.Text.Json.JsonSerializer.Serialize(new { status = report.Status.ToString() });
                        await ctx.Response.WriteAsync(result);
                    }
                });
            });
        });
    })
    .ConfigureServices((context, services) =>
    {
        var config = new CustomerWorkerConfig();
        services.AddFileGenerationPackage(config, registry =>
        {
            registry.Register("CFMTranslator", new CFMTranslator());
            registry.Register("CFKTranslator", new CFKTranslator());
        }, registerHostedService: false);

        services.AddHostedService<CustomerHostedService>();
    })
    .UseSerilog((context, loggerConfig) =>
    {
        loggerConfig
            .MinimumLevel.Information()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
    })
    .Build();

await host.RunAsync();
