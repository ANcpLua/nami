using DuckDB.NET.Data;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using Nami.Collector.Storage;
using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
public sealed class StorageSchemaTests(TestContext context)
{
    [TestMethod]
    public async Task Open_MissingSchemaIdentityWithData_RejectsAdoptionWithoutChangingRows()
    {
        var path = Path.Combine(context.TestTempDirectory ?? throw new InvalidOperationException("No test directory"), "unidentified.duckdb");
        await using (var store = new DuckDbStore(path))
            await TestData.SeedTraces(store, OtlpData.Traces(TestData.Nanos(TestData.s_now)), context.CancellationToken);
        using (var connection = Open(path))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM nami_schema_meta";
            Assert.AreEqual(1, command.ExecuteNonQuery());
        }
        var failure = await Assert.ThrowsExactlyAsync<NamiSchemaMismatchException>(async () =>
        {
            await using var store = new DuckDbStore(path);
        });
        Assert.Contains("no current schema identity", failure.Message);
        using var check = Open(path);
        using var read = check.CreateCommand();
        read.CommandText = "SELECT (SELECT count(*) FROM spans), (SELECT count(*) FROM nami_schema_meta)";
        using var reader = read.ExecuteReader();
        Assert.IsTrue(reader.Read());
        Assert.AreEqual(1L, reader.GetInt64(0));
        Assert.AreEqual(0L, reader.GetInt64(1));
    }

    [TestMethod]
    public async Task Open_EmptyObsoleteMetadata_RecreatesTheGeneratedSchema()
    {
        var path = Path.Combine(context.TestTempDirectory ?? throw new InvalidOperationException("No test directory"), "empty.duckdb");
        using (var connection = Open(path))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE nami_schema_meta (obsolete INTEGER)";
            command.ExecuteNonQuery();
        }
        await using var store = new DuckDbStore(path);
        await TestData.SeedTraces(store, OtlpData.Traces(TestData.Nanos(TestData.s_now)), context.CancellationToken);
        Assert.ContainsSingle((await store.ReadSnapshotAsync("default", ct: context.CancellationToken)).Spans);
    }

    [TestMethod]
    public async Task LogSequence_RestartAndRetransmission_PreserveOrderAndStoredSequence()
    {
        var path = Path.Combine(context.TestTempDirectory ?? throw new InvalidOperationException("No test directory"), "sequence.duckdb");
        var clock = new FakeTimeProvider(TestData.s_now);
        var time = TestData.Nanos(TestData.s_now);
        var firstPayload = OtlpData.Logs(time, body: "first");
        long firstSequence;
        await using (var store = new DuckDbStore(path, timeProvider: clock))
        {
            await TestData.SeedLogs(store, firstPayload, clock, context.CancellationToken);
            firstSequence = Assert.ContainsSingle((await store.ReadSnapshotAsync("default", ct: context.CancellationToken)).Logs).IngestSequence;
            Assert.AreEqual(1L, firstSequence);
        }
        await using (var store = new DuckDbStore(path, timeProvider: clock))
        {
            await TestData.SeedLogs(store, firstPayload, clock, context.CancellationToken);
            await TestData.SeedLogs(store, OtlpData.Logs(time, body: "second"), clock, context.CancellationToken);
            var rows = (await store.ReadSnapshotAsync("default", ct: context.CancellationToken)).Logs;
            Assert.HasCount(2, rows);
            Assert.AreEqual(firstSequence, Assert.ContainsSingle(row => row.Body == "\"first\"", rows).IngestSequence);
            Assert.IsGreaterThan(firstSequence, Assert.ContainsSingle(row => row.Body == "\"second\"", rows).IngestSequence);
        }
    }

    [TestMethod]
    [DataRow("short", "longer", "longer")]
    [DataRow("longer", "short", "longer")]
    [DataRow("aa", "zz", "zz")]
    [DataRow("zz", "aa", "zz")]
    public async Task Upsert_MetricSeries_MergesDescriptionAndObservationBounds(string first, string second, string expected)
    {
        await using var store = new DuckDbStore(":memory:");
        var earlier = TestData.Nanos(TestData.s_now.AddSeconds(-20));
        var later = TestData.Nanos(TestData.s_now.AddSeconds(-10));
        await TestData.SeedMetrics(store, Payload(later, first), context.CancellationToken);
        await TestData.SeedMetrics(store, Payload(earlier, second), context.CancellationToken);
        var snapshot = await store.ReadSnapshotAsync("default", ct: context.CancellationToken);
        var series = Assert.ContainsSingle(snapshot.Metrics);
        Assert.AreEqual(expected, series.Description);
        Assert.AreEqual(earlier, series.FirstSeenUnixNano);
        Assert.AreEqual(later, series.LastSeenUnixNano);

        static string Payload(ulong time, string description)
        {
            var payload = JsonNode.Parse(OtlpData.Metrics(time))!;
            payload["resourceMetrics"]![0]!["scopeMetrics"]![0]!["metrics"]![0]!["description"] = description;
            return payload.ToJsonString();
        }
    }

    [TestMethod]
    public async Task Upsert_RepeatedLog_PreservesEarliestReceipt()
    {
        var clock = new FakeTimeProvider(TestData.s_now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        var payload = OtlpData.Logs(TestData.Nanos(TestData.s_now.AddMinutes(-1)));
        await TestData.SeedLogs(store, payload, clock, context.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(1));
        await TestData.SeedLogs(store, payload, clock, context.CancellationToken);
        var snapshot = await store.ReadSnapshotAsync("default", ct: context.CancellationToken);
        Assert.AreEqual(TestData.Nanos(TestData.s_now), Assert.ContainsSingle(snapshot.Logs).ReceivedTimeUnixNano);
    }

    [TestMethod]
    public async Task Upsert_SpanWithNullService_KeepsKnownService()
    {
        await using var store = new DuckDbStore(":memory:");
        var time = TestData.Nanos(TestData.s_now.AddSeconds(-10));
        await TestData.SeedTraces(store, OtlpData.Traces(time, service: "checkout"), context.CancellationToken);
        var before = await store.ReadSnapshotAsync("default", ct: context.CancellationToken);
        var original = Assert.ContainsSingle(before.Spans);
        await store.EnqueueAsync(new SpanBatch([original with { ServiceName = null }]), context.CancellationToken);
        var snapshot = await store.ReadSnapshotAsync("default", ct: context.CancellationToken);
        Assert.AreEqual("checkout", Assert.ContainsSingle(snapshot.Spans).ServiceName);
    }

    [TestMethod]
    public async Task Open_DifferentAuthoritativeHashWithData_RejectsWithoutRewritingDatabase()
    {
        var path = Path.Combine(context.TestTempDirectory ?? throw new InvalidOperationException("No test directory"),
            "schema.duckdb");
        await using (var store = new DuckDbStore(path))
            await TestData.SeedTraces(store, OtlpData.Traces(TestData.Nanos(TestData.s_now)), context.CancellationToken);

        using (var connection = Open(path))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE nami_schema_meta SET authoritative_schema_hash = 'earlier-private-build'";
            Assert.AreEqual(1, command.ExecuteNonQuery());
        }

        var failure = await Assert.ThrowsExactlyAsync<NamiSchemaMismatchException>(async () =>
        {
            await using var store = new DuckDbStore(path);
        });
        Assert.Contains("does not migrate earlier schemas", failure.Message);
        using var check = Open(path);
        using var read = check.CreateCommand();
        read.CommandText = "SELECT authoritative_schema_hash, (SELECT count(*) FROM spans) FROM nami_schema_meta";
        using var reader = read.ExecuteReader();
        Assert.IsTrue(reader.Read());
        Assert.AreEqual("earlier-private-build", reader.GetString(0));
        Assert.AreEqual(1L, reader.GetInt64(1));
        Assert.IsFalse(reader.Read());
    }

    [TestMethod]
    public async Task Open_CurrentSchema_RequiresCanonicalMetricPayloadAndInstrumentIdentity()
    {
        var path = Path.Combine(context.TestTempDirectory ?? throw new InvalidOperationException("No test directory"),
            "metric-schema.duckdb");
        await using (var store = new DuckDbStore(path))
            await TestData.SeedMetrics(store, OtlpData.Metrics(TestData.Nanos(TestData.s_now)), context.CancellationToken);
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT table_name, column_name, is_nullable FROM duckdb_columns()
            WHERE (table_name = 'metric_observations' AND column_name IN ('data_json', 'historical_projection_json', 'historical_exemplar_trace_ids_json'))
               OR (table_name = 'metric_series' AND column_name = 'instrument_id')
            ORDER BY table_name, column_name
            """;
        using var reader = command.ExecuteReader();
        var columns = new List<(string Table, string Column, bool Nullable)>();
        while (reader.Read()) columns.Add((reader.GetString(0), reader.GetString(1), reader.GetBoolean(2)));
        Assert.AreSequenceEqual([("metric_observations", "data_json", false), ("metric_series", "instrument_id", false)], columns);
    }

    private static DuckDBConnection Open(string path)
    {
        var connection = new DuckDBConnection($"DataSource={path};vacuum_rebuild_indexes={ulong.MaxValue}");
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}
