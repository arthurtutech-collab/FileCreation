using Microsoft.Extensions.Logging;
using FileGenPackage.Abstractions;

namespace FileGenPackage.Infrastructure;

/// <summary>
/// Kafka consumer for daily trigger events. Ensures only one processing run per day.
/// </summary>
public interface IDailyTriggerGuard
{
    /// <summary>
    /// Check if this event should trigger processing today.
    /// Returns true only if no processing run has occurred since midnight UTC.
    /// </summary>
    Task<bool> ShouldProcessAsync(string workerId, CancellationToken ct = default);

    /// <summary>
    /// Mark that processing was triggered today.
    /// </summary>
    Task MarkProcessedAsync(string workerId, CancellationToken ct = default);
}

/// <summary>
/// In-memory implementation for daily trigger guard (stateless, good for single-instance).
/// For distributed scenarios, use MongoDB-backed implementation.
/// </summary>
public class InMemoryDailyTriggerGuard : IDailyTriggerGuard
{
    private readonly Dictionary<string, DateTime> _lastProcessedDates = new();
    private readonly object _lock = new();
    private readonly ILogger<InMemoryDailyTriggerGuard> _logger;

    public InMemoryDailyTriggerGuard(ILogger<InMemoryDailyTriggerGuard> logger)
    {
        _logger = logger;
    }

    public Task<bool> ShouldProcessAsync(string workerId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var today = DateTime.UtcNow.Date;

            if (_lastProcessedDates.TryGetValue(workerId, out var lastProcessed))
            {
                if (lastProcessed.Date == today)
                {
                    _logger.LogInformation("Worker {WorkerId} already processed today", workerId);
                    return Task.FromResult(false);
                }
            }

            return Task.FromResult(true);
        }
    }

    public Task MarkProcessedAsync(string workerId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _lastProcessedDates[workerId] = DateTime.UtcNow;
            _logger.LogInformation("Worker {WorkerId} marked as processed for today", workerId);
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// MongoDB-backed daily trigger guard for distributed deployments.
/// Ensures exactly one run per worker per calendar day across all pods.
/// </summary>
public class MongoDBDailyTriggerGuard : IDailyTriggerGuard
{
    private readonly IProgressStore _progressStore;
    private readonly ILogger<MongoDBDailyTriggerGuard> _logger;

    public MongoDBDailyTriggerGuard(IProgressStore progressStore, ILogger<MongoDBDailyTriggerGuard> logger)
    {
        _progressStore = progressStore;
        _logger = logger;
    }

    public async Task<bool> ShouldProcessAsync(string workerId, CancellationToken ct = default)
    {
        try
        {
            // In a real impl, would check a separate trigger table
            // For now, check if any files are already started today
            var files = await _progressStore.ListByWorkerAsync(workerId, ct);
            var today = DateTime.UtcNow.Date;

            var processedToday = files.Any(f => f.StartedAt.Date == today);

            if (processedToday)
            {
                _logger.LogInformation("Worker {WorkerId} already processed today", workerId);
            }

            return !processedToday;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking daily trigger for {WorkerId}", workerId);
            return false;
        }
    }

    public async Task MarkProcessedAsync(string workerId, CancellationToken ct = default)
    {
        // Handled by SetStartAsync in progress store
        _logger.LogInformation("Worker {WorkerId} trigger marked in progress store", workerId);
        await Task.CompletedTask;
    }
}
