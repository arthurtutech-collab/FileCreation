using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using FileGenPackage.Abstractions;
using Dapper;

namespace FileGenPackage.Infrastructure;

/// <summary>
/// SQL page reader with stable ordering for consistent pagination and crash-resume.
/// Uses configurable page size and stable ORDER BY for deterministic behavior.
/// </summary>
public class SqlPageReader : IPageReader
{
    private readonly string _connectionString;
    private readonly string _viewName;
    private readonly string _orderBy;
    private readonly int _pageSize;
    private readonly ILogger<SqlPageReader> _logger;

    public SqlPageReader(
        string connectionString,
        string viewName,
        string orderBy,
        int pageSize,
        ILogger<SqlPageReader> logger)
    {
        _connectionString = connectionString;
        _viewName = viewName;
        _orderBy = orderBy;
        _pageSize = pageSize;
        _logger = logger;
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadPageAsync(int pageNumber, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            var offset = pageNumber * _pageSize;
            var sql = $@"
                SELECT * FROM [{_viewName}]
                ORDER BY {_orderBy}
                OFFSET @Offset ROWS
                FETCH NEXT @PageSize ROWS ONLY";

            var cmdDef = new CommandDefinition(sql, new { Offset = offset, PageSize = _pageSize }, commandTimeout: 300, cancellationToken: ct);
            var rows = await conn.QueryAsync(cmdDef);

            var result = new List<IReadOnlyDictionary<string, object?>>();
            foreach (var row in rows)
            {
                if (row is IDictionary<string, object> dict)
                {
                    var map = new Dictionary<string, object?>();
                    foreach (var kv in dict)
                    {
                        map[kv.Key] = kv.Value;
                    }
                    result.Add(map);
                }
                else
                {
                    // Fallback: convert via reflection
                    var map = new Dictionary<string, object?>();
                    foreach (var prop in row.GetType().GetProperties())
                    {
                        map[prop.Name] = prop.GetValue(row);
                    }
                    result.Add(map);
                }
            }

            _logger.LogDebug("Read {Count} rows from page {Page}", result.Count, pageNumber);
            return result.AsReadOnly();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading page {Page} from {ViewName}", pageNumber, _viewName);
            throw;
        }
    }

    public async Task<long> GetTotalRowCountAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            var sql = $"SELECT COUNT(*) FROM [{_viewName}]";
            var cmdDef = new CommandDefinition(sql, commandTimeout: 300, cancellationToken: ct);
            var scalar = await conn.ExecuteScalarAsync(cmdDef);
            var count = scalar is long l ? l : Convert.ToInt64(scalar);
            _logger.LogInformation("Total row count from {ViewName}: {Count}", _viewName, count);
            return count;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting row count from {ViewName}", _viewName);
            throw;
        }
    }
}
