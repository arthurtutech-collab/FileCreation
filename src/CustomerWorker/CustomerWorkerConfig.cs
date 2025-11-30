using FileGenPackage;
using FileGenPackage.Abstractions;

namespace CustomerWorker;

public class CustomerWorkerConfig : IWorkerConfig
{
    public string WorkerId => "CustomerWorker";

    public KafkaConfig Kafka => new()
    {
        Topic = "customer.files.output",
        EventType = "CustomerDataReady",
        ConsumerGroup = "customer-worker-group",
        BootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BROKERS") ?? "localhost:9092",
        TimeoutMs = 5000
    };

    public SqlConfig Sql => new()
    {
        ViewName = "[dbo].[v_CustomerData]",
        OrderBy = "[CustomerId] ASC",
        KeyColumn = "[CustomerId]",
        PageSize = 5000,
        ConnectionString = Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING")
            ?? "Server=localhost;Database=CustomerDB;Integrated Security=true;Encrypt=false;"
    };

    public IReadOnlyList<TargetFileConfig> Files => new[]
    {
        new TargetFileConfig
        {
            FileId = "CFM",
            FileNamePattern = "CFM_{date}.txt",
            TranslatorId = "CFMTranslator"
        },
        new TargetFileConfig
        {
            FileId = "CFK",
            FileNamePattern = "CFK_{date}.txt",
            TranslatorId = "CFKTranslator"
        }
    };

    public MongoConfig Mongo => new()
    {
        ConnectionString = Environment.GetEnvironmentVariable("MONGO_CONNECTION_STRING")
            ?? "mongodb://localhost:27017",
        Database = "FileGeneration",
        StatusCollection = "file_status",
        LeaseCollection = "worker_leases"
    };

    public PathsConfig Paths => new()
    {
        OutputRootPath = Environment.GetEnvironmentVariable("OUTPUT_ROOT_PATH") ?? "/data/output"
    };

    public PoliciesConfig Policies => new()
    {
        DailyTriggerWindow = TimeSpan.FromHours(24),
        MaxRetries = 3,
        InitialBackoff = TimeSpan.FromSeconds(1),
        BackoffMultiplier = 2.0,
        LeaseHeartbeatInterval = TimeSpan.FromSeconds(30),
        LeaseTtl = TimeSpan.FromMinutes(2),
        TakeoverPollingInterval = TimeSpan.FromSeconds(15)
    };
}

public class CFMTranslator : ITranslator
{
    public string TranslateRow(IReadOnlyDictionary<string, object?> row)
    {
        // Customer Fixed Mapping format
        var fields = new[]
        {
            row.TryGetValue("CustomerId", out var custId) ? custId?.ToString() ?? "" : "",
            row.TryGetValue("Name", out var name) ? name?.ToString() ?? "" : "",
            row.TryGetValue("Email", out var email) ? email?.ToString() ?? "" : "",
            row.TryGetValue("CreatedDate", out var created) ? ((DateTime?)created)?.ToString("yyyy-MM-dd") ?? "" : ""
        };
        return string.Join("|", fields);
    }
}

public class CFKTranslator : ITranslator
{
    public string TranslateRow(IReadOnlyDictionary<string, object?> row)
    {
        // Customer Format Kafka (JSON-like) format
        return System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            { "id", row.TryGetValue("CustomerId", out var custId) ? custId : null },
            { "status", row.TryGetValue("Status", out var status) ? status : null },
            { "tier", row.TryGetValue("CustomerTier", out var tier) ? tier : null },
            { "balance", row.TryGetValue("AccountBalance", out var balance) ? balance : null }
        });
    }
}
