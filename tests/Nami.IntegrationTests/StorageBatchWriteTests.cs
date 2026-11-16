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
