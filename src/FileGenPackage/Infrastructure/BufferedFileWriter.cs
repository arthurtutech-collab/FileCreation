using System.Text;
using Microsoft.Extensions.Logging;
using FileGenPackage.Abstractions;

namespace FileGenPackage.Infrastructure;

/// <summary>
/// Buffered, append-only file writer with atomic footer updates.
/// Footer (last line) format: "{page},{rows}" for crash recovery.
/// Supports atomic footer updates via lock file.
/// </summary>
public class BufferedFileWriter : IOutputWriter
{
    private readonly string _filePath;
    private readonly string _fileId;
    private readonly ILogger _logger;
    private bool _disposed;
    private readonly TimeSpan _lockStaleThreshold;

    public BufferedFileWriter(string filePath, string fileId, ILogger logger)
    {
        _filePath = filePath;
        _fileId = fileId;
        _logger = logger;

        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

        var env = Environment.GetEnvironmentVariable("FILE_LOCK_STALE_SECONDS");
        if (int.TryParse(env, out var secs) && secs > 0)
            _lockStaleThreshold = TimeSpan.FromSeconds(secs);
        else
            _lockStaleThreshold = TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Append a page of lines and update footer atomically.
    /// Skips if page <= current footer page.
    /// </summary>
    public async Task AppendPageAsync(int page, long rows, IEnumerable<string> lines, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        using (await AcquireLockAsync(ct))
        {
            try
            {
                var (currentPage, _) = ReadFooter(_filePath);
                if (page <= currentPage)
                {
                    _logger.LogInformation("Skipping append for {FileId}: page {Page} <= current footer {CurrentPage}", _fileId, page, currentPage);
                    return;
                }

                var sb = new StringBuilder();
                foreach (var line in lines)
                {
                    var text = line?.TrimEnd('\r', '\n') ?? string.Empty;
                    sb.AppendLine(text);
                }
                // Append footer as last line
                sb.AppendLine($"{page},{rows}");

                using var fs = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                using var writer = new StreamWriter(fs, Encoding.UTF8, bufferSize: 65536);
                await writer.WriteAsync(sb.ToString());
                await writer.FlushAsync();

                _logger.LogDebug("Page {Page} appended with {Rows} rows to {FileId}", page, rows, _fileId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error appending page to {FileId}", _fileId);
                throw;
            }
        }
    }

    /// <summary>
    /// Efficiently read footer (last line).
    /// Returns (page, rows) or (0,0) if none.
    /// </summary>
    public static (int page, long rows) ReadFooter(string filePath)
    {
        try
        {
            var footerLine = FindFooter(filePath);
            if (string.IsNullOrEmpty(footerLine)) return (0, 0);

            var parts = footerLine.Split(',');
            if (parts.Length == 2 && int.TryParse(parts[0], out var page) && long.TryParse(parts[1], out var rows))
                return (page, rows);

            return (0, 0);
        }
        catch
        {
            return (0, 0);
        }
    }

    /// <summary>
    /// Remove the footer (last line) efficiently by truncating the file.
    /// </summary>
    public async Task RemoveFooterAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        using (await AcquireLockAsync(ct))
        {
            try
            {
                if (!File.Exists(_filePath))
                    return;

                using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                if (fs.Length == 0)
                    return;

                long footerStart = FindFooterPosition(fs);
                if (footerStart <= 0)
                {
                    // No newline found → whole file is footer
                    fs.SetLength(0);
                }
                else
                {
                    fs.SetLength(footerStart);
                }

                await fs.FlushAsync(ct);
                _logger.LogDebug("Footer removed from {FileId}", _fileId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing footer from {FileId}", _fileId);
                throw;
            }
        }
    }

    public async Task CloseAsync(CancellationToken ct = default)
    {
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
    /// Shared helper: find footer line by scanning backwards.
    /// </summary>
    private static string? FindFooter(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (fs.Length == 0) return null;

        long footerStart = FindFooterPosition(fs);
        if (footerStart < 0) return null;

        fs.Seek(footerStart, SeekOrigin.Begin);
        using var sr = new StreamReader(fs, Encoding.UTF8, leaveOpen: true);
        return sr.ReadLine();
    }

    /// <summary>
    /// Find the position of the last line (footer) by scanning backwards from the end.
    /// Returns byte offset where footer starts.
    /// </summary>
    private static long FindFooterPosition(FileStream fs)
    {
        long position = fs.Length - 1;
        while (position >= 0)
        {
            fs.Seek(position, SeekOrigin.Begin);
            int b = fs.ReadByte();
            if (b == '\n')
            {
                return position + 1; // footer starts right after newline
            }
            position--;
        }
        return 0; // no newline found, footer is whole file
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
                using var fs = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var content = System.Text.Json.JsonSerializer.Serialize(new { pid = System.Diagnostics.Process.GetCurrentProcess().Id, ts = DateTime.UtcNow });
                var bytes = Encoding.UTF8.GetBytes(content);
                await fs.WriteAsync(bytes, 0, bytes.Length, ct);
                await fs.FlushAsync(ct);
                return new LockReleaser(lockPath, _logger);
            }
            catch (IOException)
            {
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
                        continue;
                    }
                }
                catch { }

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
