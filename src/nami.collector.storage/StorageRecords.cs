namespace Nami.Collector.Storage;

internal sealed partial record LogStorageRow
{
    internal ulong? EffectiveTimeUnixNano() => TimeUnixNano is > 0 ? TimeUnixNano :
        ObservedTimeUnixNano is > 0 ? ObservedTimeUnixNano : null;
}

internal sealed class SnapshotUnavailableException(string? message, Exception? innerException)
    : InvalidOperationException(message, innerException)
{
    public SnapshotUnavailableException()
        : this("Snapshot is unknown, expired, or belongs to another project.", null) { }

    public SnapshotUnavailableException(string? message) : this(message, null) { }
}
