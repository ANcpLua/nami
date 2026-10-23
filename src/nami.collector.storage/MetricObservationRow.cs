namespace Nami.Collector.Storage;

/// <summary>
/// The authoritative metric journal. Different observations of one timestamp remain
/// available even when their measurements conflict; exact export retries deduplicate.
/// </summary>
[DuckDbTable("metric_observations",
    Indexes = "ProjectId,SeriesId,TimeUnixNano;ProjectId,TimeUnixNano",
    OnConflict = "ON CONFLICT (project_id, series_id, observation_id) DO NOTHING")]
internal sealed partial record MetricObservationRow
{
    [DuckDbColumn(PrimaryKeyOrdinal = 0, SqlType = "VARCHAR(128)")]
    public required string ProjectId { get; init; }
    [DuckDbColumn(PrimaryKeyOrdinal = 1)]
    public required string SeriesId { get; init; }
    [DuckDbColumn(PrimaryKeyOrdinal = 2)]
    public required string ObservationId { get; init; }

    public required string MeasurementId { get; init; }
    public required ulong TimeUnixNano { get; init; }
    public ulong? StartTimeUnixNano { get; init; }
    public uint? Flags { get; init; }

    [DuckDbColumn(SqlType = "JSON")]
    public string? DataJson { get; init; }
    [DuckDbColumn(SqlType = "JSON")]
    public string? ExemplarsJson { get; init; }
    [DuckDbColumn(SqlType = "JSON")]
    public string? InstrumentMetadataJson { get; init; }

    /// <summary>
    /// Old stores retained only the normalized projection. Preserve that evidence while
    /// leaving the erased original data, flags and full exemplars explicitly unknown.
    /// </summary>
    [DuckDbColumn(SqlType = "JSON")]
    public string? HistoricalProjectionJson { get; init; }
    [DuckDbColumn(SqlType = "JSON")]
    public string? HistoricalExemplarTraceIdsJson { get; init; }
    public uint? ResourceDroppedAttributesCount { get; init; }
    public uint? ScopeDroppedAttributesCount { get; init; }
}

internal sealed record MetricPointCursor(ulong TimeUnixNano, string ObservationId);

internal sealed record MetricPointQuery
{
    public required string ProjectId { get; init; }
    public required string MetricName { get; init; }
    public required string SeriesId { get; init; }
    public required int Limit { get; init; }
    public ulong? StartUnixNano { get; init; }
    public ulong? EndUnixNano { get; init; }
    public MetricPointCursor? Cursor { get; init; }
}

internal sealed record MetricObservationPageItem(MetricObservationRow Observation, bool HasConflict);
