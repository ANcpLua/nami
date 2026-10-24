using Microsoft.Extensions.Options;
using Nami.Collector.Ingestion.Configuration;
using Nami.Collector.Storage;

namespace Nami.Hosting;

internal sealed class CollectorStorageOptions
{
    [ConfigurationKeyName(CollectorConfigurationKeys.DataPath)]
    public string DataPath { get; set; } = StorageDefaults.DatabaseFileName;

    [ConfigurationKeyName(CollectorConfigurationKeys.DatabaseMemoryLimit)]
    public string? MemoryLimit { get; set; }

    [ConfigurationKeyName(CollectorConfigurationKeys.DatabaseThreads)]
    [ConfiguredRange(CollectorConfigurationKeys.DatabaseThreads, 1, int.MaxValue)]
    public int? Threads { get; set; }

    [ConfigurationKeyName(CollectorConfigurationKeys.DatabaseTempDirectory)]
    public string? TempDirectory { get; set; }
}

[OptionsValidator]
internal sealed partial class CollectorStorageOptionsValidator : IValidateOptions<CollectorStorageOptions>;
