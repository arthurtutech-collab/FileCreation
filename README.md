# File Generation Hosted Service Package

A production-ready, resilient C# package for generating files from paginated SQL data triggered by Kafka events, with automatic leader election, crash-resume capabilities, and deployment to OpenShift (OCP).

## Objectives

- **Event-driven**: Start processing when a configurable Kafka event is consumed (once per day)
- **Single writer**: Ensure exactly one pod writes files at any time across data centers via MongoDB-based lease coordination
- **Crash-resume**: Automatically resume from the last processed page when a leader pod fails
- **Extensible**: Reuse the same package for different workers (e.g., LoanWorker, CustomerWorker) with pluggable translators
- **Observable**: Structured logging to Splunk and OCP health checks (readiness/liveness)

## Architecture

### Components

1. **FileGenPackage** - Core library providing:
   - `ILeaseStore`: MongoDB-based leadership coordination with TTL
   - `IProgressStore`: File-level status and pagination tracking
   - `IPageReader`: SQL pagination with stable ordering
   - `IOutputWriter`: Buffered file writer with atomic header updates
   - `ITranslator`: Per-file translation logic
   - `IEventPublisher`: Kafka completion event publishing
   - `FileGenerationHostedService`: Main orchestration and takeover logic
   - `ReadinessHealthCheck` / `LivenessHealthCheck`: OCP probes

2. **LoanWorker** - Example worker generating four loan files (Loan0–Loan3) with unique translators

3. **CustomerWorker** - Example worker generating customer files (CFM, CFK) with unique translators

4. **Tests** - xUnit unit tests with Testcontainers for MongoDB integration

### Takeover Flow

1. **Leader Election**: Pod acquires MongoDB lease with TTL
2. **Heartbeat**: Leader renews lease every `LeaseHeartbeatInterval`
3. **Detect Failure**: Non-leader polls MongoDB every `TakeoverPollingInterval`
4. **Resume**: If leader lease expires, new leader reads file headers and resumes from minimum outstanding page
5. **Safety**: Each page write atomically updates header before appending data

## Features

### File Generation

- **Single daily trigger**: Only processes once per day even if multiple Kafka events arrive
- **Shared page fetch**: Reads each SQL page once, translates to all target files
- **Per-file translators**: Each file has its own translation logic (pipe-separated, CSV, JSON, etc.)
- **Progress headers**: First line of each file tracks `{page},{rows}` for recovery
- **Skip duplicates**: Files beyond current page are skipped to prevent re-generation

### Resilience

- **MongoDB lease**: TTL-based expiry ensures stale leases automatically release
- **Atomic header updates**: Temp-file + rename pattern ensures crash safety
- **Configurable retries**: Exponential backoff with `MaxRetries`, `InitialBackoff`, `BackoffMultiplier`
- **Crash detection**: Heartbeat renewal failures immediately stop writing

### Deployment

- **OCP-ready**: Health checks for readiness (MongoDB/Kafka/SQL connectivity) and liveness (heartbeat + progress)
- **.NET 8**: Latest LTS runtime with modern C# features
- **Containerizable**: Example Docker compose configurations for local dev

## Project Structure

```
FileCreation/
├── src/
│   ├── FileGenPackage/           # Core package library
│   │   ├── Abstractions/         # Interfaces (IWorkerConfig, ITranslator, etc.)
│   │   ├── Infrastructure/       # MongoDB, SQL, Kafka implementations
│   │   ├── Core/                 # FileGenerationHostedService, takeover logic
│   │   └── FileGenPackage.csproj
│   ├── LoanWorker/               # Example worker for loan files
│   │   ├── LoanWorkerConfig.cs
│   │   ├── Program.cs
│   │   └── LoanWorker.csproj
│   └── CustomerWorker/           # Example worker for customer files
│       ├── CustomerWorkerConfig.cs
│       ├── Program.cs
│       └── CustomerWorker.csproj
├── tests/
│   └── FileGenPackage.UnitTests/
│       ├── MongoLeaseStoreTests.cs
│       ├── MongoProgressStoreTests.cs
│       ├── BufferedFileWriterTests.cs
│       ├── TranslatorTests.cs
│       └── FileGenPackage.UnitTests.csproj
├── .github/workflows/
│   └── build-and-test.yml        # GitHub Actions CI workflow
├── FileCreation.sln
└── README.md
```

