namespace FileGenPackage.Abstractions;

/// <summary>
/// MongoDB-based lease store for single-writer coordination across pods and data centers.
/// Uses TTL-based expiry and atomic operations for exactly-one-writer guarantee.
/// </summary>
public interface ILeaseStore
{
    /// <summary>
    /// Attempt to acquire exclusive lease for a worker. Returns true if acquired.
    /// </summary>
    Task<bool> TryAcquireAsync(string workerId, string instanceId, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>
    /// Renew an active lease. Returns true if renewed, false if lease expired/lost.
    /// </summary>
    Task<bool> RenewAsync(string workerId, string instanceId, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>
    /// Release the lease held by this instance.
    /// </summary>
    Task ReleaseAsync(string workerId, string instanceId, CancellationToken ct = default);

    /// <summary>
    /// Check if lease is expired or unheld (for takeover detection).
    /// </summary>
    Task<bool> IsExpiredOrUnheldAsync(string workerId, CancellationToken ct = default);

    /// <summary>
    /// Get current lease holder for diagnostics.
    /// </summary>
    Task<LeaseInfo?> GetLeaseAsync(string workerId, CancellationToken ct = default);
}

public class LeaseInfo
{
    public required string WorkerId { get; set; }
    public required string InstanceId { get; set; }
    public DateTime AcquiredAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
