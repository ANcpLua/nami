using Microsoft.Extensions.Options;
using Nami.Collector.Ingestion.Configuration;

namespace Nami.Collector.Retention;

internal sealed class RetentionOptions
{
    private const long BytesPerMegabyte = 1024L * 1024L;

    [ConfigurationKeyName(CollectorConfigurationKeys.RetentionDays)]
    [ConfiguredRange(CollectorConfigurationKeys.RetentionDays, 0, int.MaxValue)]
    public int Days { get; set; } = 30;

    [ConfigurationKeyName(CollectorConfigurationKeys.RetentionIntervalMinutes)]
    [ConfiguredRange(CollectorConfigurationKeys.RetentionIntervalMinutes, 1, int.MaxValue)]
    public int IntervalMinutes { get; set; } = 60;

    [ConfigurationKeyName(CollectorConfigurationKeys.StorageMinimumFreeMegabytes)]
    [ConfiguredRange(CollectorConfigurationKeys.StorageMinimumFreeMegabytes, 0, long.MaxValue / BytesPerMegabyte)]
    public long StorageMinimumFreeMegabytes { get; set; } = 2048;

    public TimeSpan Interval => TimeSpan.FromMinutes(IntervalMinutes);

    public long StorageMinimumFreeBytes => StorageMinimumFreeMegabytes * BytesPerMegabyte;

    public bool IsEnabled => Days > 0;
}

[OptionsValidator]
internal sealed partial class RetentionOptionsValidator : IValidateOptions<RetentionOptions>;