## Configuration

All configuration comes from `IWorkerConfig` implementations (e.g., `LoanWorkerConfig`, `CustomerWorkerConfig`):

```csharp
public class LoanWorkerConfig : IWorkerConfig
{
    public string WorkerId => "LoanWorker";
    public KafkaConfig Kafka => new() { ... };
    public SqlConfig Sql => new() { ViewName = "[v_LoanData]", ... };
    public IReadOnlyList<TargetFileConfig> Files => new[] { ... };
    public MongoConfig Mongo => new() { ... };
    public PathsConfig Paths => new() { OutputRootPath = "/data/output" };
    public PoliciesConfig Policies => new() { LeaseHeartbeatInterval = ..., ... };
}
```

### Environment Variables

- `KAFKA_BROKERS`: Kafka bootstrap servers (default: `localhost:9092`)
- `SQL_CONNECTION_STRING`: SQL Server connection string
- `MONGO_CONNECTION_STRING`: MongoDB connection string (default: `mongodb://localhost:27017`)
- `OUTPUT_ROOT_PATH`: Output directory for generated files (default: `/data/output`)

## Building and Testing

### Prerequisites

- .NET 8 SDK
- Docker (for running MongoDB/SQL Server locally)
- MongoDB and SQL Server instances

### Build

```bash
cd FileCreation
dotnet build --configuration Release
```

### Tests

```bash
dotnet test --configuration Release
```

Tests use Testcontainers to spin up temporary MongoDB instances:

```bash
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "ClassName=FileGenPackage.UnitTests.MongoLeaseStoreTests"
```

### Solution

Open in Visual Studio 2022 or later:

```bash
start FileCreation.sln
```

## Running Workers Locally

### LoanWorker

```bash
cd src/LoanWorker
dotnet run
```

### CustomerWorker

```bash
cd src/CustomerWorker
dotnet run
```

## Health Checks

Workers expose two health check endpoints (when using AspNetCore):

- **Readiness**: `/health/ready` - Checks MongoDB, Kafka, SQL connectivity
- **Liveness**: `/health/live` - Checks lease renewal and recent progress

### Local testing and Kubernetes probes

- **Local test**: Workers bind the health endpoints to the port defined by the `HEALTH_PORT` environment variable (default `5000`). Run a worker and query the endpoints:

```powershell
$env:HEALTH_PORT = '8080'    # optional, defaults to 5000
dotnet run --project src/LoanWorker
# in another shell
curl http://localhost:8080/health/ready
curl http://localhost:8080/health/live
```

- **Kubernetes / OpenShift**: An example `deploy/health-probes.yaml` is included with this repository. It shows how to configure `readinessProbe` and `livenessProbe` to hit `/health/ready` and `/health/live` respectively. Update the `containerPort` and `HEALTH_PORT` env var in the manifest to match your deployment.

See `deploy/health-probes.yaml` for a copyable probe snippet you can adapt for your cluster.

## Docker Deployment

Example Dockerfile:

```dockerfile
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/LoanWorker/LoanWorker.csproj", "src/LoanWorker/"]
COPY ["src/FileGenPackage/FileGenPackage.csproj", "src/FileGenPackage/"]
RUN dotnet restore "src/LoanWorker/LoanWorker.csproj"
COPY . .
RUN dotnet build "src/LoanWorker/LoanWorker.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "src/LoanWorker/LoanWorker.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "LoanWorker.dll"]
```

## Kafka Event Format

Completion events published after file finalization:

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

## MongoDB Collections

### `file_status` Collection

