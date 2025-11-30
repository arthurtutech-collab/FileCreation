using Microsoft.Extensions.Logging;
using FileGenPackage.Abstractions;
using FileGenPackage.Infrastructure;

namespace FileGenPackage.Core;

/// <summary>
/// Reusable orchestrator implementing the file generation logic.
/// This class contains the orchestration logic previously embedded in the hosted service
/// and can be invoked by worker-specific BackgroundService implementations.
/// </summary>
public class FileGenerationOrchestrator
{
    private readonly IWorkerConfig _config;
    private readonly ILeaseStore _leaseStore;
    private readonly IProgressStore _progressStore;
    private readonly IPageReader _pageReader;
    private readonly ITranslatorRegistry _translatorRegistry;
    private readonly IOutputWriterFactory _writerFactory;
    private readonly IEventPublisher _eventPublisher;
    private readonly IDailyTriggerGuard _triggerGuard;
    private readonly ILogger<FileGenerationOrchestrator> _logger;
    private readonly string _instanceId = Guid.NewGuid().ToString();
    private bool _isLeader;
    private Task? _heartbeatTask;
    private CancellationTokenSource? _heartbeatCts;

    public FileGenerationOrchestrator(
        IWorkerConfig config,
        ILeaseStore leaseStore,
        IProgressStore progressStore,
        IPageReader pageReader,
        ITranslatorRegistry translatorRegistry,
        IOutputWriterFactory writerFactory,
        IEventPublisher eventPublisher,
        IDailyTriggerGuard triggerGuard,
        ILogger<FileGenerationOrchestrator> logger)
    {
        _config = config;
        _leaseStore = leaseStore;
        _progressStore = progressStore;
        _pageReader = pageReader;
        _translatorRegistry = translatorRegistry;
        _writerFactory = writerFactory;
        _eventPublisher = eventPublisher;
        _triggerGuard = triggerGuard;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Orchestrator running. WorkerId={WorkerId}, InstanceId={InstanceId}", _config.WorkerId, _instanceId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await AttemptLeadershipAndProcess(stoppingToken);
                await Task.Delay(_config.Policies.TakeoverPollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Orchestrator cancelled");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in orchestrator loop");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        await ReleaseLeadershipAsync();
        _heartbeatCts?.Cancel();
        if (_heartbeatTask != null)
        {
            try { await _heartbeatTask; } catch { }
        }
    }

    private async Task AttemptLeadershipAndProcess(CancellationToken stoppingToken)
    {
        var acquired = await _leaseStore.TryAcquireAsync(
            _config.WorkerId,
            _instanceId,
            _config.Policies.LeaseTtl,
            stoppingToken);

        if (!acquired)
        {
            _logger.LogDebug("Could not acquire leadership");
            return;
        }

        _isLeader = true;
        _logger.LogInformation("Leadership acquired");

        _heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _heartbeatTask = RenewLeaseHeartbeat(_heartbeatCts.Token);

        try
        {
            var shouldProcess = await _triggerGuard.ShouldProcessAsync(_config.WorkerId, stoppingToken);
            if (!shouldProcess)
            {
                _logger.LogInformation("Daily trigger already processed");
                return;
            }

            await ProcessFilesAsync(stoppingToken);
            await _triggerGuard.MarkProcessedAsync(_config.WorkerId, stoppingToken);
        }
        finally
        {
            _isLeader = false;
            await ReleaseLeadershipAsync();
        }
    }

    private async Task ProcessFilesAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting file processing");

        // Determine resume page: get the minimum page across all files to avoid skipping any file
        var resumeFromPage = await _progressStore.GetMinOutstandingPageAsync(_config.WorkerId, ct);
        
        // Only set start for files not yet started
        foreach (var fileConfig in _config.Files)
        {
            var progress = await _progressStore.GetAsync(fileConfig.FileId, ct);
            if (progress == null)
            {
                await _progressStore.SetStartAsync(fileConfig.FileId, ct);
            }
        }

