using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FileGenPackage.Core;

namespace LoanWorker;

public class LoanHostedService : BackgroundService
{
    private readonly FileGenerationOrchestrator _orchestrator;
    private readonly ILogger<LoanHostedService> _logger;

    public LoanHostedService(FileGenerationOrchestrator orchestrator, ILogger<LoanHostedService> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LoanHostedService starting and delegating to orchestrator");
        await _orchestrator.RunAsync(stoppingToken);
    }
}
