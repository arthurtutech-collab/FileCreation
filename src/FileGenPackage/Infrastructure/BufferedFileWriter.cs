using System.Text;
using Microsoft.Extensions.Logging;
using FileGenPackage.Abstractions;

namespace FileGenPackage.Infrastructure;

/// <summary>
/// Buffered, append-only file writer with atomic header updates.
/// Header (first line) format: "{page},{rows}" for crash recovery.
/// Supports atomic header updates via temp file and rename.
/// </summary>
public class BufferedFileWriter : IOutputWriter
{
    private readonly string _filePath;
    private readonly string _fileId;
    private readonly ILogger _logger;
    private readonly object _lock = new();
    private bool _disposed;
    private int _currentPage;
    private long _currentRows;
    private bool _headerWritten;
    private readonly TimeSpan _lockStaleThreshold;

    public BufferedFileWriter(string filePath, string fileId, ILogger logger)
    {
        _filePath = filePath;
        _fileId = fileId;
        _logger = logger;

        // Ensure directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        // Configurable stale threshold for lock files (seconds)
        var env = Environment.GetEnvironmentVariable("FILE_LOCK_STALE_SECONDS");
        if (int.TryParse(env, out var secs) && secs > 0)
            _lockStaleThreshold = TimeSpan.FromSeconds(secs);
        else
            _lockStaleThreshold = TimeSpan.FromMinutes(5);
    }

