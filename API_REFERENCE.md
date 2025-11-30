# API Reference - File Generation Package

## Core Interfaces

### IWorkerConfig
Configuration interface providing all settings for a worker instance.

```csharp
public interface IWorkerConfig
{
    string WorkerId { get; }
    KafkaConfig Kafka { get; }
    SqlConfig Sql { get; }
    IReadOnlyList<TargetFileConfig> Files { get; }
    MongoConfig Mongo { get; }
    PathsConfig Paths { get; }
    PoliciesConfig Policies { get; }
}
```

**Implementation Example**:
```csharp
public class LoanWorkerConfig : IWorkerConfig
{
    public string WorkerId => "LoanWorker";
    public KafkaConfig Kafka => new() { Topic = "loan.files.output", ... };
    public SqlConfig Sql => new() { ViewName = "[v_LoanData]", ... };
    // ... other properties
}
```

---

### ITranslator
Translates database rows to output format.

```csharp
public interface ITranslator
{
    /// <summary>Translate a single row to output string.</summary>
    string TranslateRow(IReadOnlyDictionary<string, object?> row);

    /// <summary>Translate multiple rows. Default uses TranslateRow per row.</summary>
    IEnumerable<string> TranslateBatch(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        return rows.Select(TranslateRow);
    }
}
```

**Implementation Example**:
```csharp
public class Loan0Translator : ITranslator
{
    public string TranslateRow(IReadOnlyDictionary<string, object?> row)
    {
        return $"{row["LoanId"]}|{row["CustomerId"]}|{row["Amount"]}";
    }
}
```

---

### ILeaseStore
MongoDB-based leadership coordination with TTL expiry.

```csharp
public interface ILeaseStore
{
    /// <summary>Attempt to acquire exclusive lease. Returns true if acquired.</summary>
    Task<bool> TryAcquireAsync(string workerId, string instanceId, TimeSpan ttl, 
        CancellationToken ct = default);

    /// <summary>Renew active lease. Returns false if lease expired/lost.</summary>
    Task<bool> RenewAsync(string workerId, string instanceId, TimeSpan ttl, 
        CancellationToken ct = default);

    /// <summary>Release the held lease.</summary>
    Task ReleaseAsync(string workerId, string instanceId, CancellationToken ct = default);

    /// <summary>Check if lease is expired or unheld (for takeover detection).</summary>
    Task<bool> IsExpiredOrUnheldAsync(string workerId, CancellationToken ct = default);

    /// <summary>Get current lease holder for diagnostics.</summary>
    Task<LeaseInfo?> GetLeaseAsync(string workerId, CancellationToken ct = default);
}
```

**Usage**:
```csharp
// Acquire leadership
var acquired = await leaseStore.TryAcquireAsync("LoanWorker", "pod-123", TimeSpan.FromMinutes(2));

if (acquired)
{
    // Renew every 30 seconds
    var renewed = await leaseStore.RenewAsync("LoanWorker", "pod-123", TimeSpan.FromMinutes(2));
}
```

---

### IProgressStore
File-level status and pagination progress tracking.

```csharp
public interface IProgressStore
{
    /// <summary>Mark file processing as started.</summary>
    Task SetStartAsync(string fileId, CancellationToken ct = default);

    /// <summary>Update progress for current page and rows. Idempotent.</summary>
    Task UpsertProgressAsync(string fileId, int page, long rows, CancellationToken ct = default);

    /// <summary>Mark file processing as completed.</summary>
    Task SetCompletedAsync(string fileId, CancellationToken ct = default);

    /// <summary>Get current progress for a file. Returns null if not started.</summary>
    Task<FileProgress?> GetAsync(string fileId, CancellationToken ct = default);

    /// <summary>List all files for a worker.</summary>
    Task<IReadOnlyList<FileProgress>> ListByWorkerAsync(string workerId, CancellationToken ct = default);

    /// <summary>Get minimum outstanding page across all files (for resume point).</summary>
    Task<int> GetMinOutstandingPageAsync(string workerId, CancellationToken ct = default);
}

public class FileProgress
{
    public string FileId { get; set; }
    public string WorkerId { get; set; }
    public FileStatus Status { get; set; }  // StatusStart, InProgress, Completed
    public int LastPage { get; set; }
    public long CumulativeRows { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
```

