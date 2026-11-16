using System.Diagnostics;
using DuckDB.NET.Data;
using Google.Protobuf;
using Nami.Collector.Ingestion;
using Nami.Collector.Storage;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace Nami.IntegrationTests;

/// <summary>
///     A batch of more than one row goes through DuckDB's appender into a temporary table and from there into the
///     table in one statement (ledger N48). These tests hold what a batch promises whatever its size.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class StorageBatchWriteTests(TestContext context)
{
    /// <summary>
    ///     nami sets no memory limit of its own. A default of 512 MiB refused a metrics request of 2,000 data
    ///     points, inside the ingest bounds, with "Out of Memory Error (511.7/512.0 MiB)" (evaluator-18). A store
    ///     that was given no limit has the one DuckDB gives any database; an operator's limit replaces it.
    /// </summary>
    [TestMethod]
    [DataRow(null, DisplayName = "No limit configured: DuckDB's own default")]
    [DataRow("256MB", DisplayName = "A configured limit is the limit")]
    public async Task Open_MemoryLimit_IsTheConfiguredOneOrDuckDbsOwn(string? configured)
    {
        var path = DatabasePath("limit.duckdb");
        await using var store = new DuckDbStore(path, memoryLimit: configured);

        // A connection to the same file shares the store's native instance, and its settings.
        var inEffect = Scalar(path, "SELECT current_setting('memory_limit')");
        var duckDbsOwn = Scalar(DatabasePath("plain.duckdb"), "SELECT current_setting('memory_limit')");
        Assert.AreEqual(configured is null ? duckDbsOwn : "244.1 MiB", inEffect);
    }

    /// <summary>A logs request at the record bound is one batch too: every row is stored, numbered in its order.</summary>
    [TestMethod]
    public async Task InsertLogs_BatchAtTheRecordBound_StoresEveryRowInOrder()
    {
        var path = DatabasePath("logs.duckdb");
        var scope = new OpenTelemetry.Proto.Logs.V1.ScopeLogs();
        for (var i = 0; i < IngestLimits.DefaultMaxRequestRecords; i++)
        {
            scope.LogRecords.Add(new OpenTelemetry.Proto.Logs.V1.LogRecord
            {
                TimeUnixNano = 1_000_000_000UL + (ulong)i,
                Body = new OpenTelemetry.Proto.Common.V1.AnyValue { StringValue = "record " + (uint)i }
            });
        }

        var request = new OpenTelemetry.Proto.Collector.Logs.V1.ExportLogsServiceRequest
        {
            ResourceLogs = { new OpenTelemetry.Proto.Logs.V1.ResourceLogs { ScopeLogs = { scope } } }
        };
        var rows = IngestionStorageMapper.ToLogStorageRows(OtlpConverter.ConvertLogs(request, TimeProvider.System));
        await using (var store = new DuckDbStore(path))
            await store.InsertLogsAsync(rows, context.CancellationToken);

        Assert.AreEqual((long)rows.Count, Scalar(path, "SELECT count(*) FROM logs"));
        // The sequence a row gets follows its place in the batch.
        Assert.AreEqual(0L, Scalar(path,
            "SELECT count(*) FROM (SELECT time_unix_nano, lag(time_unix_nano) OVER (ORDER BY ingest_sequence) AS " +
            "previous FROM logs) WHERE previous IS NOT NULL AND time_unix_nano <> previous + 1"));
    }

    /// <summary>
    ///     A batch that holds one span twice applies both, in order, as two batches would: the later one is what
    ///     is stored. Before, the earlier one won when the two fell into the same statement of 100 rows and the
    ///     later one when they did not.
    /// </summary>
    [TestMethod]
    [DataRow(1, DisplayName = "The repeat is the next row")]
    [DataRow(150, DisplayName = "The repeat is 150 rows later")]
    public async Task Enqueue_BatchRepeatingASpan_StoresTheLaterOccurrence(int distance)
    {
        var path = DatabasePath("repeat.duckdb");
        var rows = Rows(distance + 20);
        rows.Insert(distance, rows[0] with { Name = "later" });
        await using (var store = new DuckDbStore(path))
            await store.EnqueueAsync(new SpanBatch(rows), context.CancellationToken);

        Assert.AreEqual((long)(distance + 20), Scalar(path, "SELECT count(*) FROM spans"));
        Assert.AreEqual("later", Scalar(path, $"SELECT name FROM spans WHERE span_id = '{rows[0].SpanId}'"));
    }

    /// <summary>A request at the record bound is one batch: every row is stored, each as it was mapped.</summary>
    [TestMethod]
    public async Task Enqueue_BatchAtTheRecordBound_StoresEveryRow()
    {
        var path = DatabasePath("bound.duckdb");
        var rows = Rows(IngestLimits.DefaultMaxRequestRecords);
        await using (var store = new DuckDbStore(path))
            await store.EnqueueAsync(new SpanBatch(rows), context.CancellationToken);

        Assert.AreEqual((long)rows.Count, Scalar(path, "SELECT count(*) FROM spans"));
        Assert.AreEqual((long)rows.Count, Scalar(path,
            "SELECT count(*) FROM spans WHERE name = 'span ' || (end_time_unix_nano - 2000000000)::VARCHAR " +
            "AND duration_ns = 1000000000 AND attributes_json IS NOT NULL"));
    }

    /// <summary>
    ///     A metrics request at the record bound is stored in the time an exporter waits, under a memory limit a
    ///     small machine sets. The projection of its points took one statement a point: 26 to 31 s for 3,000 points,
    ///     longer than an OTLP exporter's timeout of 10 s, and more memory than a limit of 512 MiB allows (ledger
    ///     N108). It is now a fixed number of statements a request.
    /// </summary>
    [TestMethod]
    [DataRow(1, DisplayName = "One series of 3,000 points")]
    [DataRow(30, DisplayName = "30 series of 100 points")]
    [DataRow(IngestLimits.DefaultMaxRequestRecords, DisplayName = "3,000 series of one point")]
    public async Task InsertMetrics_RequestAtTheRecordBound_IsStoredUnderASmallLimitInAnExportersTime(int series)
    {
        var path = DatabasePath("metrics-bound.duckdb");
        var points = IngestLimits.DefaultMaxRequestRecords / series;
        var rows = MetricStorageMapper.ToStorageRows(OtlpConverter.ConvertMetrics(MetricsRequest(
            Enumerable.Range(0, series).Select(metric => Gauge("bound." + (uint)metric,
                Enumerable.Range(0, points).Select(point => Point(point, metric * 10_000L + point, point)))))));
        Assert.HasCount(IngestLimits.DefaultMaxRequestRecords, rows.Observations);
        TimeSpan elapsed;
        await using (var store = new DuckDbStore(path, memoryLimit: "256MB"))
        {
            var started = Stopwatch.GetTimestamp();
            await store.InsertMetricsAsync(rows.Series, rows.Observations, context.CancellationToken);
            elapsed = Stopwatch.GetElapsedTime(started);
        }

        Assert.AreEqual((long)series, Scalar(path, "SELECT count(*) FROM metric_series"));
        Assert.AreEqual((long)rows.Observations.Count, Scalar(path, "SELECT count(*) FROM metric_observations"));
        // Every point is projected with its own value, and every exemplar with its trace.
        Assert.AreEqual((long)rows.Observations.Count, Scalar(path,
            "SELECT count(DISTINCT value) FROM metric_points WHERE has_conflict = 0"));
        Assert.AreEqual((long)rows.Observations.Count, Scalar(path, "SELECT count(*) FROM metric_exemplars"));
        Assert.IsLessThan(TimeSpan.FromSeconds(10), elapsed);
    }

    /// <summary>
    ///     A batch of points stores what the same points store one request each, whatever the batch holds: points
    ///     that repeat a timestamp with the same or another measurement, exemplars, points of a stored series, and
    ///     the whole batch once more. A batch names its points in a temporary table and one point is bound as
    ///     parameters; the statements are the same.
    /// </summary>
    [TestMethod]
    public async Task InsertMetrics_Batch_StoresWhatItsPointsStoreOneByOne()
    {
        OpenTelemetry.Proto.Metrics.V1.Metric[] first =
        [
            Gauge("plain", Enumerable.Range(0, 40).Select(point => Point(point, point, point % 3 is 0 ? point : null))),
            // One timestamp, three measurements: the first one is kept and the point is marked.
            Gauge("conflict", [Point(1, 5), Point(1, 6), Point(1, 7), Point(2, 1)]),
            // One measurement observed twice, each time with another exemplar; and an exemplar without a trace.
            Gauge("observed", [Point(1, 9, 1), Point(1, 9, 2), Point(2, 4, 3), Point(2, 8, 4), Point(3, 1, 5, false)]),
            Histogram("histogram", 6)
        ];
        OpenTelemetry.Proto.Metrics.V1.Metric[] second =
        [
            // Other measurements at stored timestamps, stored measurements with new exemplars, and new points.
            Gauge("plain", Enumerable.Range(30, 20).Select(point => Point(point, point % 2 is 0 ? point : -point, 100 + point))),
            Gauge("conflict", [Point(2, 2), Point(3, 3)]),
            Gauge("observed", [Point(1, 9, 6)]),
            Histogram("histogram", 8)
        ];
        var batched = DatabasePath("metrics-batched.duckdb");
        var single = DatabasePath("metrics-single.duckdb");
        await using (var store = new DuckDbStore(batched))
        {
            foreach (var request in new[] { first, second, first })
                await Insert(store, MetricsRequest(request));
        }

        await using (var store = new DuckDbStore(single))
        {
            foreach (var request in new[] { first, second, first })
            {
                foreach (var metric in request)
                {
                    foreach (var point in metric.Gauge?.DataPoints.ToArray() ?? [])
                        await Insert(store, MetricsRequest([Gauge(metric.Name, [point])]));
                    if (metric.Histogram is not { } histogram) continue;
                    foreach (var point in histogram.DataPoints.ToArray())
                    {
                        await Insert(store, MetricsRequest([new OpenTelemetry.Proto.Metrics.V1.Metric
                        {
                            Name = metric.Name,
                            Histogram = new OpenTelemetry.Proto.Metrics.V1.Histogram
                            {
                                AggregationTemporality = histogram.AggregationTemporality, DataPoints = { point }
                            }
                        }]));
                    }
                }
            }
        }

        string[] tables = ["metric_series", "metric_observations", "metric_points", "metric_exemplars"];
        var expected = StoreDump.Read(single);
        var actual = StoreDump.Read(batched);
        foreach (var table in tables)
        {
            Assert.IsNotEmpty(expected[table], table);
            CollectionAssert.AreEqual(expected[table], actual[table], table);
        }

        // The first of the three measurements is the one the point keeps.
        Assert.AreEqual(5d, Scalar(batched,
            "SELECT p.value FROM metric_points p JOIN metric_series s USING (project_id, series_id) " +
            "WHERE s.metric_name = 'conflict' AND p.time_unix_nano = 1000000001 AND p.has_conflict = 1"));

        async Task Insert(DuckDbStore store, OpenTelemetry.Proto.Collector.Metrics.V1.ExportMetricsServiceRequest request)
        {
            var rows = MetricStorageMapper.ToStorageRows(OtlpConverter.ConvertMetrics(request));
            await store.InsertMetricsAsync(rows.Series, rows.Observations, context.CancellationToken);
        }
    }

    /// <summary>
    ///     A JSON column refuses text that is not JSON, in a batch as in a single row. The batch is refused whole,
    ///     and the store writes the next one.
    /// </summary>
    [TestMethod]
    [DataRow(1, DisplayName = "One row")]
    [DataRow(3, DisplayName = "A batch")]
    public async Task Enqueue_RowWhoseJsonColumnIsNotJson_IsRefusedWholeAndTheStoreGoesOn(int count)
    {
        var path = DatabasePath("json.duckdb");
        var rows = Rows(count);
        rows[^1] = rows[^1] with { AttributesJson = "not json" };
        await using (var store = new DuckDbStore(path))
        {
            var refused = await Assert.ThrowsExactlyAsync<DuckDBException>(async () =>
                await store.EnqueueAsync(new SpanBatch(rows), context.CancellationToken));
            Assert.Contains("Malformed JSON", refused.Message);
            await store.EnqueueAsync(new SpanBatch(Rows(2)), context.CancellationToken);
        }

        Assert.AreEqual(2L, Scalar(path, "SELECT count(*) FROM spans"));
    }

    /// <summary>
    ///     The stored duration is the end minus the start, for every writer: the tools compute it from the bounds,
    ///     so a row that carries another number is refused before it is queued, and its batch with it.
    /// </summary>
    [TestMethod]
    [DataRow(1L, DisplayName = "One nanosecond more")]
    [DataRow(-1L, DisplayName = "One nanosecond less")]
    [DataRow(-1_000_000_000L, DisplayName = "Zero")]
    public async Task Enqueue_DurationThatIsNotEndMinusStart_IsRefusedWhole(long difference)
    {
        var path = DatabasePath("duration.duckdb");
        var rows = Rows(3);
        rows[1] = rows[1] with { DurationNs = (ulong)((long)rows[1].DurationNs + difference) };
        await using (var store = new DuckDbStore(path))
        {
            var refused = await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
                await store.EnqueueAsync(new SpanBatch(rows), context.CancellationToken));
            Assert.AreEqual("Span duration must equal its end time minus its start time.", refused.Message);
        }

        Assert.AreEqual(0L, Scalar(path, "SELECT count(*) FROM spans"));
    }

    /// <summary><paramref name="count" /> spans of one trace; span i is named "span i" and lasts one second.</summary>
    private static List<SpanStorageRow> Rows(int count)
    {
        var scope = new ScopeSpans();
        for (var i = 0; i < count; i++)
        {
            var id = new byte[8];
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(id, i + 1);
            scope.Spans.Add(new Span
            {
                TraceId = ByteString.CopyFrom(Enumerable.Repeat((byte)0xAB, 16).ToArray()),
                SpanId = ByteString.CopyFrom(id),
                Name = "span " + (uint)i,
                StartTimeUnixNano = 1_000_000_000UL + (ulong)i,
                EndTimeUnixNano = 2_000_000_000UL + (ulong)i,
                Attributes =
                {
                    new OpenTelemetry.Proto.Common.V1.KeyValue
                    {
                        Key = "ordinal", Value = new OpenTelemetry.Proto.Common.V1.AnyValue { IntValue = i }
                    }
                }
            });
        }

        var request = new ExportTraceServiceRequest { ResourceSpans = { new ResourceSpans { ScopeSpans = { scope } } } };
        return IngestionStorageMapper.ToSpanStorageRows(OtlpConverter.ConvertTraceRequest(request));
    }

    private static OpenTelemetry.Proto.Collector.Metrics.V1.ExportMetricsServiceRequest MetricsRequest(
        IEnumerable<OpenTelemetry.Proto.Metrics.V1.Metric> metrics) => new()
    {
        ResourceMetrics =
        {
            new OpenTelemetry.Proto.Metrics.V1.ResourceMetrics
            {
                ScopeMetrics = { new OpenTelemetry.Proto.Metrics.V1.ScopeMetrics { Metrics = { metrics } } }
            }
        }
    };

    private static OpenTelemetry.Proto.Metrics.V1.Metric Gauge(string name,
        IEnumerable<OpenTelemetry.Proto.Metrics.V1.NumberDataPoint> points) => new()
    {
        Name = name, Gauge = new OpenTelemetry.Proto.Metrics.V1.Gauge { DataPoints = { points } }
    };

    /// <summary>A point at <paramref name="time" />; <paramref name="exemplar" /> numbers the trace of its exemplar.</summary>
    private static OpenTelemetry.Proto.Metrics.V1.NumberDataPoint Point(int time, long value, int? exemplar = null,
        bool traced = true)
    {
        var point = new OpenTelemetry.Proto.Metrics.V1.NumberDataPoint
        {
            TimeUnixNano = 1_000_000_000UL + (ulong)time, AsInt = value
        };
        if (exemplar is not { } number) return point;
        var trace = new byte[16];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(trace, number + 1);
        point.Exemplars.Add(new OpenTelemetry.Proto.Metrics.V1.Exemplar
        {
            TimeUnixNano = point.TimeUnixNano, AsInt = value,
            TraceId = traced ? ByteString.CopyFrom(trace) : ByteString.Empty,
            SpanId = traced ? ByteString.CopyFrom(trace, 0, 8) : ByteString.Empty
        });
        return point;
    }

    private static OpenTelemetry.Proto.Metrics.V1.Metric Histogram(string name, ulong count)
    {
        var histogram = new OpenTelemetry.Proto.Metrics.V1.Histogram
        {
            AggregationTemporality = OpenTelemetry.Proto.Metrics.V1.AggregationTemporality.Delta
        };
        for (var time = 0; time < 5; time++)
        {
            histogram.DataPoints.Add(new OpenTelemetry.Proto.Metrics.V1.HistogramDataPoint
            {
                TimeUnixNano = 1_000_000_000UL + (ulong)time, StartTimeUnixNano = 1_000_000_000UL, Count = count,
                Sum = 12.5, ExplicitBounds = { 1, 5 }, BucketCounts = { 1, 2, count - 3 }
            });
        }

        return new OpenTelemetry.Proto.Metrics.V1.Metric { Name = name, Histogram = histogram };
    }

    private static object? Scalar(string path, string sql)
    {
        // The builder quotes a path that holds a separator of the connection string, such as ';' or '='.
        using var connection = new DuckDBConnection(
            new DuckDBConnectionStringBuilder { DataSource = path }.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private string DatabasePath(string name) =>
        Path.Combine(context.TestTempDirectory ?? throw new InvalidOperationException("No test directory"), name);
}
