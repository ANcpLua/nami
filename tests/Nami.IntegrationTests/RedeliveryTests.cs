using DuckDB.NET.Data;
using Microsoft.Extensions.Time.Testing;
using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

/// <summary>
///     An export is acknowledged after its write. When the answer is lost on the way (a dropped connection, the end
///     of the stop budget) the sender has no acknowledgement and, by OTLP, sends the export again: nami receives a
///     stored export a second time (ledger N62; observed in N105). These tests hold what the store then holds, for
///     each signal: every row is keyed by what identifies its record, so the second delivery finds the rows of the
///     first and adds none. The only thing it moves is the watermark, which says when the service was last heard.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class RedeliveryTests(TestContext context)
{
    // The times are two seconds before TestData.s_now, the hosts' clock.
    // Two spans of one trace, with an event and a link.
    private const string Traces = """
        {"resourceSpans":[{"resource":{"attributes":[{"key":"service.name","value":{"stringValue":"sender"}}]},
          "scopeSpans":[{"scope":{"name":"redelivery"},"spans":[
            {"traceId":"5b8efff798038103d269b633813fc60c","spanId":"eee19b7ec3c1b174","name":"parent","kind":2,
             "startTimeUnixNano":"1792843198000000000","endTimeUnixNano":"1792843199000000000",
             "attributes":[{"key":"k","value":{"stringValue":"v"}}],
             "events":[{"timeUnixNano":"1792843198500000000","name":"event"}]},
            {"traceId":"5b8efff798038103d269b633813fc60c","spanId":"eee19b7ec3c1b175","parentSpanId":"eee19b7ec3c1b174",
             "name":"child","kind":3,"startTimeUnixNano":"1792843198100000000","endTimeUnixNano":"1792843198900000000",
             "links":[{"traceId":"5b8efff798038103d269b633813fc60d","spanId":"eee19b7ec3c1b176"}]}]}]}]}
        """;

    // A log record carries no identifier. The row's key is the record's whole content, and for a record with
    // neither a time nor an observed time also its place in the request. So of the two pairs of equal records
    // below, the pair with times is one row and the pair without is two: three rows.
    private const string Logs = """
        {"resourceLogs":[{"resource":{"attributes":[{"key":"service.name","value":{"stringValue":"sender"}}]},
          "scopeLogs":[{"scope":{"name":"redelivery"},"logRecords":[
            {"timeUnixNano":"1792843198000000000","observedTimeUnixNano":"1792843198000000001","severityNumber":9,
             "body":{"stringValue":"with a time"},"attributes":[{"key":"k","value":{"stringValue":"v"}}]},
            {"timeUnixNano":"1792843198000000000","observedTimeUnixNano":"1792843198000000001","severityNumber":9,
             "body":{"stringValue":"with a time"},"attributes":[{"key":"k","value":{"stringValue":"v"}}]},
            {"severityNumber":9,"body":{"stringValue":"without a time"}},
            {"severityNumber":9,"body":{"stringValue":"without a time"}}]}]}]}
        """;

    // Three gauge points, two of them at one timestamp with different values, one with an exemplar; and a histogram
    // point: four observations, three points.
    private const string Metrics = """
        {"resourceMetrics":[{"resource":{"attributes":[{"key":"service.name","value":{"stringValue":"sender"}}]},
          "scopeMetrics":[{"scope":{"name":"redelivery"},"metrics":[
            {"name":"gauge","unit":"1","gauge":{"dataPoints":[
              {"timeUnixNano":"1792843198000000000","asInt":"1",
               "exemplars":[{"timeUnixNano":"1792843198000000000","asInt":"1",
                 "traceId":"5b8efff798038103d269b633813fc60c","spanId":"eee19b7ec3c1b174"}]},
              {"timeUnixNano":"1792843199000000000","asInt":"2"},
              {"timeUnixNano":"1792843199000000000","asInt":"3"}]}},
            {"name":"histogram","histogram":{"aggregationTemporality":1,"dataPoints":[
              {"startTimeUnixNano":"1792843198000000000","timeUnixNano":"1792843199000000000","count":"3","sum":4.5,
               "bucketCounts":["1","2"],"explicitBounds":[1]}]}}]}]}]}
        """;

    [TestMethod]
    [DataRow(Signals.Traces, Traces, "spans", 2, DisplayName = "Spans: one row a trace and span id")]
    [DataRow(Signals.Logs, Logs, "logs", 3, DisplayName = "Log records: one row a content")]
    [DataRow(Signals.Metrics, Metrics, "metric_observations", 4, DisplayName = "Data points: one row a series, time and content")]
    public async Task Redelivery_OfAStoredExport_AddsNoRowAndChangesNone(string signal, string export, string table,
        int rows)
    {
        var path = Path.Combine(context.TestTempDirectory ?? throw new InvalidOperationException("No test directory"),
            signal + ".duckdb");
        var clock = new FakeTimeProvider(TestData.s_now);

        // Stored, and the answer never reaches the sender: the engine has stopped.
        await DeliverAsync(path, clock, signal, export);
        var stored = Read(path);
        var heard = LastHeard(path);
        // The sender's retry, half a minute later, to the engine that runs again.
        clock.Advance(TimeSpan.FromSeconds(30));
        await DeliverAsync(path, clock, signal, export);
        var redelivered = Read(path);

        Assert.HasCount(rows, stored[table], "The export did not reach the store: " + table);
        Assert.AreSequenceEqual(stored.Keys.ToArray(), redelivered.Keys.ToArray());
        foreach (var (name, expected) in stored)
            Assert.AreSequenceEqual(expected, redelivered[name], $"The retry changed {name}.");
        Assert.AreEqual(heard + 30_000_000_000m, LastHeard(path));
    }

    /// <summary>Starts a collector on <paramref name="path" />, sends the export and stops the collector again.</summary>
    private async Task DeliverAsync(string path, TimeProvider clock, string signal, string export)
    {
        await using var host = await CollectorHost.StartAsync(path, context.CancellationToken, clock: clock);
        await host.SendAsync(CollectorHost.ProtobufTransport, signal, export, context.CancellationToken);
        // A read waits until the accepted export is committed.
        _ = await host.Store.ReadSnapshotAsync("default", ct: context.CancellationToken);
    }

    /// <summary>
    ///     Every table but the cache of answered reads; without the random identifier of a resource observation and
    ///     without the two times of the watermark, which <see cref="LastHeard" /> reads.
    /// </summary>
    private static SortedDictionary<string, string[]> Read(string path) => StoreDump.Read(path, "read_snapshots",
        "resource_observations.observation_id", "signal_watermarks.last_seen_unix_nano",
        "signal_watermarks.watermark_unix_nano");

    private static decimal LastHeard(string path)
    {
        using var connection = new DuckDBConnection(
            new DuckDBConnectionStringBuilder { DataSource = path }.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT last_seen_unix_nano::DECIMAL(20, 0) FROM signal_watermarks";
        return Convert.ToDecimal(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