**Usage**:
```csharp
// Start processing
await progressStore.SetStartAsync("Loan0");

// Update after each page
await progressStore.UpsertProgressAsync("Loan0", page: 5, rows: 50000);

// Mark as done
await progressStore.SetCompletedAsync("Loan0");

// Get current state
var progress = await progressStore.GetAsync("Loan0");
```

---

### IPageReader
SQL pagination with stable ordering.

```csharp
public interface IPageReader
{
    /// <summary>Read a page of rows with stable ordering.</summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadPageAsync(
        int pageNumber, 
        CancellationToken ct = default);

    /// <summary>Get total row count for progress tracking.</summary>
    Task<long> GetTotalRowCountAsync(CancellationToken ct = default);
}
```

**Usage**:
```csharp
var totalRows = await pageReader.GetTotalRowCountAsync();
var totalPages = (int)Math.Ceiling((double)totalRows / 10000);

for (int page = 0; page < totalPages; page++)
{
    var rows = await pageReader.ReadPageAsync(page);
    // Process rows...
}
```

---

### IOutputWriter
Buffered file writer with atomic header updates.

```csharp
public interface IOutputWriter : IAsyncDisposable
{
    /// <summary>Write or update header with current page and rows (atomic).</summary>
    Task WriteHeaderAsync(int page, long rows, CancellationToken ct = default);

    /// <summary>Append translated lines to file.</summary>
    Task AppendLinesAsync(IEnumerable<string> lines, CancellationToken ct = default);

    /// <summary>Remove header before finalization.</summary>
    Task RemoveHeaderAsync(CancellationToken ct = default);

    /// <summary>Flush pending writes and close file safely.</summary>
    Task CloseAsync(CancellationToken ct = default);
}
```

**Usage**:
```csharp
await using var writer = writerFactory.CreateWriter("/path/to/Loan0.txt", "Loan0");

// Write header (atomic via temp-file + rename)
await writer.WriteHeaderAsync(5, 50000);

// Append data
await writer.AppendLinesAsync(new[] { "line1\n", "line2\n" });

// Close file
await writer.CloseAsync();

// On recovery, read header:
var (page, rows) = BufferedFileWriter.ReadHeader("/path/to/Loan0.txt");
// Returns: (5, 50000)
```

---

### IEventPublisher
Kafka completion event publishing.

```csharp
public interface IEventPublisher
{
    /// <summary>Publish completion event for a file.</summary>
    Task PublishCompletedAsync(string workerId, string fileId, string eventType, 
        CancellationToken ct = default);
}
```

**Usage**:
```csharp
await eventPublisher.PublishCompletedAsync("LoanWorker", "Loan0", "LoanDataReady");
```

**Event JSON**:
```json
{
  "workerId": "LoanWorker",
  "fileId": "Loan0",
  "eventType": "LoanDataReady",
  "completedAt": "2024-11-29T10:30:00Z",
  "totalRows": 1000000,
  "correlationId": "LoanWorker:Loan0:1234567890"
}
```

---

## Configuration Classes

### KafkaConfig
```csharp
public class KafkaConfig
{
    public required string Topic { get; set; }
    public required string EventType { get; set; }
    public required string ConsumerGroup { get; set; }
    public required string BootstrapServers { get; set; }
    public int TimeoutMs { get; set; } = 5000;
}
```

### SqlConfig
```csharp
public class SqlConfig
{
    public required string ViewName { get; set; }
    public required string OrderBy { get; set; }
    public required string KeyColumn { get; set; }
    public int PageSize { get; set; } = 10000;
    public required string ConnectionString { get; set; }
}
```

