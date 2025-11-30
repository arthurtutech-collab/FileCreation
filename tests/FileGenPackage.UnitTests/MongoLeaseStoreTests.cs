using MongoDB.Driver;
using Testcontainers.MongoDb;
using Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using FileGenPackage.Abstractions;
using FileGenPackage.Infrastructure;

namespace FileGenPackage.UnitTests;

/// <summary>
/// Integration tests for MongoLeaseStore using Testcontainers for MongoDB.
/// </summary>
public class MongoLeaseStoreTests : IAsyncLifetime
{
    private MongoDbContainer? _container;
    private IMongoClient? _mongoClient;
    private MongoLeaseStore? _leaseStore;
    private bool _skip = false;
    private readonly ILogger<MongoLeaseStore> _logger = new Mock<ILogger<MongoLeaseStore>>().Object;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new MongoDbBuilder().Build();
            await _container.StartAsync();

            var connectionString = _container.GetConnectionString();
            _mongoClient = new MongoClient(connectionString);

            var config = new MongoConfig
            {
                ConnectionString = connectionString,
                Database = "test",
                LeaseCollection = "leases",
                StatusCollection = "status"
            };

            _leaseStore = new MongoLeaseStore(_mongoClient, config, _logger);
        }
        catch (ArgumentException)
        {
            // Testcontainers throws ArgumentException when Docker isn't available/configured.
            // Mark tests to be skipped by setting _skip; individual tests will return early.
            _skip = true;
            return;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }

    [Fact]
    public async Task TryAcquireAsync_FirstInstance_ShouldAcquire()
    {
        if (_skip) return;

        // Arrange
        var workerId = "test-worker";
        var instanceId = "instance-1";
        var ttl = TimeSpan.FromMinutes(1);

        // Act
        var acquired = await _leaseStore!.TryAcquireAsync(workerId, instanceId, ttl);

        // Assert
        Assert.True(acquired);
        
        var lease = await _leaseStore.GetLeaseAsync(workerId);
        Assert.NotNull(lease);
        Assert.Equal(instanceId, lease.InstanceId);
    }

    [Fact]
    public async Task TryAcquireAsync_SecondInstance_ShouldFail()
    {
        if (_skip) return;

        // Arrange
        var workerId = "test-worker";
        var instance1 = "instance-1";
        var instance2 = "instance-2";
        var ttl = TimeSpan.FromMinutes(1);

        // Act
        var acquired1 = await _leaseStore!.TryAcquireAsync(workerId, instance1, ttl);
        var acquired2 = await _leaseStore.TryAcquireAsync(workerId, instance2, ttl);

        // Assert
        Assert.True(acquired1);
        Assert.False(acquired2);
    }

    [Fact]
    public async Task RenewAsync_ShouldExtendExpiry()
    {
        if (_skip) return;

        // Arrange
        var workerId = "test-worker";
        var instanceId = "instance-1";
        var ttl = TimeSpan.FromSeconds(1);

        await _leaseStore!.TryAcquireAsync(workerId, instanceId, ttl);

        // Act
        var renewed = await _leaseStore.RenewAsync(workerId, instanceId, TimeSpan.FromMinutes(10));

        // Assert
        Assert.True(renewed);
        
        var lease = await _leaseStore.GetLeaseAsync(workerId);
        Assert.NotNull(lease);
        Assert.True(lease.ExpiresAt > DateTime.UtcNow.AddMinutes(5));
    }

    [Fact]
    public async Task RenewAsync_WrongInstance_ShouldFail()
    {
        if (_skip) return;

        // Arrange
        var workerId = "test-worker";
        var instance1 = "instance-1";
        var instance2 = "instance-2";

        await _leaseStore!.TryAcquireAsync(workerId, instance1, TimeSpan.FromMinutes(1));

        // Act
        var renewed = await _leaseStore.RenewAsync(workerId, instance2, TimeSpan.FromMinutes(10));

        // Assert
        Assert.False(renewed);
    }

    [Fact]
    public async Task IsExpiredOrUnheldAsync_ExpiredLease_ShouldBeTrue()
    {
        if (_skip) return;

        // Arrange
        var workerId = "test-worker";
        var instanceId = "instance-1";

        await _leaseStore!.TryAcquireAsync(workerId, instanceId, TimeSpan.FromMilliseconds(100));

        // Act
        await Task.Delay(200); // Wait for expiry
        var isExpired = await _leaseStore.IsExpiredOrUnheldAsync(workerId);

        // Assert
        Assert.True(isExpired);
    }

    [Fact]
    public async Task ReleaseAsync_ShouldRemoveLease()
    {
        if (_skip) return;

        // Arrange
        var workerId = "test-worker";
        var instanceId = "instance-1";

        await _leaseStore!.TryAcquireAsync(workerId, instanceId, TimeSpan.FromMinutes(1));

        // Act
        await _leaseStore.ReleaseAsync(workerId, instanceId);
        var lease = await _leaseStore.GetLeaseAsync(workerId);

        // Assert
        Assert.Null(lease);
    }
}
