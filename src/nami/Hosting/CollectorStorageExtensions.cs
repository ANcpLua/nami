using Microsoft.Extensions.Options;
using Nami.Collector.Health;
using Nami.Collector.Retention;
using Nami.Collector.Storage;

namespace Nami.Collector.Hosting;

internal static class CollectorStorageExtensions
{

    public static IServiceCollection AddNamiCollectorStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<RetentionOptions>()
            .Bind(configuration)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<RetentionOptions>, RetentionOptionsValidator>();
        services.AddSingleton(CreateStore);
        services.ActivateSingleton<INamiStore>();
        services.AddHostedService<RetentionService>();
        services.AddHealthChecks()
            .AddCheck<DuckDbHealthCheck>("duckdb", tags: ["db", "storage", NamiEndpoints.ReadyTag]);

        return services;
    }

    private static INamiStore CreateStore(IServiceProvider services)
    {
        var storage = services.GetRequiredService<IOptions<CollectorStorageOptions>>().Value;
        var dataDir = Path.GetDirectoryName(storage.DataPath);
        if (!string.IsNullOrEmpty(dataDir))
            Directory.CreateDirectory(dataDir);

        return new DuckDbStore(
            storage.DataPath,
            memoryLimit: storage.MemoryLimit,
            threads: storage.Threads,
            tempDirectory: storage.TempDirectory,
            loggerFactory: services.GetRequiredService<ILoggerFactory>());
    }
}
