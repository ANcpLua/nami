using DuckDB.NET.Data;
using Nami.Collector.Ingestion.Primitives;

namespace Nami.Collector.Storage;

/// <summary>
/// A metric range query in the Grafana/Prometheus shape: a metric, label matchers, a time
/// range, and a step. The collector answers it server-side and returns buckets, never raw
/// points — the primary consumer is an agent over MCP, and a compact bucketed answer is both
/// the cheaper payload and the one it can reason about without a second round trip.
/// </summary>
internal sealed record MetricRangeQuery
{
    public string? SeriesId { get; init; }
    public required string ProjectId { get; init; }
    public required string MetricName { get; init; }
    public required ulong StartUnixNano { get; init; }
    public required ulong EndUnixNano { get; init; }

    /// <summary>Bucket width in nanoseconds. One bucket covering the range collapses the query to a single value.</summary>
    public required ulong StepUnixNano { get; init; }

    public MetricAggregation Aggregation { get; init; } = MetricAggregation.Avg;

    /// <summary>Attribute keys to split the result by. Empty collapses every matching series into one stream.</summary>
    public IReadOnlyList<string> GroupBy { get; init; } = [];

    public IReadOnlyList<MetricAttributeMatcher> Matchers { get; init; } = [];

    /// <summary>Maximum result streams. Grouping on a high-cardinality attribute is truncated, not refused.</summary>
    public int SeriesLimit { get; init; } = 50;
}

internal sealed class MetricQueryConflictException(string? message = null) : InvalidOperationException(message ??
    "Selected metric series have incompatible kind, unit or monotonicity; select one series_id.");

internal sealed class MetricProjectionUnavailableException(string message) : InvalidOperationException(message);

internal sealed class MetricQueryRequiresHistogramException() : InvalidOperationException(
    "Percentile aggregations require a histogram metric; use avg, min, max, sum, count or last.");

internal sealed partial class DuckDbStore
{
    private const int MaxMetricSeriesPerBatch = 120;

    private const int MaxMetricPointsPerBatch = 150;

    /// <summary>
    /// Percentiles fold bucket vectors that SQL cannot sum element-wise, so their rows are
    /// read out and folded here. The cap keeps that read bounded; every other aggregation is
    /// computed entirely in DuckDB and is unaffected.
    /// </summary>
    private const int MaxHistogramRowsPerQuery = 20_000;

