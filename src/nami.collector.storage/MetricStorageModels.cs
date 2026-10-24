namespace Nami.Collector.Storage;

/// <summary>
/// One row per distinct resource, scope, metric shape and attribute set — the series index.
/// It answers "which metrics exist" and "which series carry attribute X" without touching
/// the point table, which is the pair of questions an agent asks before it asks for values.
/// </summary>
[DuckDbTable(
    "metric_series",
    // Series listing is always project-scoped and almost always metric-scoped; one composite
    // index serves both because the project prefix is usable on its own.
    Indexes = "ProjectId,MetricName",
    OnConflict = """
    ON CONFLICT (project_id, series_id) DO UPDATE SET
        metric_kind = EXCLUDED.metric_kind,
        temporality = EXCLUDED.temporality,
        is_monotonic = EXCLUDED.is_monotonic,
        unit = EXCLUDED.unit,
        description = CASE
            WHEN length(coalesce(EXCLUDED.description, '')) > length(coalesce(description, '')) THEN EXCLUDED.description
            WHEN length(coalesce(EXCLUDED.description, '')) < length(coalesce(description, '')) THEN description
            ELSE greatest(description, EXCLUDED.description) END,
        schema_url = EXCLUDED.schema_url,
        attributes_json = EXCLUDED.attributes_json,
        resource_json = EXCLUDED.resource_json,
        resource_entity_refs_json = EXCLUDED.resource_entity_refs_json,
        first_seen_unix_nano = least(first_seen_unix_nano, EXCLUDED.first_seen_unix_nano),
        last_seen_unix_nano = greatest(last_seen_unix_nano, EXCLUDED.last_seen_unix_nano),
        scope_name = EXCLUDED.scope_name,
        scope_version = EXCLUDED.scope_version,
        scope_schema_url = EXCLUDED.scope_schema_url,
        scope_attributes_json = EXCLUDED.scope_attributes_json,
        scope_dropped_attributes_count = EXCLUDED.scope_dropped_attributes_count,
        resource_schema_url = EXCLUDED.resource_schema_url,
        resource_dropped_attributes_count = EXCLUDED.resource_dropped_attributes_count,
        instrument_id = EXCLUDED.instrument_id
    """)]
internal sealed partial record MetricSeriesRow
{
    [JsonIgnore]
    internal IReadOnlyList<AttributeRename> AttributeRenames { get; init; } = [];
    [DuckDbColumn(PrimaryKeyOrdinal = 0, SqlType = "VARCHAR(128)")]
    public required string ProjectId { get; init; }

    /// <summary>
    /// Deterministic digest of resource, instrumentation scope, metric name, identifying
    /// descriptor fields and point attributes. Stable across restarts, so re-ingesting the same stream
    /// updates one row instead of forking a new series.
    /// </summary>
    [DuckDbColumn(PrimaryKeyOrdinal = 1)]
    public required string SeriesId { get; init; }

    public required string MetricName { get; init; }
    public required byte MetricKind { get; init; }
    public required byte Temporality { get; init; }
    public required byte IsMonotonic { get; init; }

    public string? Unit { get; init; }
    public string? Description { get; init; }
    public string? ServiceName { get; init; }

    [DuckDbColumn(SqlType = "VARCHAR(256)")]
    public string? SchemaUrl { get; init; }

    [DuckDbColumn(SqlType = "JSON")]
    public string? AttributesJson { get; init; }

    [DuckDbColumn(SqlType = "JSON")]
    public string? ResourceJson { get; init; }

    [DuckDbColumn(SqlType = "JSON")]
    public string? ResourceEntityRefsJson { get; init; }

    public required ulong FirstSeenUnixNano { get; init; }
    public required ulong LastSeenUnixNano { get; init; }
    public string? ScopeName { get; init; }
    public string? ScopeVersion { get; init; }
    public string? ScopeSchemaUrl { get; init; }
    [DuckDbColumn(SqlType = "JSON")]
    public string? ScopeAttributesJson { get; init; }
    public uint? ScopeDroppedAttributesCount { get; init; }
    public string? ResourceSchemaUrl { get; init; }
    public uint? ResourceDroppedAttributesCount { get; init; }
    public string? InstrumentId { get; init; }
}

