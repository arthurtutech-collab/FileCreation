using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FileGenPackage.Core;

namespace CustomerWorker;

public class CustomerHostedService : BackgroundService
{
    private readonly FileGenerationOrchestrator _orchestrator;
    private readonly ILogger<CustomerHostedService> _logger;

    public CustomerHostedService(FileGenerationOrchestrator orchestrator, ILogger<CustomerHostedService> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CustomerHostedService starting and delegating to orchestrator");
        await _orchestrator.RunAsync(stoppingToken);
    }
}
