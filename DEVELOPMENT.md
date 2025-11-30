# File Generation Service - Local Development Setup

## Prerequisites

- Docker and Docker Compose
- .NET 8 SDK
- Visual Studio 2022 or VS Code

## Quick Start

### 1. Start Infrastructure

```bash
docker-compose up -d
```

This starts:
- MongoDB (localhost:27017) - admin/password
- SQL Server (localhost:1433) - sa/YourStrong@Password
- Kafka (localhost:9092)

### 2. Create Sample SQL Database

```bash
# Using sqlcmd (requires SQL Server Tools)
sqlcmd -S localhost -U sa -P "YourStrong@Password" -i setup.sql
```

Or use your favorite SQL client to run:

```sql
CREATE DATABASE LoanDB;
USE LoanDB;

CREATE TABLE [dbo].[Loans] (
    [LoanId] INT PRIMARY KEY,
    [CustomerId] INT,
    [Amount] DECIMAL(10, 2),
    [StartDate] DATETIME,
    [Status] VARCHAR(50),
    [InterestRate] DECIMAL(5, 3),
    [Principal] DECIMAL(10, 2),
    [Term] INT,
    [Balance] DECIMAL(10, 2),
    [LastPaymentDate] DATETIME
);

CREATE VIEW [dbo].[v_LoanData] AS
SELECT * FROM [dbo].[Loans]
ORDER BY [LoanId];

-- Insert sample data
INSERT INTO [dbo].[Loans] VALUES
(1, 100, 50000.00, '2024-01-01', 'Active', 3.5, 50000, 60, 48000, '2024-11-15'),
(2, 101, 75000.00, '2024-02-15', 'Active', 3.7, 75000, 84, 72000, '2024-11-10');
-- ... add more sample data as needed
```

### 3. Set Environment Variables

```bash
# Windows PowerShell
$env:KAFKA_BROKERS = "localhost:9092"
$env:SQL_CONNECTION_STRING = "Server=localhost;Database=LoanDB;User Id=sa;Password=YourStrong@Password;Encrypt=false;"
$env:MONGO_CONNECTION_STRING = "mongodb://admin:password@localhost:27017"
$env:OUTPUT_ROOT_PATH = "C:\temp\filegen-output"  # Windows
# Or for Linux/macOS:
# export OUTPUT_ROOT_PATH = "/tmp/filegen-output"
```

### 4. Build and Test

```bash
cd FileCreation
dotnet build --configuration Release
dotnet test
```

### 5. Run a Worker

```bash
cd src/LoanWorker
dotnet run
```

Or in another terminal:

```bash
cd src/CustomerWorker
dotnet run
```

## Monitoring

### MongoDB

Access MongoDB directly:

```bash
mongosh -u admin -p password
use FileGeneration
db.file_status.find()
db.worker_leases.find()
```

### SQL Server

Query sample:

```bash
sqlcmd -S localhost -U sa -P "YourStrong@Password"
> SELECT COUNT(*) FROM LoanDB.dbo.Loans
```

### Kafka

Create a test topic and check messages:

```bash
docker exec filegen-kafka kafka-topics --bootstrap-server localhost:9092 --list
docker exec filegen-kafka kafka-console-consumer --bootstrap-server localhost:9092 --topic loan.files.output --from-beginning
```

## Running Tests with Testcontainers

Tests automatically start temporary MongoDB containers:

```bash
dotnet test --verbosity normal
```

## Cleanup

```bash
docker-compose down -v
```

## Troubleshooting

### MongoDB Connection Error

```bash
# Check if MongoDB is running
docker ps | grep mongodb

# View logs
docker logs filegen-mongodb
```

### SQL Server Connection Error

Check that SQL Server is running and accessible:

```bash
# From PowerShell
Test-NetConnection localhost -Port 1433
```

### Port Conflicts

If ports are already in use, edit `docker-compose.yml` to use different ports:

```yaml
ports:
  - "27018:27017"  # MongoDB on 27018 instead
```

And update connection strings accordingly.

### Slow Test Execution

First time running tests may be slow due to Testcontainers pulling images. Subsequent runs are faster.

## Architecture Notes

- **MongoDB**: Stores lease and progress documents. TTL indexes auto-expire stale leases.
- **SQL Server**: Holds loan and customer data with views for aggregation.
- **Kafka**: Triggers file generation and receives completion events.
- **Workers**: Run as .NET hosted services, compete for MongoDB lease.

## Next Steps

1. Modify `LoanWorkerConfig.cs` or `CustomerWorkerConfig.cs` to match your schema
2. Implement custom translators in `LoanWorker` or `CustomerWorker` projects
3. Configure health check endpoints in `Program.cs` (requires AspNetCore)
4. Deploy to OpenShift using provided Docker image

For production deployment, see `README.md` for Docker image examples and OCP health check configuration.