    public async Task WriteHeaderAsync(int page, long rows, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var header = $"{page},{rows}\n";
        var tempPath = _filePath + ".tmp";

        using (await AcquireLockAsync(ct))
        {
            try
            {
                // Read existing content if file exists. If first line looks like a header (digits,digits), skip it.
                var existingLines = new List<string>();
                if (File.Exists(_filePath))
                {
                    var all = File.ReadAllLines(_filePath, Encoding.UTF8).ToList();
                    if (all.Count > 0 && IsHeaderLine(all[0]))
                    {
                        existingLines = all.Skip(1).ToList();
                    }
                    else
                    {
                        existingLines = all;
                    }
                }

                // Write header + existing content to temp file
                using (var writer = new StreamWriter(new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.Read)))
                {
                    writer.Write(header);
                    foreach (var line in existingLines)
                    {
                        writer.WriteLine(line);
                    }
                }

                // Replace target atomically when possible
                if (File.Exists(_filePath))
                {
                    // Try Replace which is atomic on many platforms; fallback to delete/move
                    try
                    {
                        File.Replace(tempPath, _filePath, null);
                    }
                    catch
                    {
                        if (File.Exists(_filePath)) File.Delete(_filePath);
                        File.Move(tempPath, _filePath);
                    }
                }
                else
                {
                    File.Move(tempPath, _filePath);
                }

                _currentPage = page;
                _currentRows = rows;
                _headerWritten = true;

                _logger.LogDebug("Header written for {FileId}: page={Page}, rows={Rows}", _fileId, page, rows);
            }
            catch (Exception ex)
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
                _logger.LogError(ex, "Error writing header for {FileId}", _fileId);
                throw;
            }
        }
    }

    public async Task AppendLinesAsync(IEnumerable<string> lines, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        // Open, append, and close per call to avoid holding OS file handle across process lifetime
        using (await AcquireLockAsync(ct))
        {
            try
            {
                // Use permissive sharing so other processes can read/delete if needed during takeover
                using var fs = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                using var writer = new StreamWriter(fs, Encoding.UTF8, bufferSize: 65536);
                foreach (var line in lines)
                {
                    // Normalize incoming lines: strip trailing newline characters so WriteLineAsync adds one clean newline
                    var text = line?.TrimEnd('\r', '\n') ?? string.Empty;
                    await writer.WriteLineAsync(text);
                }
                await writer.FlushAsync();

                _logger.LogDebug("Lines appended to {FileId}", _fileId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error appending lines to {FileId}", _fileId);
                throw;
            }
        }
    }

    public async Task RemoveHeaderAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        using (await AcquireLockAsync(ct))
        {
            try
            {
                if (!File.Exists(_filePath))
                    return;

                var lines = File.ReadAllLines(_filePath, Encoding.UTF8);
                if (lines.Length <= 1)
                {
                    // Only header, delete file
                    File.Delete(_filePath);
                }
                else
                {
                    // Write all lines except first (header)
                    var tempPath = _filePath + ".tmp";
                    File.WriteAllLines(tempPath, lines.Skip(1), Encoding.UTF8);

                    try
                    {
                        File.Replace(tempPath, _filePath, null);
                    }
                    catch
                    {
                        if (File.Exists(_filePath)) File.Delete(_filePath);
                        File.Move(tempPath, _filePath);
                    }
                }

                _headerWritten = false;
                _logger.LogDebug("Header removed from {FileId}", _fileId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing header from {FileId}", _fileId);
                throw;
            }
        }
    }

    public async Task CloseAsync(CancellationToken ct = default)
    {
        // Nothing to close since streams are opened per-call. Keep for compatibility.
        _logger.LogInformation("File closed for {FileId}", _fileId);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        
        await CloseAsync();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BufferedFileWriter));
    }

    /// <summary>
    /// Read the current header to get resume point.
    /// Returns (page, rows) or (0, 0) if no header.
    /// </summary>
    public static (int page, long rows) ReadHeader(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return (0, 0);

            var firstLine = File.ReadLines(filePath).FirstOrDefault();
            if (string.IsNullOrEmpty(firstLine))
                return (0, 0);

            var parts = firstLine.Split(',');
            if (parts.Length == 2 && int.TryParse(parts[0], out var page) && long.TryParse(parts[1], out var rows))
                return (page, rows);

            return (0, 0);
        }
        catch
        {
            return (0, 0);
        }
    }

    private string GetLockPath() => _filePath + ".lock";

    private async Task<IDisposable> AcquireLockAsync(CancellationToken ct)
    {
        var lockPath = GetLockPath();
        var start = DateTime.UtcNow;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Try to create lock file exclusively
                using var fs = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var content = System.Text.Json.JsonSerializer.Serialize(new { pid = System.Diagnostics.Process.GetCurrentProcess().Id, ts = DateTime.UtcNow });
                var bytes = Encoding.UTF8.GetBytes(content);
                await fs.WriteAsync(bytes, 0, bytes.Length, ct);
                await fs.FlushAsync(ct);
                // We created the lock file; return a releaser that deletes it on dispose
                return new LockReleaser(lockPath, _logger);
            }
            catch (IOException)
            {
                // lock file exists; check if stale
                try
                {
                    if (!File.Exists(lockPath))
                    {
                        await Task.Delay(100, ct);
                        continue;
                    }

                    var created = File.GetCreationTimeUtc(lockPath);
                    if (DateTime.UtcNow - created > _lockStaleThreshold)
                    {
                        try { File.Delete(lockPath); }
                        catch { }
                        // try again immediately
                        continue;
                    }
                }
                catch
                {
                    // ignore and retry
                }

                // If we've been trying for a while, return a no-op releaser to avoid indefinite blocking
                if (DateTime.UtcNow - start > TimeSpan.FromSeconds(10))
                {
                    _logger.LogWarning("Could not acquire file lock for {FileId} after waiting; proceeding without exclusive lock", _fileId);
                    return new LockReleaser(null, _logger, ownsLock: false);
                }

                await Task.Delay(200, ct);
            }
        }
    }

    private class LockReleaser : IDisposable
    {
        private readonly string? _lockPath;
        private readonly ILogger _logger;
        private readonly bool _ownsLock;

        public LockReleaser(string? lockPath, ILogger logger, bool ownsLock = true)
        {
            _lockPath = lockPath;
            _logger = logger;
            _ownsLock = ownsLock && lockPath != null;
        }

        public void Dispose()
        {
            if (!_ownsLock || string.IsNullOrEmpty(_lockPath)) return;
            try
            {
                if (File.Exists(_lockPath)) File.Delete(_lockPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete lock file {LockPath}", _lockPath);
            }
        }
    }

    private static bool IsHeaderLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        var parts = line.Split(',');
        if (parts.Length != 2) return false;
        return int.TryParse(parts[0], out _) && long.TryParse(parts[1], out _);
    }
}

public class BufferedFileWriterFactory : IOutputWriterFactory
{
    private readonly ILogger<BufferedFileWriterFactory> _logger;

    public BufferedFileWriterFactory(ILogger<BufferedFileWriterFactory> logger)
    {
        _logger = logger;
    }

    public IOutputWriter CreateWriter(string filePath, string fileId)
    {
        return new BufferedFileWriter(filePath, fileId, _logger);
    }
}
