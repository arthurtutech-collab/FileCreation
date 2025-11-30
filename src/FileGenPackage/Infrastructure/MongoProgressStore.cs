using MongoDB.Driver;
using Microsoft.Extensions.Logging;
using FileGenPackage.Abstractions;

namespace FileGenPackage.Infrastructure;

/// <summary>
/// MongoDB-based progress store for file generation status and pagination tracking.
/// Maintains per-file status transitions (start → in-progress → completed) and page/row progress.
/// </summary>
public class MongoProgressStore : IProgressStore
{
    private readonly IMongoCollection<FileProgressBson> _collection;
    private readonly ILogger<MongoProgressStore> _logger;

    public MongoProgressStore(IMongoClient mongoClient, MongoConfig config, ILogger<MongoProgressStore> logger)
    {
        _logger = logger;
        var db = mongoClient.GetDatabase(config.Database);
        _collection = db.GetCollection<FileProgressBson>(config.StatusCollection);

        // Ensure indexes
        var indexModels = new[]
        {
            new CreateIndexModel<FileProgressBson>(
                Builders<FileProgressBson>.IndexKeys.Ascending(x => x.FileId)),
            new CreateIndexModel<FileProgressBson>(
                Builders<FileProgressBson>.IndexKeys.Ascending(x => x.WorkerId))
        };
        _collection.Indexes.CreateMany(indexModels);
    }

    public async Task SetStartAsync(string fileId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var filter = Builders<FileProgressBson>.Filter.Eq(x => x.FileId, fileId);
        var update = Builders<FileProgressBson>.Update
            .Set(x => x.Status, FileStatus.StatusStart)
            .Set(x => x.StartedAt, now)
            .SetOnInsert(x => x.FileId, fileId)
            .SetOnInsert(x => x.WorkerId, "unknown");

        try
        {
            await _collection.UpdateOneAsync(
                filter,
                update,
                new UpdateOptions { IsUpsert = true },
                ct);
            _logger.LogInformation("File {FileId} status set to StatusStart", fileId);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting status start for {FileId}", fileId);
            throw;
        }
    }

    public async Task UpsertProgressAsync(string fileId, int page, long rows, CancellationToken ct = default)
    {
        var filter = Builders<FileProgressBson>.Filter.Eq(x => x.FileId, fileId);
        var update = Builders<FileProgressBson>.Update
            .Set(x => x.LastPage, page)
            .Set(x => x.CumulativeRows, rows)
            .Set(x => x.Status, FileStatus.InProgress)
            .SetOnInsert(x => x.FileId, fileId)
            .SetOnInsert(x => x.WorkerId, "unknown")
            .SetOnInsert(x => x.StartedAt, DateTime.UtcNow);

        try
        {
            await _collection.UpdateOneAsync(
                filter,
                update,
                new UpdateOptions { IsUpsert = true },
                ct);
            _logger.LogDebug("Progress updated for {FileId}: page={Page}, rows={Rows}", fileId, page, rows);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upserting progress for {FileId}", fileId);
            throw;
        }
    }

    public async Task SetCompletedAsync(string fileId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var filter = Builders<FileProgressBson>.Filter.Eq(x => x.FileId, fileId);
        var update = Builders<FileProgressBson>.Update
            .Set(x => x.Status, FileStatus.Completed)
            .Set(x => x.CompletedAt, now);

        try
        {
            await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
            _logger.LogInformation("File {FileId} status set to Completed", fileId);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting status completed for {FileId}", fileId);
            throw;
        }
    }

    public async Task<FileProgress?> GetAsync(string fileId, CancellationToken ct = default)
    {
        try
        {
            var doc = await _collection.Find(x => x.FileId == fileId).FirstOrDefaultAsync(ct);
            return doc == null ? null : ToBson(doc);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting progress for {FileId}", fileId);
            throw;
        }
    }

    public async Task<IReadOnlyList<FileProgress>> ListByWorkerAsync(string workerId, CancellationToken ct = default)
    {
        try
        {
            var docs = await _collection
                .Find(x => x.WorkerId == workerId)
                .ToListAsync(ct);
            
            return docs.Select(ToBson).ToList().AsReadOnly();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing progress for {WorkerId}", workerId);
            throw;
        }
    }

    public async Task<int> GetMinOutstandingPageAsync(string workerId, CancellationToken ct = default)
    {
        try
        {
            var files = await ListByWorkerAsync(workerId, ct);
            
            if (!files.Any())
                return 0;

            // Files not completed have outstanding pages
            var incompleteFies = files.Where(f => f.Status != FileStatus.Completed).ToList();
            
            if (!incompleteFies.Any())
                return 0;

            return incompleteFies.Min(f => f.LastPage);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting min outstanding page for {WorkerId}", workerId);
            throw;
        }
    }

    private static FileProgress ToBson(FileProgressBson doc) => new()
    {
        FileId = doc.FileId,
        WorkerId = doc.WorkerId,
        Status = doc.Status,
        LastPage = doc.LastPage,
        CumulativeRows = doc.CumulativeRows,
        StartedAt = doc.StartedAt,
        CompletedAt = doc.CompletedAt
    };
}

internal class FileProgressBson
{
    public required string FileId { get; set; }
    public required string WorkerId { get; set; }
    public FileStatus Status { get; set; }
    public int LastPage { get; set; }
    public long CumulativeRows { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
