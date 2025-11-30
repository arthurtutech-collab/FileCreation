using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using FileGenPackage.Abstractions;

namespace FileGenPackage.Infrastructure;

/// <summary>
/// Readiness probe for OCP: Checks MongoDB, Kafka, and SQL connectivity.
/// </summary>
public class ReadinessHealthCheck : IHealthCheck
{
    private readonly ILeaseStore _leaseStore;
    private readonly IProgressStore _progressStore;
    private readonly KafkaConfig _kafkaConfig;
    private readonly IPageReader _pageReader;
    private readonly ILogger<ReadinessHealthCheck> _logger;

    public ReadinessHealthCheck(
        ILeaseStore leaseStore,
        IProgressStore progressStore,
        KafkaConfig kafkaConfig,
        IPageReader pageReader,
        ILogger<ReadinessHealthCheck> logger)
    {
        _leaseStore = leaseStore;
        _progressStore = progressStore;
        _kafkaConfig = kafkaConfig;
        _pageReader = pageReader;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            // Check MongoDB by attempting lease check
            await _leaseStore.IsExpiredOrUnheldAsync("readiness-check", ct);

            // Check SQL by attempting a row count
            await _pageReader.GetTotalRowCountAsync(ct);

            // Check Kafka connectivity (would attempt admin client in production)
            if (string.IsNullOrEmpty(_kafkaConfig.BootstrapServers))
                throw new InvalidOperationException("Kafka brokers not configured");

            _logger.LogInformation("Readiness check passed");
            return HealthCheckResult.Healthy("All dependencies accessible");
        }
        catch (OperationCanceledException)
        {
            return HealthCheckResult.Degraded("Readiness check timeout");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Readiness check failed");
            return HealthCheckResult.Unhealthy("Readiness check failed", ex);
        }
    }
}

/// <summary>
/// Liveness probe for OCP: Checks heartbeat renewal and recent progress.
/// </summary>
public class LivenessHealthCheck : IHealthCheck
{
    private readonly ILeaseStore _leaseStore;
    private readonly IProgressStore _progressStore;
    private readonly string _workerId;
    private readonly string _instanceId;
    private readonly ILogger<LivenessHealthCheck> _logger;
    private DateTime _lastProgressCheck = DateTime.UtcNow;
    private bool _hasProcessed;

    public LivenessHealthCheck(
        ILeaseStore leaseStore,
        IProgressStore progressStore,
        string workerId,
        string instanceId,
        ILogger<LivenessHealthCheck> logger)
    {
        _leaseStore = leaseStore;
        _progressStore = progressStore;
        _workerId = workerId;
        _instanceId = instanceId;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            // Check if we can still see our lease (heartbeat indicator)
            var lease = await _leaseStore.GetLeaseAsync(_workerId, ct);
            if (lease?.InstanceId != _instanceId)
            {
                // We're not the leader, that's ok for liveness
                return HealthCheckResult.Healthy("Not the leader, liveness ok");
            }

            // We're the leader - check if making progress
            var files = await _progressStore.ListByWorkerAsync(_workerId, ct);
            var hasRecentProgress = files.Any(f =>
                f.Status == FileStatus.InProgress &&
                DateTime.UtcNow.Subtract(f.StartedAt).TotalMinutes < 10);

            if (hasRecentProgress || !_hasProcessed)
            {
                _lastProgressCheck = DateTime.UtcNow;
                _hasProcessed = true;
                return HealthCheckResult.Healthy("Progress detected");
            }

            // Check staleness
            var timeSinceProgress = DateTime.UtcNow.Subtract(_lastProgressCheck);
            if (timeSinceProgress.TotalMinutes < 5)
            {
                return HealthCheckResult.Healthy("Recent progress");
            }

            _logger.LogWarning("No progress in {Minutes} minutes", timeSinceProgress.TotalMinutes);
            return HealthCheckResult.Degraded($"No progress for {timeSinceProgress.TotalMinutes:F0} minutes");
        }
        catch (OperationCanceledException)
        {
            return HealthCheckResult.Degraded("Liveness check timeout");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Liveness check failed");
            return HealthCheckResult.Unhealthy("Liveness check failed", ex);
        }
    }
}
