namespace Nami.Collector.Ingestion.Configuration;

/// <summary>
///     The configuration keys the Collector's options bind, each also its environment variable name.
///     The options types name them in <see cref="Microsoft.Extensions.Configuration.ConfigurationKeyNameAttribute" />
///     and in their validation messages, so a key exists once.
/// </summary>
internal static class CollectorConfigurationKeys
{
    public const string BindAddress = "NAMI_BIND_ADDRESS";
    public const string Port = "NAMI_PORT";
    public const string PlatformPort = "PORT";
    // These two names are consumed by the generated TypeScript configuration table.
    public const string PublicUrl = "NAMI_PUBLIC_URL";
    public const string RailwayPublicDomain = "RAILWAY_PUBLIC_DOMAIN";
    public const string OtlpPort = "NAMI_OTLP_PORT";
    public const string GrpcPort = "NAMI_GRPC_PORT";
    public const string CorsAllowedOrigins = "NAMI_OTLP_CORS_ALLOWED_ORIGINS";
    public const string CorsAllowedHeaders = "NAMI_OTLP_CORS_ALLOWED_HEADERS";
    public const string AuthMode = "NAMI_OTLP_AUTH_MODE";
    public const string ProjectKeys = "NAMI_OTLP_PROJECT_KEYS";
    public const string RetentionDays = "NAMI_RETENTION_DAYS";
    public const string RetentionIntervalMinutes = "NAMI_RETENTION_INTERVAL_MINUTES";
    public const string StorageMinimumFreeMegabytes = "NAMI_STORAGE_MIN_FREE_MB";
    public const string DataPath = "NAMI_DATA_PATH";
    public const string DatabaseMemoryLimit = "NAMI_DB_MEMORY_LIMIT";
    public const string DatabaseThreads = "NAMI_DB_THREADS";
    public const string DatabaseTempDirectory = "NAMI_DB_TEMP_DIR";
}
