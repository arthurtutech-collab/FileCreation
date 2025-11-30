using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FileGenPackage.Abstractions;
using FileGenPackage.Infrastructure;

namespace FileGenPackage.Core;

/// <summary>
/// Main hosted service orchestrating file generation with leadership, takeover, and crash-resume.
/// Handles:
/// - Kafka event triggering (once per day)
/// - Lease acquisition for single-writer coordination
/// - SQL pagination with per-file translation
/// - File-level status tracking and progress headers
/// - Automatic takeover when leader fails
/// - Safe finalization with header removal and event publication
/// </summary>
public class FileGenerationHostedService : BackgroundService
{
    private readonly FileGenerationOrchestrator _orchestrator;
    private readonly ILogger<FileGenerationHostedService> _logger;

    public FileGenerationHostedService(FileGenerationOrchestrator orchestrator, ILogger<FileGenerationHostedService> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("File generation hosted service starting");
        await base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("File generation hosted service delegating to orchestrator");
        await _orchestrator.RunAsync(stoppingToken);
    }


    public override async Task StopAsync(CancellationToken ct)
    {
        _logger.LogInformation("File generation service stopping");
        await base.StopAsync(ct);
    }
}
