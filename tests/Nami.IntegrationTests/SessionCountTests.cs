using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using Nami.Collector.Storage;
using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

/// <summary>
///     <c>StorageStats.SessionCount</c>, which the DuckDB health check reports as <c>session_count</c>: the number of
///     distinct <c>session.id</c> values on the project's spans and log records. Until spans kept
///     <c>session.id</c> it was the number of traces.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class SessionCountTests(TestContext context)
{
    [TestMethod]
    public async Task StorageStats_SessionCount_CountsTheSessionsOfSpansAndLogs_NotTheTraces()
    {
        var clock = new FakeTimeProvider(TestData.s_now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        var time = TestData.Nanos(TestData.s_now.AddSeconds(-1));

        // Two traces of a sender without sessions, one session with a span and an event, one session with events only.
        await StorageTestData.SeedTraces(store, OtlpData.Traces(time, 2), context.CancellationToken);
        await StorageTestData.SeedTraces(store,
            OtlpData.Traces(time, offset: 2, attributes: new JsonArray(OtlpData.Attribute("session.id", "traced"))),
            context.CancellationToken);
        await StorageTestData.SeedLogs(store, Logs(time, "traced"), clock, context.CancellationToken);
        await StorageTestData.SeedLogs(store, Logs(time, "events-only"), clock, context.CancellationToken);
        await StorageTestData.SeedLogs(store, Logs(time + 1, "events-only"), clock, context.CancellationToken);

        var stats = await store.GetStorageStatsAsync("default", context.CancellationToken);
        var snapshot = await store.ReadSnapshotAsync("default", ct: context.CancellationToken);

        Assert.HasCount(3, snapshot.Spans);
        Assert.HasCount(3, snapshot.Logs);
        Assert.AreEqual(3L, stats.SpanCount);
        Assert.AreEqual(2L, stats.SessionCount);
    }

    private static string Logs(ulong time, string session) => new JsonObject
    {
        ["resourceLogs"] = new JsonArray(new JsonObject
        {
            ["resource"] = new JsonObject
            {
                ["attributes"] = new JsonArray(OtlpData.Attribute("nami.project.id", "default"),
                    OtlpData.Attribute("service.name", "checkout"))
            },
            ["scopeLogs"] = new JsonArray(new JsonObject
            {
                ["scope"] = new JsonObject { ["name"] = "nami-tests" },
                ["logRecords"] = new JsonArray(new JsonObject
                {
                    ["timeUnixNano"] = time.ToString(CultureInfo.InvariantCulture), ["severityNumber"] = 9,
                    ["body"] = new JsonObject { ["stringValue"] = "turn completed" },
                    ["attributes"] = new JsonArray(OtlpData.Attribute("session.id", session))
                })
            })
        })
    }.ToJsonString();
}