    public async Task InsertMetricsAsync(
        IReadOnlyList<MetricSeriesRow> series,
        IReadOnlyList<MetricObservationRow> observations,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (observations.Count is 0)
            return;

        IReadOnlyList<MetricSeriesRow> indexedSeries = series.Any(static row => row.InstrumentId is null)
            ? series.Select(static row => row.InstrumentId is null
                ? row with { InstrumentId = MetricStorageMapper.InstrumentId(row) } : row).ToArray()
            : series;

        await ExecuteWriteAsync(async (con, token) =>
        {
            await using var tx = await con.BeginTransactionAsync(token).ConfigureAwait(false);
            // Series first: a point is only reachable through its series row, so writing the
            // point first would expose an unjoinable sample to a concurrent reader.
            await InsertRowsBatchedAsync(con, tx, indexedSeries, MetricSeriesRow.AddParameters,
                MetricSeriesRow.BuildMultiRowInsertSql, MaxMetricSeriesPerBatch, token);
            await InsertRowsBatchedAsync(con, tx, observations, MetricObservationRow.AddParameters,
                MetricObservationRow.BuildMultiRowInsertSql, MaxMetricPointsPerBatch, token);
            // An export's first observation of a timestamp is the one that arrived first.
            foreach (var arriving in observations.GroupBy(static row => new MetricObservationKey(row.ProjectId, row.SeriesId, row.TimeUnixNano)))
            {
                token.ThrowIfCancellationRequested();
                RefreshMetricProjection(con, tx, arriving.Key, arriving.First());
            }
            RecordIngestion(con, tx, "metrics", indexedSeries.Select(static row => new ArrivingResource(row.ProjectId, row.ServiceName, row.ResourceJson, row.AttributesJson, row.ScopeAttributesJson, row.AttributeRenames)), token);
            await tx.CommitAsync(token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    // Upgrade bounded batches of complete series reference graphs atomically. Unknown historical
    // scope fields remain unknown; only each stored descriptor determines its new identity.
    private static bool BackfillMetricInstrumentIds(DuckDBConnection connection)
    {
        var changed = false;
        while (true)
        {
            using var transaction = connection.BeginTransaction();
            var rows = new List<MetricSeriesRow>();
            using (var read = connection.CreateCommand())
            {
                read.Transaction = transaction;
                read.CommandText = "SELECT " + MetricSeriesRow.SelectColumnList +
                    " FROM metric_series WHERE instrument_id IS NULL LIMIT 1000";
                using var reader = read.ExecuteReader();
                while (reader.Read()) rows.Add(MetricSeriesRow.MapFromReader(reader));
            }
            foreach (var previous in rows)
            {
                changed = true;
                var indexed = previous with { InstrumentId = MetricStorageMapper.InstrumentId(previous) };
                var migrated = indexed with { SeriesId = MetricStorageMapper.SeriesId(indexed) };
                if (migrated.SeriesId == previous.SeriesId)
                {
                    using var update = connection.CreateCommand();
                    update.Transaction = transaction;
                    update.CommandText = "UPDATE metric_series SET instrument_id = $1 WHERE project_id = $2 AND series_id = $3";
                    AddParameters(update, migrated.InstrumentId, previous.ProjectId, previous.SeriesId);
                    update.ExecuteNonQuery();
                    continue;
                }
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "SELECT " + MetricSeriesRow.SelectColumnList +
                    " FROM metric_series WHERE project_id = $1 AND series_id = $2";
                AddParameters(command, migrated.ProjectId, migrated.SeriesId);
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        var existing = MetricSeriesRow.MapFromReader(reader);
                        existing = existing with { InstrumentId = existing.InstrumentId ?? MetricStorageMapper.InstrumentId(existing) };
                        // Only description, observation ranges and dropped counts are non-identifying.
                        var comparable = migrated with
                        {
                            Description = existing.Description, FirstSeenUnixNano = existing.FirstSeenUnixNano,
                            LastSeenUnixNano = existing.LastSeenUnixNano,
                            ScopeDroppedAttributesCount = existing.ScopeDroppedAttributesCount,
                            ResourceDroppedAttributesCount = existing.ResourceDroppedAttributesCount
                        };
                        if (comparable != existing)
                            throw new InvalidDataException($"Metric identity collision while migrating '{previous.SeriesId}'.");
                    }
                }
                command.Parameters.Clear();
                command.CommandText = MetricSeriesRow.BuildMultiRowInsertSql(1);
                MetricSeriesRow.AddParameters(command, migrated);
                command.ExecuteNonQuery();

                // Canonical observations survive ID changes, including contradictory historical
                // measurements. The derived query tables are rebuilt after this migration.
                MoveMetricRows("metric_observations", MetricObservationRow.SelectColumnList);
                command.Parameters.Clear();
                command.CommandText = "DELETE FROM metric_series WHERE project_id = $1 AND series_id = $2";
                AddParameters(command, previous.ProjectId, previous.SeriesId);
                command.ExecuteNonQuery();

                void MoveMetricRows(string table, string columns)
                {
                    var projection = string.Join(", ", columns.Split(',').Select(static column => column.Trim().Trim('"'))
                        .Select(static column => column == "series_id" ? "$1" : column));
                    command.Parameters.Clear();
                    command.CommandText = "INSERT INTO " + table + " (" + columns + ") SELECT " + projection + " FROM " + table + " " +
                        "WHERE project_id = $2 AND series_id = $3 ON CONFLICT DO NOTHING";
                    AddParameters(command, migrated.SeriesId, previous.ProjectId, previous.SeriesId);
                    command.ExecuteNonQuery();
                    command.Parameters.Clear();
                    command.CommandText = "DELETE FROM " + table + " WHERE project_id = $1 AND series_id = $2";
                    AddParameters(command, previous.ProjectId, previous.SeriesId);
                    command.ExecuteNonQuery();
                }
            }
            transaction.Commit();
            if (rows.Count is 0) return changed;
        }
    }

    public Task<IReadOnlyList<MetricCatalogEntry>> ListMetricsAsync(
        string projectId,
        string? namePrefix = null,
        int limit = 200,
        MetricCatalogCursor? cursor = null,
        IReadOnlyList<MetricAttributeMatcher>? matchers = null,
        string? seriesId = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return ExecuteReadAsync<IReadOnlyList<MetricCatalogEntry>>(con =>
        {
            using var cmd = con.CreateCommand();
            var qb = new QueryBuilder();
            qb.Add("project_id = $N", projectId);
            if (cursor is { } after)
                qb.Add("(metric_name > $A OR (metric_name = $A AND instrument_id > $B))", after.MetricName, after.InstrumentId);
            if (!string.IsNullOrEmpty(namePrefix)) qb.Add("starts_with(metric_name, $N)", namePrefix);
            if (seriesId is not null) qb.Add("series_id = $N", seriesId);
            AppendMatchers(ref qb, matchers ?? []);
            // Composed, not interpolated, into CommandText: every fragment spliced here is a
            // constant chosen by this method, and every caller value is a bound parameter.
            // An aggregate projection, not a row read: the catalog collapses every series
            // under a resource, scope and name into one entry, so there is no generated column list to reuse
            // and the reader ordinals below are this statement's own contract.
            var sql = """
                      SELECT count(*), metric_name,
                             first(description ORDER BY length(description) DESC NULLS LAST, description DESC), max(last_seen_unix_nano),
                             to_json(list(DISTINCT struct_pack(
                                 kind := metric_kind, temporality := temporality,
                                 monotonic := is_monotonic, unit := unit, service := service_name))),
                             instrument_id, first(service_name), first(resource_json), first(resource_entity_refs_json),
                             first(resource_schema_url), max(resource_dropped_attributes_count), first(scope_name),
                             first(scope_version), first(scope_schema_url), first(scope_attributes_json), max(scope_dropped_attributes_count)
                      FROM metric_series
                      """
                      + " " + qb.WhereClause
                      + " GROUP BY metric_name, instrument_id ORDER BY metric_name, instrument_id LIMIT " + qb.NextParam;
            cmd.CommandText = sql;
            qb.ApplyTo(cmd);
            cmd.Parameters.Add(new DuckDBParameter { Value = limit });

            var entries = new List<MetricCatalogEntry>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var variants = ReadMetricVariants(DuckDbValueReader.ReadString(reader, 4, "[]"));
                var first = variants[0];
                entries.Add(new MetricCatalogEntry(
                    DuckDbValueReader.ReadString(reader, 1, ""),
                    first.Kind,
                    first.Temporality,
                    first.IsMonotonic,
                    first.Unit,
                    DuckDbValueReader.ReadString(reader, 2),
                    DuckDbValueReader.ReadInt64(reader, 0, 0),
                    DuckDbValueReader.ReadUInt64(reader, 3, 0))
                {
                    Variants = variants,
                    InstrumentId = DuckDbValueReader.ReadString(reader, 5) ?? throw new InvalidDataException("Metric catalog has no instrument identity."),
                    ServiceName = DuckDbValueReader.ReadString(reader, 6),
                    ResourceJson = DuckDbValueReader.ReadString(reader, 7),
                    ResourceEntityRefsJson = DuckDbValueReader.ReadString(reader, 8),
                    ResourceSchemaUrl = DuckDbValueReader.ReadString(reader, 9),
                    ResourceDroppedAttributesCount = DuckDbValueReader.ReadUInt32(reader, 10),
                    ScopeName = DuckDbValueReader.ReadString(reader, 11),
                    ScopeVersion = DuckDbValueReader.ReadString(reader, 12),
                    ScopeSchemaUrl = DuckDbValueReader.ReadString(reader, 13),
                    ScopeAttributesJson = DuckDbValueReader.ReadString(reader, 14),
                    ScopeDroppedAttributesCount = DuckDbValueReader.ReadUInt32(reader, 15)
                });
            }

            return entries;
        }, ct);
    }

    private static IReadOnlyList<MetricDescriptorVariant> ReadMetricVariants(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray()
            .GroupBy(static item => (
                Kind: item.GetProperty("kind").GetByte(),
                Temporality: item.GetProperty("temporality").GetByte(),
                Monotonic: item.GetProperty("monotonic").GetByte(),
                Unit: item.GetProperty("unit").GetString()))
            .OrderBy(static group => group.Key.Kind)
            .ThenBy(static group => group.Key.Temporality)
            .ThenBy(static group => group.Key.Monotonic)
            .ThenBy(static group => group.Key.Unit, StringComparer.Ordinal)
            .Select(static group => new MetricDescriptorVariant(
                group.Key.Kind, group.Key.Temporality, group.Key.Monotonic, group.Key.Unit,
                group.Select(static item => item.GetProperty("service").GetString())
                    .OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()))
            .ToArray();
    }

    public Task<IReadOnlyList<MetricSeriesRow>> ListMetricSeriesAsync(
        string projectId,
        string metricName,
        IReadOnlyList<MetricAttributeMatcher> matchers,
        int limit = 200,
        MetricSeriesPageCursor? cursor = null,
        string? seriesId = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return ExecuteReadAsync<IReadOnlyList<MetricSeriesRow>>(con =>
        {
            var qb = new QueryBuilder();
            qb.Add("project_id = $N", projectId);
            qb.Add("metric_name = $N", metricName);
            if (seriesId is not null) qb.Add("series_id = $N", seriesId);
            AppendMatchers(ref qb, matchers);
            if (cursor is { } after)
                qb.Add("(last_seen_unix_nano < $A OR (last_seen_unix_nano = $A AND series_id > $B))",
                    (decimal)after.LastSeenUnixNano, after.SeriesId);

            using var cmd = con.CreateCommand();
            // CA2100: the generated column list, QueryBuilder's $n predicates and the limit's placeholder; values are parameters.
            cmd.CommandText = "SELECT " + MetricSeriesRow.SelectColumnList
                              + " FROM metric_series " + qb.WhereClause
                              + " ORDER BY last_seen_unix_nano DESC, series_id LIMIT " + qb.NextParam;
            qb.ApplyTo(cmd);
            cmd.Parameters.Add(new DuckDBParameter { Value = limit });

            var rows = new List<MetricSeriesRow>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                rows.Add(MetricSeriesRow.MapFromReader(reader));

            return rows;
        }, ct);
    }

    public Task<IReadOnlyList<MetricQuerySeries>> QueryMetricAsync(
        MetricRangeQuery request,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (request.StepUnixNano is 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Metric query step must be positive.");
        if (request.EndUnixNano < request.StartUnixNano)
            throw new ArgumentOutOfRangeException(nameof(request), "Metric query end must not precede its start.");

        return ExecuteReadAsync<IReadOnlyList<MetricQuerySeries>>(con =>
        {
            var kind = ResolveMetricKind(con, request);
            if (kind == (byte)MetricKind.Summary)
                throw new MetricProjectionUnavailableException("OTLP summaries are available as original points; this reducer does not support them.");
            ValidateMetricProjection(con, request);
            if (kind >= (byte)MetricKind.Histogram) return QueryHistogram(con, request, ct);
            if (request.Aggregation is MetricAggregation.P50 or MetricAggregation.P90 or MetricAggregation.P95 or MetricAggregation.P99)
                throw new MetricQueryRequiresHistogramException();
            return QueryAggregate(con, request);
        }, ct);
    }

    private static void ValidateMetricProjection(DuckDBConnection connection, MetricRangeQuery request)
    {
        var where = new QueryBuilder();
        where.Add("p.project_id = $N", request.ProjectId);
        where.Add("s.metric_name = $N", request.MetricName);
        if (request.SeriesId is not null) where.Add("p.series_id = $N", request.SeriesId);
        AppendMatchers(ref where, request.Matchers, "s.");
        where.Add("p.time_unix_nano >= $N", (decimal)request.StartUnixNano);
        where.Add("p.time_unix_nano < $N", (decimal)request.EndUnixNano);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT max(p.has_conflict), first(p.projection_unavailable_reason) FILTER " +
            "(WHERE p.projection_unavailable_reason IS NOT NULL) FROM metric_points p JOIN metric_series s " +
            "ON s.project_id = p.project_id AND s.series_id = p.series_id " + where.WhereClause;
        where.ApplyTo(command);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return;
        if (DuckDbValueReader.ReadByte(reader, 0, 0) != 0)
            throw new MetricQueryConflictException("Different measurements exist at one series timestamp; inspect the original observations before aggregating this window.");
        if (request.Aggregation is MetricAggregation.P50 or MetricAggregation.P90 or MetricAggregation.P95 or MetricAggregation.P99 &&
            DuckDbValueReader.ReadString(reader, 1) is { } reason)
            throw new MetricProjectionUnavailableException(reason);
    }

    private static IReadOnlyList<MetricQuerySeries> QueryAggregate(
        DuckDBConnection con,
        MetricRangeQuery request)
    {
        var qb = new QueryBuilder();
        qb.Add("s.project_id = $N", request.ProjectId);
        qb.Add("s.metric_name = $N", request.MetricName);
        if (request.SeriesId is not null) qb.Add("s.series_id = $N", request.SeriesId);
        AppendMatchers(ref qb, request.Matchers, "s.");
        qb.Add("p.time_unix_nano >= $N", (decimal)request.StartUnixNano);
        qb.Add("p.time_unix_nano < $N", (decimal)request.EndUnixNano);

        var groupExpression = BuildGroupExpression(request.GroupBy);
        var sample = BuildSampleExpression(request.Aggregation);
        var start = request.StartUnixNano.ToString(CultureInfo.InvariantCulture);
        var step = request.StepUnixNano.ToString(CultureInfo.InvariantCulture);

        using var cmd = con.CreateCommand();
        // Composed, not interpolated, into CommandText: the spliced fragments are the
        // aggregate and grouping expressions this method derives, plus the range and step
        // rendered from unsigned integers. Every caller-supplied value is a bound parameter.
        var sql = "SELECT " + groupExpression + " AS group_key, "
                  + BuildBucketExpression(start, step) + " AS bucket_start, "
                  + sample + " AS sample, count(*) AS point_count "
                  + "FROM metric_points AS p JOIN metric_series AS s "
                  + "ON s.project_id = p.project_id AND s.series_id = p.series_id "
                  + qb.WhereClause
                  + " GROUP BY group_key, bucket_start ORDER BY group_key, bucket_start";
        cmd.CommandText = sql;
        qb.ApplyTo(cmd);

        var streams = new Dictionary<string, List<MetricAggregatePoint>>(StringComparer.Ordinal);
        var order = new List<string>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var groupKey = DuckDbValueReader.ReadString(reader, 0) ?? "";
                if (!streams.TryGetValue(groupKey, out var bucket))
                {
                    if (order.Count >= request.SeriesLimit)
                        continue;
                    bucket = [];
                    streams[groupKey] = bucket;
                    order.Add(groupKey);
                }

                bucket.Add(new MetricAggregatePoint(
                    DuckDbValueReader.ReadUInt64(reader, 1, 0),
                    DuckDbValueReader.ReadDouble(reader, 2),
                    DuckDbValueReader.ReadUInt64(reader, 3, 0)));
            }
        }

        return [.. order.Select(key => new MetricQuerySeries(ToGroupAttributesJson(request.GroupBy, key), streams[key]))];
    }

