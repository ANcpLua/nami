using Microsoft.Extensions.Options;

namespace Nami.Retention;

internal sealed partial class RetentionOptions
{
    private const long BytesPerMegabyte = 1024L * 1024L;

    public TimeSpan Interval => TimeSpan.FromMinutes(IntervalMinutes);

    public long StorageMinimumFreeBytes => StorageMinimumFreeMegabytes * BytesPerMegabyte;

    public bool IsEnabled => Days > 0;
}

[OptionsValidator]
internal sealed partial class RetentionOptionsValidator : IValidateOptions<RetentionOptions>;
