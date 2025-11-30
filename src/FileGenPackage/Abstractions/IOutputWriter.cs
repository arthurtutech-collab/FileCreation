using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FileGenPackage.Abstractions
{
    /// <summary>
    /// Abstraction for append-only file writers that support page-based appends
    /// with footer tracking for crash recovery.
    /// </summary>
    public interface IOutputWriter : IAsyncDisposable
    {
        /// <summary>
        /// Append a page of lines and update the footer atomically.
        /// Skips if the page number is not greater than the current footer page.
        /// </summary>
        /// <param name="page">Page number to append.</param>
        /// <param name="rows">Row count for this page.</param>
        /// <param name="lines">Lines of content to append before the footer.</param>
        /// <param name="ct">Optional cancellation token.</param>
        Task AppendPageAsync(int page, long rows, IEnumerable<string> lines, CancellationToken ct = default);

        /// <summary>
        /// Remove the footer (last line) from the file.
        /// If only the footer exists, deletes the file entirely.
        /// </summary>
        /// <param name="ct">Optional cancellation token.</param>
        Task RemoveFooterAsync(CancellationToken ct = default);

        /// <summary>
        /// Close the writer. Provided for compatibility; streams are opened per call.
        /// </summary>
        Task CloseAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Factory abstraction for creating output writers.
    /// </summary>
    public interface IOutputWriterFactory
    {
        /// <summary>
        /// Create a new output writer for the given file path and identifier.
        /// </summary>
        IOutputWriter CreateWriter(string filePath, string fileId);
    }
}