/// <summary>
/// One row per data point. Narrow on purpose: everything constant for a stream lives in
/// <see cref="MetricSeriesRow" />, so a range scan reads only what changes over time. The row is the
/// projection of the journal in <see cref="MetricObservationRow" />, rewritten when an observation of its
/// timestamp arrives; a conflicting measurement keeps the first one and sets <see cref="HasConflict" />.
/// </summary>
[DuckDbTable(
    "metric_points",
    Derived = true,
    // The primary key already orders points by (project, series, time), which is the range
    // scan every value query performs. This one extra index serves the two queries that cross
    // series: the retention sweep and "what happened in this project between t0 and t1".
    Indexes = "ProjectId,TimeUnixNano",
    OnConflict = """
    ON CONFLICT (project_id, series_id, time_unix_nano) DO UPDATE SET
        start_time_unix_nano = EXCLUDED.start_time_unix_nano,
        value = EXCLUDED.value,
        histogram_count = EXCLUDED.histogram_count,
        histogram_sum = EXCLUDED.histogram_sum,
        histogram_min = EXCLUDED.histogram_min,
        histogram_max = EXCLUDED.histogram_max,
        bucket_bounds_json = EXCLUDED.bucket_bounds_json,
        bucket_counts_json = EXCLUDED.bucket_counts_json,
        has_conflict = EXCLUDED.has_conflict,
        projection_unavailable_reason = EXCLUDED.projection_unavailable_reason
    """)]
internal sealed partial record MetricPointRow
{
    [DuckDbColumn(PrimaryKeyOrdinal = 0, SqlType = "VARCHAR(128)")]
    public required string ProjectId { get; init; }

    [DuckDbColumn(PrimaryKeyOrdinal = 1)]
    public required string SeriesId { get; init; }

    [DuckDbColumn(PrimaryKeyOrdinal = 2)]
    public required ulong TimeUnixNano { get; init; }

    public ulong? StartTimeUnixNano { get; init; }

    /// <summary>
    /// Gauge and sum value. NULL both for histogram points and for a point whose
    /// <c>NO_RECORDED_VALUE</c> flag was set — in either case there is no scalar to read,
    /// and a NULL is the one representation every aggregate already skips.
    /// </summary>
    public double? Value { get; init; }

    public ulong? HistogramCount { get; init; }
    public double? HistogramSum { get; init; }
    public double? HistogramMin { get; init; }
    public double? HistogramMax { get; init; }

    /// <summary>
    /// Ascending explicit upper bounds, JSON array of doubles. The implicit final bucket
    /// (+infinity) is not listed; <c>bucket_counts_json</c> is always one element longer.
    /// </summary>
    [DuckDbColumn(SqlType = "JSON")]
    public string? BucketBoundsJson { get; init; }

    [DuckDbColumn(SqlType = "JSON")]
    public string? BucketCountsJson { get; init; }
    public byte HasConflict { get; init; }
    public string? ProjectionUnavailableReason { get; init; }
}

/// <summary>A resource, scope and name with its actual descriptor variants.</summary>
internal sealed record MetricCatalogEntry(
    string MetricName,
    byte MetricKind,
    byte Temporality,
    byte IsMonotonic,
    string? Unit,
    string? Description,
    long SeriesCount,
    ulong LastSeenUnixNano)
{
    public required string InstrumentId { get; init; }
    public string? ServiceName { get; init; }
    public string? ResourceJson { get; init; }
    public string? ResourceEntityRefsJson { get; init; }
    public string? ResourceSchemaUrl { get; init; }
    public uint? ResourceDroppedAttributesCount { get; init; }
    public string? ScopeName { get; init; }
    public string? ScopeVersion { get; init; }
    public string? ScopeSchemaUrl { get; init; }
    public string? ScopeAttributesJson { get; init; }
    public uint? ScopeDroppedAttributesCount { get; init; }
    public IReadOnlyList<MetricDescriptorVariant> Variants { get; init; } = [];
}

internal sealed record MetricDescriptorVariant(
    byte Kind, byte Temporality, byte IsMonotonic, string? Unit, IReadOnlyList<string> Services);

/// <summary>One aggregated time bucket of a metric query.</summary>
internal sealed record MetricAggregatePoint(
    ulong BucketStartUnixNano,
    double? Value,
    ulong PointCount);

/// <summary>
/// One result stream of a metric query: its grouping attributes and time buckets.
/// </summary>
internal sealed record MetricQuerySeries(
    string? AttributesJson,
    IReadOnlyList<MetricAggregatePoint> Points);

internal readonly record struct MetricAttributeMatcher(string Key, string Value, bool IsPrefix);

internal enum MetricAggregation : byte
{
    Avg,
    Min,
    Max,
    Sum,
    Count,
    Last,
    P50,
    P90,
    P95,
    P99
}

internal readonly record struct MetricRetentionResult(int Points, int Series);
