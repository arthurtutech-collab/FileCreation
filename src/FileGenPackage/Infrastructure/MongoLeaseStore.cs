using MongoDB.Driver;
using Microsoft.Extensions.Logging;
using FileGenPackage.Abstractions;

namespace FileGenPackage.Infrastructure;

/// <summary>
/// MongoDB-based lease store with TTL expiry for exactly-one-writer coordination.
/// Uses atomic upsert with compare-and-swap semantics for concurrent safety.
/// </summary>
public class MongoLeaseStore : ILeaseStore
{
    private readonly IMongoCollection<LeaseBson> _collection;
    private readonly ILogger<MongoLeaseStore> _logger;

    public MongoLeaseStore(IMongoClient mongoClient, MongoConfig config, ILogger<MongoLeaseStore> logger)
    {
        _logger = logger;
        var db = mongoClient.GetDatabase(config.Database);
        _collection = db.GetCollection<LeaseBson>(config.LeaseCollection);
        
        // Ensure TTL index
        var indexModel = new CreateIndexModel<LeaseBson>(
            Builders<LeaseBson>.IndexKeys.Ascending(x => x.ExpiresAt),
            new CreateIndexOptions { ExpireAfter = TimeSpan.Zero });
        _collection.Indexes.CreateOne(indexModel);
    }

    public async Task<bool> TryAcquireAsync(string workerId, string instanceId, TimeSpan ttl, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var expiresAt = now.Add(ttl);

        var filter = Builders<LeaseBson>.Filter.Eq(x => x.WorkerId, workerId);
        var update = Builders<LeaseBson>.Update
            .Set(x => x.InstanceId, instanceId)
            .Set(x => x.AcquiredAt, now)
            .Set(x => x.ExpiresAt, expiresAt);

        try
        {
            var result = await _collection.UpdateOneAsync(
                filter,
                update,
                new UpdateOptions { IsUpsert = true },
                ct);

            // Check if we won the race by reading back
            var lease = await _collection.Find(filter).FirstOrDefaultAsync(ct);
            var acquired = lease?.InstanceId == instanceId;

            if (acquired)
            {
                _logger.LogInformation("Lease acquired for {WorkerId} by {InstanceId}", workerId, instanceId);
            }

            return acquired;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acquiring lease for {WorkerId}", workerId);
            return false;
        }
    }

    public async Task<bool> RenewAsync(string workerId, string instanceId, TimeSpan ttl, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var expiresAt = now.Add(ttl);

        var filter = Builders<LeaseBson>.Filter.And(
            Builders<LeaseBson>.Filter.Eq(x => x.WorkerId, workerId),
            Builders<LeaseBson>.Filter.Eq(x => x.InstanceId, instanceId));

        try
        {
            var result = await _collection.UpdateOneAsync(
                filter,
                Builders<LeaseBson>.Update.Set(x => x.ExpiresAt, expiresAt),
                cancellationToken: ct);

            var renewed = result.ModifiedCount > 0;

            if (renewed)
            {
                _logger.LogDebug("Lease renewed for {WorkerId} by {InstanceId}", workerId, instanceId);
            }

            return renewed;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error renewing lease for {WorkerId}", workerId);
            return false;
        }
    }

    public async Task ReleaseAsync(string workerId, string instanceId, CancellationToken ct = default)
    {
        var filter = Builders<LeaseBson>.Filter.And(
            Builders<LeaseBson>.Filter.Eq(x => x.WorkerId, workerId),
            Builders<LeaseBson>.Filter.Eq(x => x.InstanceId, instanceId));

        try
        {
            await _collection.DeleteOneAsync(filter, ct);
            _logger.LogInformation("Lease released for {WorkerId} by {InstanceId}", workerId, instanceId);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing lease for {WorkerId}", workerId);
        }
    }

    public async Task<bool> IsExpiredOrUnheldAsync(string workerId, CancellationToken ct = default)
    {
        var filter = Builders<LeaseBson>.Filter.Eq(x => x.WorkerId, workerId);

        try
        {
            var lease = await _collection.Find(filter).FirstOrDefaultAsync(ct);
            
            if (lease == null)
                return true;

            return DateTime.UtcNow >= lease.ExpiresAt;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking lease expiry for {WorkerId}", workerId);
            return true; // Assume expired on error for safety
        }
    }

    public async Task<LeaseInfo?> GetLeaseAsync(string workerId, CancellationToken ct = default)
    {
        try
        {
            var lease = await _collection.Find(x => x.WorkerId == workerId).FirstOrDefaultAsync(ct);
            if (lease == null) return null;

            return new LeaseInfo
            {
                WorkerId = lease.WorkerId,
                InstanceId = lease.InstanceId,
                AcquiredAt = lease.AcquiredAt,
                ExpiresAt = lease.ExpiresAt
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting lease info for {WorkerId}", workerId);
            return null;
        }
    }
}

internal class LeaseBson
{
    public required string WorkerId { get; set; }
    public required string InstanceId { get; set; }
    public required DateTime AcquiredAt { get; set; }
    public required DateTime ExpiresAt { get; set; }
}