### TargetFileConfig
```csharp
public class TargetFileConfig
{
    public required string FileId { get; set; }
    public required string FileNamePattern { get; set; }
    public required string TranslatorId { get; set; }
}
```

### MongoConfig
```csharp
public class MongoConfig
{
    public required string ConnectionString { get; set; }
    public required string Database { get; set; }
    public required string StatusCollection { get; set; }
    public required string LeaseCollection { get; set; }
}
```

### PathsConfig
```csharp
public class PathsConfig
{
    public required string OutputRootPath { get; set; }
}
```

### PoliciesConfig
```csharp
public class PoliciesConfig
{
    public TimeSpan DailyTriggerWindow { get; set; } = TimeSpan.FromHours(24);
    public int MaxRetries { get; set; } = 3;
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(1);
    public double BackoffMultiplier { get; set; } = 2.0;
    public TimeSpan LeaseHeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan LeaseTtl { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan TakeoverPollingInterval { get; set; } = TimeSpan.FromSeconds(15);
}
```

---

## Dependency Injection

### One-Line Registration

```csharp
services.AddFileGenerationPackage(workerConfig, registry =>
{
    registry.Register("Loan0Translator", new Loan0Translator());
    registry.Register("Loan1Translator", new Loan1Translator());
    registry.Register("Loan2Translator", new Loan2Translator());
    registry.Register("Loan3Translator", new Loan3Translator());
});
```

**Automatically registers**:
- `IWorkerConfig` (singleton)
- `ILeaseStore` (MongoDB implementation)
- `IProgressStore` (MongoDB implementation)
- `IPageReader` (SQL implementation)
- `IOutputWriterFactory` (Buffered file writer)
- `IEventPublisher` (Kafka implementation)
- `IDailyTriggerGuard` (MongoDB-backed)
- `ITranslatorRegistry` (with provided translators)
- `FileGenerationHostedService` (background service)
- Health checks (readiness/liveness)

---

## Health Checks

### Readiness Probe
```csharp
// Endpoint: GET /health/ready
// Checks: MongoDB, SQL, Kafka connectivity
// Response: Healthy / Unhealthy with reason
```

### Liveness Probe
```csharp
// Endpoint: GET /health/live
// Checks: Lease renewal within SLA, recent progress
// Response: Healthy / Degraded / Unhealthy
```

---

## Example Usage

### Complete Worker Bootstrap

```csharp
using FileGenPackage;
using Serilog;

public class CustomWorkerConfig : IWorkerConfig
{
    public string WorkerId => "CustomWorker";
    // ... implement all properties
}

public class CustomTranslator : ITranslator
{
    public string TranslateRow(IReadOnlyDictionary<string, object?> row)
    {
        return $"{row["Id"]},{row["Name"]},{row["Value"]}";
    }
}

// Program.cs
var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        var config = new CustomWorkerConfig();
        services.AddFileGenerationPackage(config, registry =>
        {
            registry.Register("custom-translator", new CustomTranslator());
        });
    })
    .UseSerilog((context, loggerConfig) =>
    {
        loggerConfig
            .MinimumLevel.Information()
            .WriteTo.Console();
    })
    .Build();

await host.RunAsync();
```

---

## Error Handling

All methods are async and may throw:
- `OperationCanceledException` - If cancellation requested
- `ArgumentException` - If configuration invalid
- `MongoException` - MongoDB connection/query errors
- `SqlException` - SQL Server connection/query errors
- `IOException` - File system errors
- `InvalidOperationException` - Logic errors (e.g., translator not registered)

**Recommended pattern**:
```csharp
try
{
    await progressStore.UpsertProgressAsync(fileId, page, rows, ct);
}
catch (OperationCanceledException)
{
    // Cancellation requested - clean up and exit
    throw;
}
catch (Exception ex)
{
    logger.LogError(ex, "Error updating progress");
    throw; // Re-throw or handle based on error type
}
```

---

For complete examples, see `src/LoanWorker/` and `src/CustomerWorker/`.