    private static IReadOnlyList<MetricQuerySeries> QueryHistogram(
        DuckDBConnection con, MetricRangeQuery request, CancellationToken ct)
    {
        var qb = new QueryBuilder();
        qb.Add("s.project_id = $N", request.ProjectId);
        qb.Add("s.metric_name = $N", request.MetricName);
        if (request.SeriesId is not null) qb.Add("s.series_id = $N", request.SeriesId);
        AppendMatchers(ref qb, request.Matchers, "s.");
        qb.Add("p.time_unix_nano < $N", (decimal)request.EndUnixNano);
        // Include the last observation before the range to derive the first interval.
        qb.Add("(p.time_unix_nano >= $N OR p.time_unix_nano = "
            + "(SELECT max(prior.time_unix_nano) FROM metric_points prior WHERE prior.project_id = p.project_id "
            + "AND prior.series_id = p.series_id AND prior.time_unix_nano < $N))", (decimal)request.StartUnixNano);
        using var cmd = con.CreateCommand();
        cmd.CommandTimeout = 50;
        var columns = string.Join(", ", MetricPointRow.SelectColumnList.Split(',').Select(static name => "p." + name.Trim()));
        // CA2100: generated columns, $n predicates and a numeric limit; group-by keys enter only as JsonPathLiteral-escaped literals.
        cmd.CommandText = "SELECT " + columns + ", s.temporality, " + BuildGroupExpression(request.GroupBy)
            + " FROM metric_points p JOIN metric_series s ON s.project_id = p.project_id AND s.series_id = p.series_id "
            + qb.WhereClause + " ORDER BY p.series_id, p.time_unix_nano LIMIT " + (MaxHistogramRowsPerQuery + 1);
        qb.ApplyTo(cmd);
        using var cancellation = ct.Register(cmd.Cancel);
        using var reader = cmd.ExecuteReader();
        var groups = new SortedDictionary<string, SortedDictionary<ulong, List<MetricPointRow>>>(StringComparer.Ordinal);
        var previous = new Dictionary<string, MetricPointRow>(StringComparer.Ordinal);
        var rowCount = 0;
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            if (++rowCount > MaxHistogramRowsPerQuery)
                throw new InvalidOperationException("Histogram query exceeds the bounded read. Narrow the range or matchers.");
            var point = MetricPointRow.MapFromReader(reader);
            var temporality = Convert.ToByte(reader.GetValue(reader.FieldCount - 2), CultureInfo.InvariantCulture);
            var group = reader.GetString(reader.FieldCount - 1);
            var delta = temporality == 2 ? MetricHistogram.HistogramDelta(point, previous.GetValueOrDefault(point.SeriesId), request.StartUnixNano) : point;
            if (point.HistogramCount is not null) previous[point.SeriesId] = point;
            if (point.TimeUnixNano < request.StartUnixNano) continue;
            if (!groups.TryGetValue(group, out var buckets)) groups[group] = buckets = [];
            var bucket = request.StartUnixNano + (point.TimeUnixNano - request.StartUnixNano) / request.StepUnixNano * request.StepUnixNano;
            if (!buckets.TryGetValue(bucket, out var points)) buckets[bucket] = points = [];
            if (delta is not null) points.Add(delta);
        }
        var aggregation = request.Aggregation switch { MetricAggregation.Avg => "avg", MetricAggregation.Min => "min", MetricAggregation.Max => "max", MetricAggregation.Sum => "sum", MetricAggregation.Count => "count", MetricAggregation.P50 => "p50", MetricAggregation.P90 => "p90", MetricAggregation.P95 => "p95", MetricAggregation.P99 => "p99", _ => "last" };
        return groups.Take(request.SeriesLimit).Select(group => new MetricQuerySeries(
            ToGroupAttributesJson(request.GroupBy, group.Key),
            group.Value.Select(bucket => new MetricAggregatePoint(bucket.Key,
                MetricHistogram.Histogram(bucket.Value, aggregation),
                bucket.Value.Aggregate(0UL, static (sum, point) => sum + (point.HistogramCount ?? 0)))).ToArray())).ToArray();
    }

    private static byte ResolveMetricKind(DuckDBConnection con, MetricRangeQuery request)
    {
        var qb = new QueryBuilder();
        qb.Add("project_id = $N", request.ProjectId);
        qb.Add("metric_name = $N", request.MetricName);
        if (request.SeriesId is not null) qb.Add("series_id = $N", request.SeriesId);
        AppendMatchers(ref qb, request.Matchers);
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT metric_kind, unit, is_monotonic, temporality FROM metric_series " + qb.WhereClause;
        qb.ApplyTo(cmd);
        using var reader = cmd.ExecuteReader();
        var descriptors = new List<(byte Kind, string? Unit, byte Monotonic, byte Temporality)>();
        while (reader.Read())
            descriptors.Add((DuckDbValueReader.ReadByte(reader, 0, 0), DuckDbValueReader.ReadString(reader, 1),
                DuckDbValueReader.ReadByte(reader, 2, 0), DuckDbValueReader.ReadByte(reader, 3, 0)));
        if (descriptors.Count is 0) return (byte)MetricKind.Gauge;
        if (descriptors.Select(static d => (d.Kind, d.Unit, d.Monotonic)).Distinct().Skip(1).Any()) throw new MetricQueryConflictException();
        // A histogram is read by each series' changes; a scalar aggregates its recorded values, which are totals in a
        // cumulative series and changes in a delta one.
        var kind = descriptors[0].Kind;
        if (kind < (byte)MetricKind.Histogram && descriptors.Select(static d => d.Temporality).Distinct().Skip(1).Any())
            throw new MetricQueryConflictException("Selected metric series differ in temporality: a cumulative series records totals and a delta series changes, so their recorded values do not aggregate together; select one series_id.");
        return kind;
    }

    /// <summary>
    /// The column an aggregate reads depends on the metric's shape: a histogram has no scalar
    /// value, so its average is the recorded sum over the recorded count, not an average of
    /// per-point averages.
    /// </summary>
    private static string BuildSampleExpression(MetricAggregation aggregation)
    {
        return aggregation switch
        {
            MetricAggregation.Avg => "avg(p.value)",
            MetricAggregation.Min => "min(p.value)",
            MetricAggregation.Max => "max(p.value)",
            MetricAggregation.Sum => "sum(p.value)",
            MetricAggregation.Count => "CAST(count(p.value) AS DOUBLE)",
            MetricAggregation.Last => "arg_max(p.value, p.time_unix_nano)",
            _ => throw new ArgumentOutOfRangeException(nameof(aggregation), aggregation, "Unsupported scalar aggregation.")
        };
    }

    /// <summary>
    /// Floors a point's timestamp onto the step grid anchored at the range start. HUGEINT
    /// keeps the subtraction signed so a point exactly at the start does not wrap.
    /// </summary>
    private static string BuildBucketExpression(string start, string step) =>
        "(((CAST(p.time_unix_nano AS HUGEINT) - " + start + ") // " + step + ") * " + step +
        " + " + start + ")";

    /// <summary>
    /// Grouping keys are read out of the persisted attribute JSON. With no keys every matching
    /// series collapses into one stream, which is what an agent asking "what is X doing" wants.
    /// </summary>
    private static string BuildGroupExpression(IReadOnlyList<string> groupBy)
    {
        if (groupBy.Count is 0)
            return "''";

        var parts = groupBy.Select(static key =>
            $"coalesce(json_extract_string(s.attributes_json, '{JsonPathLiteral(key)}'), '')");
        // chr(31) is the unit separator: DuckDB does not decode escapes inside single quotes,
        // so the separator has to be built as a function call rather than written as "\x1f".
        return string.Join(" || chr(31) || ", parts);
    }

    /// <summary>ASCII unit separator; the SQL side builds it with chr(31).</summary>
    private const char GroupKeySeparator = '\u001f';

    private static string? ToGroupAttributesJson(IReadOnlyList<string> groupBy, string groupKey)
    {
        if (groupBy.Count is 0)
            return null;

        var values = groupKey.Split(GroupKeySeparator);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            for (var i = 0; i < groupBy.Count; i++)
                writer.WriteString(groupBy[i], i < values.Length ? values[i] : "");
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void AppendMatchers(
        ref QueryBuilder qb,
        IReadOnlyList<MetricAttributeMatcher> matchers,
        string alias = "")
    {
        foreach (var matcher in matchers)
        {
            var extract = $"json_extract_string({alias}attributes_json, '{JsonPathLiteral(matcher.Key)}')";
            qb.Add(matcher.IsPrefix ? $"starts_with({extract}, $N)" : $"{extract} = $N", matcher.Value);
        }
    }

    // Attribute keys are dotted, so they must be quoted inside the JSON path or DuckDB reads
    // them as nested members. Keys come from callers, never from stored data, but the escape
    // keeps the path a literal regardless.
    private static string JsonPathLiteral(string key) =>
        "$.\"" + key.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("'", "''", StringComparison.Ordinal) + "\"";

    public Task<MetricRetentionResult> DeleteExpiredMetricsBatchAsync(
        ulong cutoffUnixNano,
        int batchSize,
        CancellationToken ct = default)
    {
        if (batchSize < 1)
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be positive.");

        return ExecuteMaintenanceWriteAsync(async (con, token) =>
        {
            await using var transaction = await con.BeginTransactionAsync(token).ConfigureAwait(false);

            var deletedPoints = 0;
            var affected = new HashSet<MetricObservationKey>();
            await using (var command = con.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                                      DELETE FROM metric_observations
                                      WHERE (project_id, series_id, observation_id) IN (
                                          SELECT project_id, series_id, observation_id
                                          FROM metric_observations
                                          WHERE time_unix_nano < $1
                                          ORDER BY time_unix_nano, project_id, series_id, observation_id
                                          LIMIT $2
                                      )
                                      RETURNING project_id, series_id, time_unix_nano
                                      """;
                command.Parameters.Add(new DuckDBParameter { Value = (decimal)cutoffUnixNano });
                command.Parameters.Add(new DuckDBParameter { Value = batchSize });
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    deletedPoints++;
                    affected.Add(new MetricObservationKey(reader.GetString(0), reader.GetString(1),
                        DuckDbValueReader.ReadUInt64(reader, 2, 0)));
                }
            }

            foreach (var key in affected) RefreshMetricProjection(con, transaction, key);

            await using (var command = con.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM metric_exemplars e WHERE NOT EXISTS (SELECT 1 FROM metric_points p WHERE p.project_id = e.project_id AND p.series_id = e.series_id AND p.time_unix_nano = e.time_unix_nano)";
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            // A series with no points left is unreachable: nothing can query it and it would
            // otherwise keep advertising a metric name that has no data behind it.
            var deletedSeries = 0;
            await using (var command = con.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                                      DELETE FROM metric_series
                                      WHERE NOT EXISTS (
                                          SELECT 1 FROM metric_observations AS p
                                          WHERE p.project_id = metric_series.project_id
                                            AND p.series_id = metric_series.series_id
                                      )
                                      RETURNING series_id
                                      """;
                await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                    deletedSeries++;
            }

            await transaction.CommitAsync(token).ConfigureAwait(false);
            return new MetricRetentionResult(deletedPoints, deletedSeries);
        }, ct);
    }
}
