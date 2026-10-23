namespace Nami.Collector.Storage;

/// <summary>The tables an analysis reads besides its annotations and recorded failure states, which it always reads.</summary>
[Flags]
internal enum AnalysisTables
{
    None = 0,
    Spans = 1,
    Logs = 2,
    MetricSeries = 4,
    /// <summary>Metric points and their exemplars.</summary>
    MetricPoints = 8,
    SavedQueries = 16,
    All = Spans | Logs | MetricSeries | MetricPoints | SavedQueries
}

// Per-request MVCC snapshot with a bounded window and explicit incomplete-scan metadata.
internal sealed record AnalysisReadQuery
{
    public ulong? Start { get; init; }
    public ulong? End { get; init; }
    public string[] TraceIds { get; init; } = [];
    public string[] SessionIds { get; init; } = [];
    /// <summary>
    /// What the analysis reads. A table it does not read cannot cut its answer, so only the tables read decide
    /// <see cref="AnalysisSnapshot.ScanComplete"/>.
    /// </summary>
    public AnalysisTables Tables { get; init; } = AnalysisTables.All;
    public bool ErrorsOnly { get; init; }
    public bool IncludeWindowWithTargets { get; init; }
    /// <summary>Look up which targets of the loaded spans' links the project retains outside the loaded spans.</summary>
    public bool IncludeLinkTargets { get; init; }
    /// <summary>Also load each metric series' last recorded point before <see cref="Start"/>, the predecessor its first change is measured from.</summary>
    public bool IncludeMetricPredecessors { get; init; }
    /// <summary>
    /// The metric instruments the analysis measures. When set, only their series, points and exemplars are read, so
    /// another instrument's points cannot fill the row cap, and <see cref="AnalysisSnapshot.MetricLastSeen"/> holds each
    /// one's last point over every retained series.
    /// </summary>
    public string[] MetricNames { get; init; } = [];
    /// <summary>Read only the metric points that hold a conflicting measurement, and no exemplars: a health check reports nothing else of them.</summary>
    public bool ConflictingPointsOnly { get; init; }
    /// <summary>
    /// A <c>service.version</c> whose first span per service the read looks up over every retained span of the
    /// project, the anchor of a release verification; the row cap never cuts it.
    /// </summary>
    public string? Release { get; init; }
    public const int MaximumRows = 25000;
}
internal sealed record AnalysisSnapshot(
    IReadOnlyList<SpanStorageRow> Spans,
    IReadOnlyList<LogStorageRow> Logs,
    IReadOnlyList<MetricSeriesRow> Series,
    IReadOnlyList<MetricPointRow> Points,
    IReadOnlyList<AnnotationRow> Annotations,
    IReadOnlyList<AnalysisStateRow> State,
    IReadOnlyList<SavedQueryRow> SavedQueries,
    bool ScanComplete = true)
{
    public IReadOnlyList<MetricExemplarRow> Exemplars { get; init; } = [];

    /// <summary>
    /// The start times for which this snapshot holds every span of the project: the read window, narrowed to
    /// just after the oldest loaded start when the row cap cut older spans off. Null when the read selected
    /// spans by trace, session or status, so that no time range is complete.
    /// </summary>
    public AnalysisSpanCoverage? SpanCoverage { get; init; }

    /// <summary>
    /// The targets of the loaded spans' links that the project retains outside the loaded spans, when the
    /// read looked them up (<see cref="AnalysisReadQuery.IncludeLinkTargets"/>).
    /// </summary>
    public IReadOnlySet<(string TraceId, string SpanId)> RetainedLinkTargets { get; init; } = new HashSet<(string TraceId, string SpanId)>();

    /// <summary>
    /// Each service's first span start of the release the read looked up (<see cref="AnalysisReadQuery.Release"/>),
    /// over every retained span of the project; empty when the read looked none up.
    /// </summary>
    public IReadOnlyDictionary<string, ulong> ReleaseStarts { get; init; } = new Dictionary<string, ulong>(StringComparer.Ordinal);

    /// <summary>
    /// Each metric of <see cref="AnalysisReadQuery.MetricNames"/> the project recorded, with its last point over every
    /// retained series whatever the window; empty when the read named no metric.
    /// </summary>
    public IReadOnlyDictionary<string, ulong> MetricLastSeen { get; init; } = new Dictionary<string, ulong>(StringComparer.Ordinal);
}

/// <summary>Every span of the project that starts in [<see cref="FromUnixNano"/>, <see cref="ToUnixNano"/>) is loaded.</summary>
internal readonly record struct AnalysisSpanCoverage(ulong FromUnixNano, ulong ToUnixNano)
{
    public bool Covers(ulong from, ulong to) => from >= FromUnixNano && to <= ToUnixNano;
}