Tracks progress for each file:

```json
{
  "_id": ObjectId(...),
  "fileId": "Loan0",
  "workerId": "LoanWorker",
  "status": "InProgress",
  "lastPage": 5,
  "cumulativeRows": 50000,
  "startedAt": ISODate("2024-11-29T10:00:00Z"),
  "completedAt": null
}
```

### `worker_leases` Collection

TTL-based lease records (auto-expire after TTL):

```json
{
  "_id": ObjectId(...),
  "workerId": "LoanWorker",
  "instanceId": "pod-12345",
  "acquiredAt": ISODate("2024-11-29T10:00:00Z"),
  "expiresAt": ISODate("2024-11-29T10:02:00Z")
}
```

## Example Translators

### Pipe-separated (Loan0)

```csharp
public class Loan0Translator : ITranslator
{
    public string TranslateRow(IReadOnlyDictionary<string, object?> row)
    {
        return $"{row["LoanId"]}|{row["CustomerId"]}|{row["Amount"]}|{row["StartDate"]}";
    }
}
```

### CSV (Loan1)

```csharp
public class Loan1Translator : ITranslator
{
    public string TranslateRow(IReadOnlyDictionary<string, object?> row)
    {
        return $"{row["LoanId"]},{row["Status"]},{row["InterestRate"]}";
    }
}
```

### JSON (Loan3)

```csharp
public class Loan3Translator : ITranslator
{
    public string TranslateRow(IReadOnlyDictionary<string, object?> row)
    {
        return JsonSerializer.Serialize(new { id = row["LoanId"], balance = row["Balance"] });
    }
}
```

## CI/CD

GitHub Actions workflow (`build-and-test.yml`) runs on push/PR:

1. **Build**: `dotnet build --configuration Release`
2. **Test**: `dotnet test` with Testcontainers MongoDB
3. **Security**: CodeQL analysis and optional SonarCloud
4. **Report**: Test results published as artifacts

## Observability

### Splunk-Friendly Logs

Structured logging with context:

```
[INFO] File generation service starting. WorkerId=LoanWorker InstanceId=pod-12345
[INFO] Leadership acquired
[INFO] Total rows: 1000000, total pages: 100
[INFO] Processing page 5 with 10000 rows
[INFO] Progress updated for Loan0: page=5, rows=50000
[INFO] File Loan0 finalized and event published
```

### Metrics

Optional integration with Prometheus:

- `pages_per_second`
- `rows_per_second`
- `lease_renewals_total`
- `files_completed_total`
- `time_to_completion_seconds`

## Error Handling

- **Lease Loss**: Detected via failed renewal; stops writing immediately
- **SQL Errors**: Retry with exponential backoff; log and bubble up after max retries
- **File I/O**: Atomic header updates ensure consistency even on crash
- **Kafka Publish**: Logged but doesn't block completion (fire-and-forget pattern)

## Production Checklist

- [ ] Configure environment variables for Kafka, SQL, MongoDB
- [ ] Set output path to persistent storage
- [ ] Configure lease TTL and heartbeat based on pod restart SLA
- [ ] Set up MongoDB with appropriate indexes and backup
- [ ] Set up Kafka topic with sufficient partitions for throughput
- [ ] Configure Splunk/ELK integration for log aggregation
- [ ] Test takeover scenario (simulate pod crash)
- [ ] Test resume from crash using MongoDB file status
- [ ] Load test with realistic page sizes and row counts
- [ ] Set up alerts for:
  - Lease expiry without renewal
  - File processing failures
  - Health check degradation
  - Completion event publish failures

## License

MIT

## Contributing

- Fork and create a feature branch
- Write tests for new functionality
- Ensure all tests pass: `dotnet test`
- Submit PR with description

## Support

For issues, please open a GitHub issue with:

- `.NET version` (output of `dotnet --version`)
- Steps to reproduce
- Expected vs. actual behavior
- Relevant logs
"# FileCreation" 
