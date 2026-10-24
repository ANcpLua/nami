using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nami.Health;
using Nami.Retention;
using Nami.Collector.Storage;

namespace Nami.Hosting;

internal static class CollectorStorageExtensions
{
    public static void AddNamiCollectorStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<RetentionOptions>()
            .Bind(configuration)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<RetentionOptions>, RetentionOptionsValidator>();
        services.AddSingleton(CreateStore);
        services.AddHostedService<RetentionService>();
        services.AddHealthChecks()
            .AddCheck<DuckDbHealthCheck>("duckdb", tags: ["db", "storage", "ready"]);

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