[DuckDbTable("analysis_state", OnConflict = """
    ON CONFLICT (project_id, state_key) DO UPDATE SET
        value_json = EXCLUDED.value_json,
        etag = EXCLUDED.etag,
        updated_at_unix_nano = EXCLUDED.updated_at_unix_nano
    """)]
internal sealed partial record AnalysisStateRow
{
    [DuckDbColumn(PrimaryKeyOrdinal = 0, SqlType = "VARCHAR(128)")]
    public required string ProjectId { get; init; }
    [DuckDbColumn(PrimaryKeyOrdinal = 1)]
    public required string StateKey { get; init; }
    [DuckDbColumn(SqlType = "JSON")]
    public required string ValueJson { get; init; }
    public required string Etag { get; init; }
    public required ulong UpdatedAtUnixNano { get; init; }
}

internal sealed partial class DuckDbStore
{
    private const int LinkTargetsPerStatement = 256;

    public Task<AnalysisSnapshot> ReadAnalysisSnapshotAsync(string projectId, AnalysisReadQuery? query = null, CancellationToken ct = default) =>
        ExecuteReadAsync(con =>
        {
            using var tx = con.BeginTransaction();
            var complete = true;
            var spansTruncated = false;
            query ??= new AnalysisReadQuery();
            List<T> Read<T>(string table, string columns, Func<DbDataReader, T> map)
            {
                using var cmd = con.CreateCommand();
                cmd.Transaction = tx;
                var telemetry = table is "spans" or "logs" or "metric_series" or "metric_points" or "metric_exemplars";
                var tableFlag = table switch
                {
                    "spans" => AnalysisTables.Spans, "logs" => AnalysisTables.Logs, "metric_series" => AnalysisTables.MetricSeries,
                    "metric_points" or "metric_exemplars" => AnalysisTables.MetricPoints, "saved_queries" => AnalysisTables.SavedQueries,
                    _ => AnalysisTables.None
                };
                if (tableFlag is not AnalysisTables.None && !query.Tables.HasFlag(tableFlag)) return [];
                var predicates = new List<string> { "project_id = $1" }; var values = new List<object> { projectId };
                void Add(string column, string comparison, object value) { values.Add(value); predicates.Add(column + comparison + "$" + values.Count); }
                var timeColumn = table switch
                {
                    "spans" => "start_time_unix_nano",
                    "metric_series" => "last_seen_unix_nano",
                    "logs" => LogStorageRow.EffectiveTimeSql,
                    _ => "time_unix_nano"
                };
                var targets = new List<string>();
                if (table is "spans" or "logs")
                {
                    foreach (var id in query.TraceIds) { values.Add(id); targets.Add("trace_id = $" + values.Count); }
                    foreach (var id in query.SessionIds) { values.Add(id); targets.Add("session_id = $" + values.Count); }
                    if (targets.Count > 0)
                    {
                        if (query.IncludeWindowWithTargets && query.Start is { } from && query.End is { } until)
                        {
                            values.Add((decimal)from); var fromParameter = "$" + values.Count;
                            values.Add((decimal)until); targets.Add("(" + timeColumn + " >= " + fromParameter + " AND " + timeColumn + " < $" + values.Count + ")");
                        }
                        predicates.Add("(" + string.Join(" OR ", targets) + ")");
                    }
                }
                if (telemetry && targets.Count is 0)
                {
                    if (query.Start is { } start && table == "metric_points" && query.IncludeMetricPredecessors)
                    {
                        // A point's change is measured against its series' last recorded point, which may precede the read.
                        values.Add((decimal)start);
                        predicates.Add("(time_unix_nano >= $" + values.Count + " OR time_unix_nano = (SELECT max(prior.time_unix_nano) FROM metric_points AS prior"
                            + " WHERE prior.project_id = metric_points.project_id AND prior.series_id = metric_points.series_id AND prior.time_unix_nano < $" + values.Count
                            + " AND (prior.value IS NOT NULL OR prior.histogram_count IS NOT NULL)))");
                    }
                    else if (query.Start is { } from) Add(timeColumn, " >= ", (decimal)from);
                    if (query.End is { } end && table != "metric_series") Add(timeColumn, " < ", (decimal)end);
                }
                if (query.ErrorsOnly && table == "spans") predicates.Add("status_code = 2");
                if (query.ConflictingPointsOnly && table == "metric_exemplars") return [];
                if (query.ConflictingPointsOnly && table == "metric_points") predicates.Add("has_conflict <> 0");
                if (query.MetricNames.Length > 0 && table is "metric_series" or "metric_points" or "metric_exemplars")
                {
                    var names = new List<string>();
                    foreach (var name in query.MetricNames) { values.Add(name); names.Add("$" + values.Count); }
                    // A point or exemplar belongs to its instrument through its series.
                    predicates.Add(table == "metric_series" ? "metric_name IN (" + string.Join(", ", names) + ")"
                        : "series_id IN (SELECT series_id FROM metric_series WHERE project_id = $1 AND metric_name IN (" + string.Join(", ", names) + "))");
                }
                cmd.CommandTimeout = 50;
                // CA2100: columns, table and the predicate shapes are this method's constants; every value is a $n parameter.
                cmd.CommandText = "SELECT " + columns + " FROM " + table + " WHERE " + string.Join(" AND ", predicates)
                    + (telemetry ? " ORDER BY " + timeColumn + " DESC" : "") + " LIMIT " + (AnalysisReadQuery.MaximumRows + 1);
                AddParameters(cmd, values.ToArray());
                ct.ThrowIfCancellationRequested();
                using var cancellation = ct.Register(cmd.Cancel);
                using var reader = cmd.ExecuteReader();
                var rows = new List<T>();
                while (reader.Read()) { ct.ThrowIfCancellationRequested(); rows.Add(map(reader)); }
                if (rows.Count > AnalysisReadQuery.MaximumRows)
                {
                    complete = false; rows.RemoveAt(rows.Count - 1);
                    spansTruncated |= table == "spans";
                }
                return rows;
            }
            // A link may target any span of the project. The targets outside the loaded spans are looked up
            // by their primary key, in the same transaction, a bounded number per statement.
            HashSet<(string TraceId, string SpanId)> RetainedLinkTargets(List<SpanStorageRow> loaded)
            {
                var known = loaded.Select(static s => (s.TraceId, s.SpanId)).ToHashSet();
                var targets = loaded.SelectMany(static s => SpanChildStorage.DeserializeLinks(s.LinksJson) ?? [])
                    .Select(static l => (l.TraceId, l.SpanId)).Where(known.Add).ToList();
                var retained = new HashSet<(string TraceId, string SpanId)>();
                foreach (var batch in targets.Chunk(LinkTargetsPerStatement))
                {
                    ct.ThrowIfCancellationRequested();
                    using var cmd = con.CreateCommand();
                    cmd.Transaction = tx;
                    var values = new List<object> { projectId }; var pairs = new List<string>(batch.Length);
                    foreach (var (traceId, spanId) in batch)
                    {
                        values.Add(traceId); values.Add(spanId);
                        pairs.Add("(trace_id = $" + (values.Count - 1) + " AND span_id = $" + values.Count + ")");
                    }
                    cmd.CommandTimeout = 50;
                    // CA2100: the statement is constants and $n placeholders; every identity is a bound parameter.
                    cmd.CommandText = "SELECT trace_id, span_id FROM spans WHERE project_id = $1 AND (" + string.Join(" OR ", pairs) + ")";
                    AddParameters(cmd, values.ToArray());
                    using var cancellation = ct.Register(cmd.Cancel);
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read()) retained.Add((reader.GetString(0), reader.GetString(1)));
                }
                return retained;
            }
            // A release's first span may be older than any loaded row: its start per service comes from every
            // retained span of the project, in the same transaction, one row per service.
            Dictionary<string, ulong> ReleaseStarts(string? version)
            {
                var starts = new Dictionary<string, ulong>(StringComparer.Ordinal);
                if (version is null) return starts;
                using var cmd = con.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandTimeout = 50;
                // CA2100: the statement is constants, the quoted attribute key and $n placeholders; the version is a bound parameter.
                cmd.CommandText = "SELECT service_name, min(start_time_unix_nano) FROM spans WHERE project_id = $1 AND json_extract_string(resource_json, '"
                    + JsonPathLiteral(CollectorSemanticAttributeCatalog.ServiceVersion) + "') = $2 GROUP BY service_name";
                AddParameters(cmd, projectId, version);
                ct.ThrowIfCancellationRequested();
                using var cancellation = ct.Register(cmd.Cancel);
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) starts[reader.IsDBNull(0) ? "unknown_service" : reader.GetString(0)] = DuckDbValueReader.ReadUInt64(reader, 1, 0);
                return starts;
            }
            // A named metric may have no series in the window; its last point over every retained series tells the
            // answer that the project recorded it.
            Dictionary<string, ulong> MetricLastSeen(string[] names)
            {
                var lastSeen = new Dictionary<string, ulong>(StringComparer.Ordinal);
                if (names.Length is 0) return lastSeen;
                using var cmd = con.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandTimeout = 50;
                // CA2100: the statement is constants and $n placeholders; every name is a bound parameter.
                cmd.CommandText = "SELECT metric_name, max(last_seen_unix_nano) FROM metric_series WHERE project_id = $1 AND metric_name IN ("
                    + string.Join(", ", names.Select(static (_, i) => "$" + (i + 2))) + ") GROUP BY metric_name";
                AddParameters(cmd, [projectId, .. names]);
                ct.ThrowIfCancellationRequested();
                using var cancellation = ct.Register(cmd.Cancel);
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) lastSeen[reader.GetString(0)] = DuckDbValueReader.ReadUInt64(reader, 1, 0);
                return lastSeen;
            }
            var spans = Read("spans", SpanStorageRow.SelectColumnList, SpanStorageRow.MapFromReader);
            var linkTargets = query.IncludeLinkTargets ? RetainedLinkTargets(spans) : new HashSet<(string TraceId, string SpanId)>();
            // Rows are read newest first, so the cap cuts the oldest spans; spans sharing the oldest loaded
            // start may be only partly loaded, so completeness begins just after it.
            AnalysisSpanCoverage? coverage = !query.Tables.HasFlag(AnalysisTables.Spans) || query.ErrorsOnly || query.TraceIds.Length > 0 || query.SessionIds.Length > 0 ? null
                : new AnalysisSpanCoverage(spansTruncated ? spans[^1].StartTimeUnixNano + 1 : query.Start ?? 0, query.End ?? ulong.MaxValue);
            var result = new AnalysisSnapshot(
                spans,
                Read("logs", LogStorageRow.SelectColumnList, LogStorageRow.MapFromReader),
                Read("metric_series", MetricSeriesRow.SelectColumnList, MetricSeriesRow.MapFromReader),
                Read("metric_points", MetricPointRow.SelectColumnList, MetricPointRow.MapFromReader),
                Read("annotations", AnnotationRow.SelectColumnList, AnnotationRow.MapFromReader),
                Read("analysis_state", AnalysisStateRow.SelectColumnList, AnalysisStateRow.MapFromReader),
                Read("saved_queries", SavedQueryRow.SelectColumnList, SavedQueryRow.MapFromReader))
            {
                Exemplars = Read("metric_exemplars", MetricExemplarRow.SelectColumnList, MetricExemplarRow.MapFromReader), SpanCoverage = coverage, RetainedLinkTargets = linkTargets,
                ReleaseStarts = ReleaseStarts(query.Release), MetricLastSeen = MetricLastSeen(query.MetricNames)
            };
            tx.Commit();
            return result with { ScanComplete = complete };
        }, ct);

    public Task<IReadOnlyList<AnalysisStateRow>> ReadAnalysisStateAsync(string projectId, IReadOnlyList<string> keys, CancellationToken ct = default) =>
        ExecuteReadAsync<IReadOnlyList<AnalysisStateRow>>(con =>
        {
            if (keys.Count is 0) return [];
            using var cmd = con.CreateCommand();
            // CA2100: the generated column list and $n placeholders only; the keys are bound parameters.
            cmd.CommandText = "SELECT " + AnalysisStateRow.SelectColumnList + " FROM analysis_state WHERE project_id = $1 AND state_key IN ("
                + string.Join(",", Enumerable.Range(2, keys.Count).Select(i => "$" + i)) + ")";
            AddParameters(cmd, [projectId, .. keys]);
            using var reader = cmd.ExecuteReader(); var rows = new List<AnalysisStateRow>();
            while (reader.Read()) { ct.ThrowIfCancellationRequested(); rows.Add(AnalysisStateRow.MapFromReader(reader)); }
            return rows;
        }, ct);

    public Task<ResourceWriteOutcome<AnalysisStateRow>> WriteAnalysisStateAsync(
        AnalysisStateRow row, string? expectedEtag, CancellationToken ct = default) =>
        ExecuteWriteAsync(async (con, token) =>
        {
            await using var tx = await con.BeginTransactionAsync(token).ConfigureAwait(false);
            await using var cmd = con.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT " + AnalysisStateRow.SelectColumnList +
                " FROM analysis_state WHERE project_id = $1 AND state_key = $2";
            AddParameters(cmd, row.ProjectId, row.StateKey);
            AnalysisStateRow? current;
            await using (var reader = cmd.ExecuteReader())
                current = reader.Read() ? AnalysisStateRow.MapFromReader(reader) : null;
            if (current?.Etag != expectedEtag)
                return new ResourceWriteOutcome<AnalysisStateRow>(ResourceWriteStatus.PreconditionFailed,
                    CurrentEtag: current?.Etag);
            await InsertRowsBatchedAsync(con, tx, [row], AnalysisStateRow.AddParameters,
                AnalysisStateRow.BuildMultiRowInsertSql, 1, token).ConfigureAwait(false);
            await tx.CommitAsync(token).ConfigureAwait(false);
            return new ResourceWriteOutcome<AnalysisStateRow>(ResourceWriteStatus.Updated, row);
        }, ct);
}
