namespace FileGenPackage.Abstractions;

/// <summary>
/// MongoDB-based progress tracking for file generation status and pagination recovery.
/// Maintains per-file status (start/in-progress/completed) and page/row tracking.
/// </summary>
public interface IProgressStore
{
    /// <summary>
    /// Mark file processing as started.
    /// </summary>
    Task SetStartAsync(string fileId, CancellationToken ct = default);

    /// <summary>
    /// Update progress for current page and cumulative rows processed.
    /// Idempotent - safe to call multiple times with same values.
    /// </summary>
    Task UpsertProgressAsync(string fileId, int page, long rows, CancellationToken ct = default);

    /// <summary>
    /// Mark file processing as completed.
    /// </summary>
    Task SetCompletedAsync(string fileId, CancellationToken ct = default);

    /// <summary>
    /// Get current progress for a file. Returns null if not started.
    /// </summary>
    Task<FileProgress?> GetAsync(string fileId, CancellationToken ct = default);

    /// <summary>
    /// List all files for a worker to detect ongoing/incomplete processing.
    /// </summary>
    Task<IReadOnlyList<FileProgress>> ListByWorkerAsync(string workerId, CancellationToken ct = default);

    /// <summary>
    /// Get minimum outstanding page across all files (for takeover resume point).
    /// </summary>
    Task<int> GetMinOutstandingPageAsync(string workerId, CancellationToken ct = default);
}

public class FileProgress
{
    public required string FileId { get; set; }
    public required string WorkerId { get; set; }
    public required FileStatus Status { get; set; }
    public int LastPage { get; set; }
    public long CumulativeRows { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public enum FileStatus
{
    StatusStart,
    InProgress,
    Completed
}
