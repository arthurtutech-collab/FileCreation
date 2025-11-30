using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using FileGenPackage.Abstractions;

namespace FileGenPackage.Infrastructure;

/// <summary>
/// Real Confluent.Kafka producer implementation for publishing file completion events.
/// Includes serialization, error handling, and delivery reports.
/// </summary>
public class KafkaEventPublisher : IEventPublisher, IAsyncDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly KafkaConfig _kafkaConfig;
    private readonly ILogger<KafkaEventPublisher> _logger;
    private bool _disposed;

    public KafkaEventPublisher(
        KafkaConfig kafkaConfig,
        ILogger<KafkaEventPublisher> logger)
    {
        _kafkaConfig = kafkaConfig;
        _logger = logger;

        var config = new ProducerConfig
        {
            BootstrapServers = kafkaConfig.BootstrapServers,
            Acks = Acks.All,
            RequestTimeoutMs = kafkaConfig.TimeoutMs,
            MessageTimeoutMs = kafkaConfig.TimeoutMs,
            CompressionType = CompressionType.Snappy
        };

        _producer = new ProducerBuilder<string, string>(config)
            .SetErrorHandler((_, error) =>
            {
                _logger.LogError("Kafka producer error: {Reason} ({Code})", error.Reason, error.Code);
            })
            .Build();

        _logger.LogInformation("Kafka event publisher initialized with brokers: {Brokers}", kafkaConfig.BootstrapServers);
    }

    public async Task PublishCompletedAsync(string workerId, string fileId, string eventType, CancellationToken ct = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(KafkaEventPublisher));

        try
        {
            var @event = new FileCompletedEvent
            {
                WorkerId = workerId,
                FileId = fileId,
                EventType = eventType,
                CompletedAt = DateTime.UtcNow,
                TotalRows = 0,
                CorrelationId = $"{workerId}:{fileId}:{DateTime.UtcNow.Ticks}"
            };

            var json = JsonSerializer.Serialize(@event);
            var key = $"{workerId}:{fileId}";

            var deliveryReport = await _producer.ProduceAsync(
                _kafkaConfig.Topic,
                new Message<string, string>
                {
                    Key = key,
                    Value = json
                },
                ct);

            if (deliveryReport.Status == PersistenceStatus.Persisted)
            {
                _logger.LogInformation(
                    "Completion event published for {FileId} on topic {Topic}. Partition={Partition}, Offset={Offset}, CorrelationId={CorrelationId}",
                    fileId,
                    _kafkaConfig.Topic,
                    deliveryReport.Partition,
                    deliveryReport.Offset,
                    @event.CorrelationId);
            }
            else
            {
                _logger.LogWarning(
                    "Completion event not persisted for {FileId}. Status={Status}",
                    fileId,
                    deliveryReport.Status);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogError(ex,
                "Error publishing completion event for {FileId}: {Reason}",
                fileId,
                ex.Error.Reason);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error publishing completion event for {FileId}", fileId);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        
        try
        {
            _producer?.Dispose();
            _logger.LogInformation("Kafka event publisher disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing Kafka event publisher");
        }

        _disposed = true;
    }
}