        try
        {
            var totalRows = await _pageReader.GetTotalRowCountAsync(ct);
            var totalPages = (int)Math.Ceiling((double)totalRows / _config.Sql.PageSize);

            _logger.LogInformation("Total rows: {TotalRows}, total pages: {TotalPages}", totalRows, totalPages);
            _logger.LogInformation("Resuming from page {ResumePage}", resumeFromPage);

            for (int pageNum = resumeFromPage; pageNum < totalPages && _isLeader; pageNum++)
            {
                var lease = await _leaseStore.GetLeaseAsync(_config.WorkerId, ct);
                if (lease?.InstanceId != _instanceId)
                {
                    _logger.LogInformation("Leadership lost, stopping processing");
                    break;
                }

                var rows = await _pageReader.ReadPageAsync(pageNum, ct);
                if (rows.Count == 0)
                {
                    _logger.LogInformation("Page {Page} is empty, stopping", pageNum);
                    break;
                }

                _logger.LogInformation("Processing page {Page} with {Count} rows", pageNum, rows.Count);

                var cumulativeRows = pageNum * _config.Sql.PageSize + rows.Count;

                var tasks = _config.Files.Select(async fileConfig =>
                {
                    await ProcessPageForFileAsync(fileConfig, pageNum, rows, cumulativeRows, ct);
                });

                await Task.WhenAll(tasks);
            }

            await FinalizeFilesAsync(ct);
            _logger.LogInformation("File processing completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during file processing");
            throw;
        }
    }

    private async Task ProcessPageForFileAsync(
        TargetFileConfig fileConfig,
        int pageNum,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        long cumulativeRows,
        CancellationToken ct)
    {
        try
        {
            var progress = await _progressStore.GetAsync(fileConfig.FileId, ct);
            if (progress?.LastPage >= pageNum && progress?.Status == FileStatus.Completed)
            {
                _logger.LogDebug("Skipping {FileId}: already completed beyond page {Page}", fileConfig.FileId, pageNum);
                return;
            }

            var filePath = Path.Combine(
                _config.Paths.OutputRootPath,
                fileConfig.FileNamePattern.Replace("{date}", DateTime.UtcNow.ToString("yyyyMMdd")));

            var translator = _translatorRegistry.GetTranslator(fileConfig.TranslatorId);
            var translatedLines = translator.TranslateBatch(rows).Select(line => line + "\n");

            await using var writer = _writerFactory.CreateWriter(filePath, fileConfig.FileId);

            await writer.WriteHeaderAsync(pageNum, cumulativeRows, ct);
            await writer.AppendLinesAsync(translatedLines, ct);
            await writer.CloseAsync(ct);

            await _progressStore.UpsertProgressAsync(fileConfig.FileId, pageNum, cumulativeRows, ct);

            _logger.LogDebug("Page {Page} written for {FileId}", pageNum, fileConfig.FileId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing page {Page} for {FileId}", pageNum, fileConfig.FileId);
            throw;
        }
    }

    private async Task FinalizeFilesAsync(CancellationToken ct)
    {
        _logger.LogInformation("Finalizing files");

        foreach (var fileConfig in _config.Files)
        {
            try
            {
                var filePath = Path.Combine(
                    _config.Paths.OutputRootPath,
                    fileConfig.FileNamePattern.Replace("{date}", DateTime.UtcNow.ToString("yyyyMMdd")));

                if (File.Exists(filePath))
                {
                    await using var writer = _writerFactory.CreateWriter(filePath, fileConfig.FileId);
                    await writer.RemoveHeaderAsync(ct);
                    await writer.CloseAsync(ct);
                }

                await _progressStore.SetCompletedAsync(fileConfig.FileId, ct);

                await _eventPublisher.PublishCompletedAsync(
                    _config.WorkerId,
                    fileConfig.FileId,
                    _config.Kafka.EventType,
                    ct);

                _logger.LogInformation("File {FileId} finalized and event published", fileConfig.FileId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finalizing {FileId}", fileConfig.FileId);
                throw;
            }
        }
    }

    private async Task RenewLeaseHeartbeat(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(_config.Policies.LeaseHeartbeatInterval, ct);

                var renewed = await _leaseStore.RenewAsync(
                    _config.WorkerId,
                    _instanceId,
                    _config.Policies.LeaseTtl,
                    ct);

                if (!renewed)
                {
                    _logger.LogWarning("Lease renewal failed - leadership lost");
                    _isLeader = false;
                    return;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in lease heartbeat");
        }
    }

    private async Task ReleaseLeadershipAsync()
    {
        if (!_isLeader) return;

        try
        {
            await _leaseStore.ReleaseAsync(_config.WorkerId, _instanceId);
            _logger.LogInformation("Leadership released");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing leadership");
        }
    }
}
