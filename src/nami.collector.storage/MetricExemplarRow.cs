namespace Nami.Collector.Storage;

// Derived trace index for queries; complete policy-filtered exemplars live in the observation journal.
[DuckDbTable("metric_exemplars", Derived = true, Indexes = "ProjectId,TimeUnixNano",
    OnConflict = "ON CONFLICT DO NOTHING")]
internal sealed partial record MetricExemplarRow
{
    [DuckDbColumn(PrimaryKeyOrdinal = 0, SqlType = "VARCHAR(128)")]
    public required string ProjectId { get; init; }

    [DuckDbColumn(PrimaryKeyOrdinal = 1)] public required string SeriesId { get; init; }

    [DuckDbColumn(PrimaryKeyOrdinal = 2)] public required ulong TimeUnixNano { get; init; }

    [DuckDbColumn(PrimaryKeyOrdinal = 3)] public required string TraceId { get; init; }
}
