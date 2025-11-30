using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using FileGenPackage.Abstractions;
using FileGenPackage.Infrastructure;
using FileGenPackage.Core;

namespace FileGenPackage;

/// <summary>
/// Extension methods for configuring the file generation package in DI container.
/// </summary>
public static class FileGenerationServiceCollectionExtensions
{
    public static IServiceCollection AddFileGenerationPackage(
        this IServiceCollection services,
        IWorkerConfig workerConfig,
        Action<ITranslatorRegistry>? configureTranslators = null,
        bool registerHostedService = true)
    {
        // Configuration
        services.AddSingleton(workerConfig);
        services.AddSingleton(workerConfig.Kafka);
        services.AddSingleton(workerConfig.Mongo);
        services.AddSingleton(workerConfig.Sql);
        services.AddSingleton(workerConfig.Paths);
        services.AddSingleton(workerConfig.Policies);

        // MongoDB client
        services.AddSingleton<IMongoClient>(sp =>
        {
            return new MongoClient(workerConfig.Mongo.ConnectionString);
        });

        // Stores
        services.AddSingleton<ILeaseStore, MongoLeaseStore>();
        services.AddSingleton<IProgressStore, MongoProgressStore>();

        // Page reader and writer
        services.AddSingleton<IPageReader>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<SqlPageReader>>();
            return new SqlPageReader(
                workerConfig.Sql.ConnectionString,
                workerConfig.Sql.ViewName,
                workerConfig.Sql.OrderBy,
                workerConfig.Sql.PageSize,
                logger);
        });

        services.AddSingleton<IOutputWriterFactory, BufferedFileWriterFactory>();

        // Kafka
        services.AddSingleton<IEventPublisher, KafkaEventPublisher>();

        // Trigger guard
        services.AddSingleton<IDailyTriggerGuard, MongoDBDailyTriggerGuard>();

        // Translator registry
        services.AddSingleton<ITranslatorRegistry>(sp =>
        {
            var registry = new TranslatorRegistry();
            configureTranslators?.Invoke(registry);
            return registry;
        });

        // Orchestrator (reusable)
        services.AddSingleton<FileGenerationOrchestrator>();

        // Main hosted service (optional)
        if (registerHostedService)
        {
            services.AddHostedService<FileGenerationHostedService>();
        }

        // Health checks
        services.AddHealthChecks()
            .AddCheck<ReadinessHealthCheck>("readiness")
            .AddCheck<LivenessHealthCheck>("liveness");

        return services;
    }
}
