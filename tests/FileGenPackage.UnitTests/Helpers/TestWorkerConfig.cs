using System.Collections.Generic;
using FileGenPackage.Abstractions;

namespace FileGenPackage.UnitTests.Helpers
{
    public class TestWorkerConfig : IWorkerConfig
    {
        public string WorkerId { get; init; } = "worker1";
        public KafkaConfig Kafka { get; init; } = new KafkaConfig { Topic = "daily", EventType = "daily-trigger", ConsumerGroup = "cg1", BootstrapServers = "" };
        public SqlConfig Sql { get; init; } = new SqlConfig { ViewName = "v_test", OrderBy = "id", KeyColumn = "id", PageSize = 1, ConnectionString = "" };
        public IReadOnlyList<TargetFileConfig> Files { get; init; } = new[] {
            new TargetFileConfig { FileId = "Loan0", FileNamePattern = "Loan0_{date}.csv", TranslatorId = "t-simple" }
        };
        public MongoConfig Mongo { get; init; } = new MongoConfig { ConnectionString = "", Database = "test", StatusCollection = "status", LeaseCollection = "leases" };
        public PathsConfig Paths { get; init; } = new PathsConfig { OutputRootPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "filegen_tests") };
        public PoliciesConfig Policies { get; init; } = new PoliciesConfig { LeaseHeartbeatInterval = System.TimeSpan.FromSeconds(1), LeaseTtl = System.TimeSpan.FromSeconds(5), TakeoverPollingInterval = System.TimeSpan.FromSeconds(1) };
    }
}
