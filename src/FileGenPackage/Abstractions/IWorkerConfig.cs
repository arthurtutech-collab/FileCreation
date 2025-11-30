namespace FileGenPackage.Abstractions;

/// <summary>
/// Worker configuration defining SQL source, Kafka trigger, output files, and storage.
/// </summary>
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

public class KafkaConfig
{
    public required string Topic { get; set; }
    public required string EventType { get; set; }
    public required string ConsumerGroup { get; set; }
    public required string BootstrapServers { get; set; }
    public int TimeoutMs { get; set; } = 5000;
}

public class SqlConfig
{
    public required string ViewName { get; set; }
    public required string OrderBy { get; set; }
    public required string KeyColumn { get; set; }
    public int PageSize { get; set; } = 10000;
    public required string ConnectionString { get; set; }
}

public class TargetFileConfig
{
    public required string FileId { get; set; }
    public required string FileNamePattern { get; set; }
    public required string TranslatorId { get; set; }
}

public class MongoConfig
{
    public required string ConnectionString { get; set; }
    public required string Database { get; set; }
    public required string StatusCollection { get; set; }
    public required string LeaseCollection { get; set; }
}

public class PathsConfig
{
    public required string OutputRootPath { get; set; }
}

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
