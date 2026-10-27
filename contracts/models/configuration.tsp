import "../emitters/csharp/lib/main.tsp";
using Nami.Api.Schema.Emit.CSharp;
namespace Nami.Models.Configuration;
@csharpForm("internal static class")
model CollectorConfigurationKeys {
  @environment("NAMI_BIND_ADDRESS") bindAddress?: string;
  @environment("NAMI_PORT") port?: string = "5100";
  @environment("PORT") platformPort?: string;
  @environment("NAMI_PUBLIC_URL") publicUrl?: string;
  @environment("RAILWAY_PUBLIC_DOMAIN") railwayPublicDomain?: string;
  @environment("NAMI_OTLP_PORT") otlpPort?: string;
  @environment("NAMI_GRPC_PORT") grpcPort?: string;
  @environment("NAMI_OTLP_CORS_ALLOWED_ORIGINS") corsAllowedOrigins?: string;
  @environment("NAMI_OTLP_CORS_ALLOWED_HEADERS") corsAllowedHeaders?: string;
  @environment("NAMI_OTLP_AUTH_MODE") authMode?: string;
  @environment("NAMI_OTLP_PROJECT_KEYS") projectKeys?: string;
  @environment("NAMI_RETENTION_DAYS") retentionDays?: string;
  @environment("NAMI_RETENTION_INTERVAL_MINUTES") retentionIntervalMinutes?: string;
  @environment("NAMI_STORAGE_MIN_FREE_MB") storageMinimumFreeMegabytes?: string;
  @environment("NAMI_DATA_PATH") dataPath?: string;
  @environment("NAMI_DB_MEMORY_LIMIT") databaseMemoryLimit?: string;
  @environment("NAMI_DB_THREADS") databaseThreads?: string;
  @environment("NAMI_DB_TEMP_DIR") databaseTempDirectory?: string;
}
