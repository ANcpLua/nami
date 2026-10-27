import "./configuration.tsp";
using Nami.Api.Schema.Emit.CSharp;
using Nami.Models.Configuration;

namespace Nami.Models.Configuration.Hosting {
  @csharpForm("internal sealed partial class")
  model CollectorPortOptions {
    @configuredBy(CollectorConfigurationKeys.bindAddress)
    bindAddress: string = "127.0.0.1";
    @configuredBy(CollectorConfigurationKeys.otlpPort) @minValue(0) @maxValue(65535)
    otlpHttp: int32 = 4318;
    @configuredBy(CollectorConfigurationKeys.grpcPort) @minValue(0) @maxValue(65535)
    grpc: int32 = 4317;
  }
  @csharpForm("internal sealed partial class")
  model CollectorStorageOptions {
    @configuredBy(CollectorConfigurationKeys.dataPath)
    dataPath: string = "nami.duckdb";
    @configuredBy(CollectorConfigurationKeys.databaseMemoryLimit)
    memoryLimit?: string;
    @configuredBy(CollectorConfigurationKeys.databaseThreads) @minValue(1) @maxValue(2147483647)
    threads?: int32;
    @configuredBy(CollectorConfigurationKeys.databaseTempDirectory)
    tempDirectory?: string;
  }
}
namespace Nami.Models.Configuration.Retention {
  @csharpForm("internal sealed partial class")
  model RetentionOptions {
    @configuredBy(CollectorConfigurationKeys.retentionDays) @minValue(0) @maxValue(2147483647)
    days: int32 = 30;
    @configuredBy(CollectorConfigurationKeys.retentionIntervalMinutes) @minValue(1) @maxValue(71582)
    intervalMinutes: int32 = 60;
    @configuredBy(CollectorConfigurationKeys.storageMinimumFreeMegabytes) @minValue(0) @maxValue(8796093022207)
    storageMinimumFreeMegabytes: int64 = 2048;
  }
}
namespace Nami.Models.Configuration.Cors {
  @csharpForm("internal sealed partial class")
  model OtlpCorsOptions {
    @configuredBy(CollectorConfigurationKeys.corsAllowedOrigins)
    allowedOrigins?: string;
    @configuredBy(CollectorConfigurationKeys.corsAllowedHeaders)
    allowedHeaders?: string;
  }
}
namespace Nami.Models.Configuration.Auth {
  @csharpEnum("int", true)
  enum ApiKeyRoles { none: 0, ingest: 1, read: 2 }
  @csharpForm("internal sealed record")
  model ProjectApiKey { projectId: string; apiKey: string; roles: ApiKeyRoles; }
  @csharpForm("internal sealed partial class")
  model OtlpApiKeyOptions {
    @configuredBy(CollectorConfigurationKeys.authMode)
    authMode: string = "Unsecured";
    @configuredBy(CollectorConfigurationKeys.projectKeys)
    projectKeys?: string;
    @csharpSetter("internal set")
    keys: ProjectApiKey[] = #[];
  }
}
