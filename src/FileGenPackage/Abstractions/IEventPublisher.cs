namespace FileGenPackage.Abstractions;

/// <summary>
/// Publishes file completion events to Kafka for downstream systems.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publish completion event for a file. Called after status-completed in MongoDB.
    /// </summary>
    Task PublishCompletedAsync(string workerId, string fileId, string eventType, CancellationToken ct = default);
}

public class FileCompletedEvent
{
    public required string WorkerId { get; set; }
    public required string FileId { get; set; }
    public required string EventType { get; set; }
    public DateTime CompletedAt { get; set; }
    public long TotalRows { get; set; }
    public string? CorrelationId { get; set; }
}
