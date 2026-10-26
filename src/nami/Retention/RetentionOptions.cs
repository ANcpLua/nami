using Microsoft.Extensions.Options;
using Nami.Collector.Ingestion.Configuration;

namespace Nami.Retention;

internal sealed class RetentionOptions
{
    private const long BytesPerMegabyte = 1024L * 1024L;

    [ConfigurationKeyName(CollectorConfigurationKeys.RetentionDays)]
    [ConfiguredRange(Key = CollectorConfigurationKeys.RetentionDays, Minimum = 0, Maximum = int.MaxValue)]
    public int Days { get; set; } = 30;

    [ConfigurationKeyName(CollectorConfigurationKeys.RetentionIntervalMinutes)]
    [ConfiguredRange(Key = CollectorConfigurationKeys.RetentionIntervalMinutes, Minimum = 1,
        Maximum = (uint.MaxValue - 1) / 60_000)]
    public int IntervalMinutes { get; set; } = 60;

    [ConfigurationKeyName(CollectorConfigurationKeys.StorageMinimumFreeMegabytes)]
    [ConfiguredRange(Key = CollectorConfigurationKeys.StorageMinimumFreeMegabytes, Minimum = 0,
        Maximum = long.MaxValue / BytesPerMegabyte)]
    public long StorageMinimumFreeMegabytes { get; set; } = 2048;

    public TimeSpan Interval => TimeSpan.FromMinutes(IntervalMinutes);

    public long StorageMinimumFreeBytes => StorageMinimumFreeMegabytes * BytesPerMegabyte;

    public bool IsEnabled => Days > 0;
}

[OptionsValidator]
internal sealed partial class RetentionOptionsValidator : IValidateOptions<RetentionOptions>;
