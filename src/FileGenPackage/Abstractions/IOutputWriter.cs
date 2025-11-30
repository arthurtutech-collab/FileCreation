namespace FileGenPackage.Abstractions;

/// <summary>
/// Buffered, append-only file writer with atomic header updates for crash-safe recovery.
/// Header format: first line contains "{page},{rows}" for resume tracking.
/// </summary>
public interface IOutputWriter : IAsyncDisposable
{
    /// <summary>
    /// Write or update header with current page and row counts.
    /// Atomic operation - safe for crash recovery.
    /// </summary>
    Task WriteHeaderAsync(int page, long rows, CancellationToken ct = default);

    /// <summary>
    /// Append translated lines to file. Lines should already include newlines.
    /// </summary>
    Task AppendLinesAsync(IEnumerable<string> lines, CancellationToken ct = default);

    /// <summary>
    /// Remove header before finalization.
    /// </summary>
    Task RemoveHeaderAsync(CancellationToken ct = default);

    /// <summary>
    /// Flush pending writes and close file safely.
    /// </summary>
    Task CloseAsync(CancellationToken ct = default);
}

/// <summary>
/// Factory for creating writers per output file.
/// </summary>
public interface IOutputWriterFactory
{
    IOutputWriter CreateWriter(string filePath, string fileId);
}
