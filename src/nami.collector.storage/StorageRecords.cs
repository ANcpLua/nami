namespace Nami.Collector.Storage;

internal sealed partial record SpanStorageRow
{
    [JsonIgnore] internal IReadOnlyList<AttributeRename> AttributeRenames { get; init; } = [];
}

internal sealed partial record LogStorageRow
{
    [JsonIgnore] internal IReadOnlyList<AttributeRename> AttributeRenames { get; init; } = [];

    internal ulong? EffectiveTimeUnixNano() => TimeUnixNano is > 0 ? TimeUnixNano :
        ObservedTimeUnixNano is > 0 ? ObservedTimeUnixNano : null;
}

/// <summary>A project-scoped, immutable view. Its captured clock and rows survive process restarts.</summary>
internal interface ISnapshot
{
    string Id { get; }
    DateTimeOffset CapturedAt { get; }
    IReadOnlyList<SpanStorageRow> Spans { get; }
    IReadOnlyList<LogStorageRow> Logs { get; }
    IReadOnlyList<MetricSeriesRow> Metrics { get; }
    IReadOnlyList<SignalWatermarkRow> Signals { get; }
    IReadOnlyList<ResourceObservationRow> Resources { get; }
    IReadOnlyList<AttributeStabilityRow> AttributeStabilities { get; }
    IReadOnlyList<AttributeDiscoveryRow> AttributeDiscoveries { get; }
}

internal sealed partial record TelemetrySnapshot : ISnapshot;

internal sealed class SnapshotUnavailableException(string? message, Exception? innerException)
    : InvalidOperationException(message, innerException)
{
    public SnapshotUnavailableException()
        : this("Snapshot is unknown, expired, or belongs to another project.", null) { }

    public SnapshotUnavailableException(string? message) : this(message, null) { }
}
