using FileGenPackage;
using FileGenPackage.Abstractions;

namespace LoanWorker;

public class LoanWorkerConfig : IWorkerConfig
{
    public string WorkerId => "LoanWorker";

    public KafkaConfig Kafka => new()
    {
        Topic = "loan.files.output",
        EventType = "LoanDataReady",
        ConsumerGroup = "loan-worker-group",
        BootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BROKERS") ?? "localhost:9092",
        TimeoutMs = 5000
    };

    public SqlConfig Sql => new()
    {
        ViewName = "[dbo].[v_LoanData]",
        OrderBy = "[LoanId] ASC",
        KeyColumn = "[LoanId]",
        PageSize = 10000,
        ConnectionString = Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING")
            ?? "Server=localhost;Database=LoanDB;Integrated Security=true;Encrypt=false;"
    };

    public IReadOnlyList<TargetFileConfig> Files => new[]
    {
        new TargetFileConfig
        {
            FileId = "Loan0",
            FileNamePattern = "Loan0_{date}.txt",
            TranslatorId = "Loan0Translator"
        },
        new TargetFileConfig
        {
            FileId = "Loan1",
            FileNamePattern = "Loan1_{date}.txt",
            TranslatorId = "Loan1Translator"
        },
        new TargetFileConfig
        {
            FileId = "Loan2",
            FileNamePattern = "Loan2_{date}.txt",
            TranslatorId = "Loan2Translator"
        },
        new TargetFileConfig
        {
            FileId = "Loan3",
            FileNamePattern = "Loan3_{date}.txt",
            TranslatorId = "Loan3Translator"
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

public class Loan0Translator : ITranslator
{
    public string TranslateRow(IReadOnlyDictionary<string, object?> row)
    {
        // Example: pipe-separated format for Loan0
        var fields = new[]
        {
            row.TryGetValue("LoanId", out var loanId) ? loanId?.ToString() ?? "" : "",
            row.TryGetValue("CustomerId", out var custId) ? custId?.ToString() ?? "" : "",
            row.TryGetValue("Amount", out var amount) ? amount?.ToString() ?? "" : "",
            row.TryGetValue("StartDate", out var startDate) ? ((DateTime?)startDate)?.ToString("yyyy-MM-dd") ?? "" : ""
        };
        return string.Join("|", fields);
    }
}

public class Loan1Translator : ITranslator
{
    public string TranslateRow(IReadOnlyDictionary<string, object?> row)
    {
        // Example: comma-separated format for Loan1
        var fields = new[]
        {
            row.TryGetValue("LoanId", out var loanId) ? loanId?.ToString() ?? "" : "",
            row.TryGetValue("Status", out var status) ? status?.ToString() ?? "" : "",
            row.TryGetValue("InterestRate", out var rate) ? rate?.ToString() ?? "" : ""
        };
        return string.Join(",", fields);
    }
}

public class Loan2Translator : ITranslator
{
    public string TranslateRow(IReadOnlyDictionary<string, object?> row)
    {
        // Example: tab-separated format for Loan2
        var fields = new[]
        {
            row.TryGetValue("LoanId", out var loanId) ? loanId?.ToString() ?? "" : "",
            row.TryGetValue("Principal", out var principal) ? principal?.ToString() ?? "" : "",
            row.TryGetValue("Term", out var term) ? term?.ToString() ?? "" : ""
        };
        return string.Join("\t", fields);
    }
}

public class Loan3Translator : ITranslator
{
    public string TranslateRow(IReadOnlyDictionary<string, object?> row)
    {
        // Example: JSON format for Loan3
        return System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            { "id", row.TryGetValue("LoanId", out var loanId) ? loanId : null },
            { "balance", row.TryGetValue("Balance", out var balance) ? balance : null },
            { "lastPayment", row.TryGetValue("LastPaymentDate", out var lastPay) ? ((DateTime?)lastPay)?.ToString("yyyy-MM-dd") : null }
        });
    }
}
