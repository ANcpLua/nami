namespace Nami.Collector.Storage;

/// <summary>
/// A user note on one trace, span or session (Contract 11.6.0). It points at telemetry by id and
/// never changes it. Rows are user-written, so they are authoritative storage; retention expires
/// them with the telemetry window, counted from their last change.
/// </summary>
[DuckDbTable(
    "annotations",
    // Annotation reads are always project-scoped: by trace (which includes its spans), by
    // session, or newest-first over the project. The created-at index also serves retention.
    Indexes = "ProjectId,TraceId;ProjectId,SessionId;ProjectId,CreatedAtUnixNano;UpdatedAtUnixNano",
    OnConflict = """
    ON CONFLICT (project_id, annotation_id) DO UPDATE SET
        text = EXCLUDED.text,
        updated_at_unix_nano = EXCLUDED.updated_at_unix_nano,
        etag = EXCLUDED.etag
    """)]
internal sealed partial record AnnotationRow
{
    [DuckDbColumn(PrimaryKeyOrdinal = 0, SqlType = "VARCHAR(128)")]
    public required string ProjectId { get; init; }

    [DuckDbColumn(PrimaryKeyOrdinal = 1, SqlType = "VARCHAR(128)")]
    public required string AnnotationId { get; init; }

    /// <summary><c>trace</c>, <c>span</c> or <c>session</c>; fixed at creation.</summary>
    public required string TargetKind { get; init; }

    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
    public string? SessionId { get; init; }

    public required string Text { get; init; }

    public required ulong CreatedAtUnixNano { get; init; }
    public required ulong UpdatedAtUnixNano { get; init; }

    [DuckDbColumn(SqlType = "VARCHAR(128)")]
    public required string Etag { get; init; }
}

/// <summary>
/// A named, structured filter over traces, logs or metrics. It stores the filter, never results.
/// Names are unique per project; the single storage writer checks that before every write.
/// </summary>
[DuckDbTable(
    "saved_queries",
    Indexes = "ProjectId,Name",
    OnConflict = """
    ON CONFLICT (project_id, saved_query_id) DO UPDATE SET
        name = EXCLUDED.name,
        description = EXCLUDED.description,
        signal = EXCLUDED.signal,
        filter_json = EXCLUDED.filter_json,
        updated_at_unix_nano = EXCLUDED.updated_at_unix_nano,
        etag = EXCLUDED.etag
    """)]
internal sealed partial record SavedQueryRow
{
    [DuckDbColumn(PrimaryKeyOrdinal = 0, SqlType = "VARCHAR(128)")]
    public required string ProjectId { get; init; }

    [DuckDbColumn(PrimaryKeyOrdinal = 1, SqlType = "VARCHAR(128)")]
    public required string SavedQueryId { get; init; }

    [DuckDbColumn(SqlType = "VARCHAR(200)")]
    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary><c>traces</c>, <c>logs</c> or <c>metrics</c>, mirrored from the filter for listing.</summary>
    public required string Signal { get; init; }

    /// <summary>The filter in its contract wire form.</summary>
    [DuckDbColumn(SqlType = "JSON")]
    public required string FilterJson { get; init; }

    public required ulong CreatedAtUnixNano { get; init; }
    public required ulong UpdatedAtUnixNano { get; init; }

    [DuckDbColumn(SqlType = "VARCHAR(128)")]
    public required string Etag { get; init; }
}

/// <summary>
/// The record a creating POST that carried <c>Idempotency-Key</c> created. A repeat with the same
/// key and request fingerprint answers that record as it is now, or creates it again once it was
/// deleted; another fingerprint is a conflict. Deleting the record deletes its keys; the others
/// expire after 24 hours, swept by retention.
/// </summary>
[DuckDbTable(
    "idempotency_keys",
    Indexes = "CreatedAtUnixNano",
    OnConflict = """
    ON CONFLICT (project_id, operation, idempotency_key) DO UPDATE SET
        request_hash = EXCLUDED.request_hash,
        resource_id = EXCLUDED.resource_id,
        created_at_unix_nano = EXCLUDED.created_at_unix_nano
    """)]
internal sealed partial record IdempotencyKeyRow
{
    [DuckDbColumn(PrimaryKeyOrdinal = 0, SqlType = "VARCHAR(128)")]
    public required string ProjectId { get; init; }

    /// <summary>The creating route, e.g. <c>annotations</c>; a key is scoped to one route.</summary>
    [DuckDbColumn(PrimaryKeyOrdinal = 1, SqlType = "VARCHAR(64)")]
    public required string Operation { get; init; }

    [DuckDbColumn(PrimaryKeyOrdinal = 2, SqlType = "VARCHAR(255)")]
    public required string IdempotencyKey { get; init; }

    /// <summary>SHA-256 of the canonical request body.</summary>
    [DuckDbColumn(SqlType = "VARCHAR(64)")]
    public required string RequestHash { get; init; }

    [DuckDbColumn(SqlType = "VARCHAR(128)")]
    public required string ResourceId { get; init; }

    public required ulong CreatedAtUnixNano { get; init; }
}
