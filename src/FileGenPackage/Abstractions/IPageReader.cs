namespace FileGenPackage.Abstractions;

/// <summary>
/// SQL pagination reader with stable ordering for consistent resume behavior.
/// </summary>
public interface IPageReader
{
    /// <summary>
    /// Read a page of rows from SQL source with stable ordering.
    /// Returns empty list if page is beyond available data.
    /// </summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadPageAsync(int pageNumber, CancellationToken ct = default);

    /// <summary>
    /// Get total row count for sizing and progress tracking.
    /// </summary>
    Task<long> GetTotalRowCountAsync(CancellationToken ct = default);
}

public class PageReaderOptions
{
    public int PageSize { get; set; } = 10000;
    public string? PageSizeOverride { get; set; }
}
