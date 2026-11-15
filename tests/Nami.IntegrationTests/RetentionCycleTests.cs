using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Nami.Collector.Storage;
using Nami.Retention;
using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

/// <summary>
///     One retention cycle as the hosted service runs it. Storage deletes a span only once no child span and no
///     log of its trace remains, so an expired trace leaves one level per pass and its logs must go first.
/// </summary>
[TestClass]
[TestCategory("Integration")]
[DependsOn(typeof(IngestStoreTests), nameof(IngestStoreTests.Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
public sealed class RetentionCycleTests(TestContext context)
{
    private static readonly string s_expiredTrace = 1.ToString("x32", CultureInfo.InvariantCulture);
    private static readonly string s_recentTrace = 11.ToString("x32", CultureInfo.InvariantCulture);

    [TestMethod]
    public async Task Cycle_ExpiredTraceTreeLogsAndMetrics_RemovedAndRecentKept()
    {
        var clock = new FakeTimeProvider(TestData.s_now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        var expired = TestData.Nanos(TestData.s_now.AddDays(-2));
        var recent = TestData.Nanos(TestData.s_now.AddHours(-1));
        await StorageTestData.SeedTraces(store, TraceTree(expired), context.CancellationToken);
        await StorageTestData.SeedLogs(store, OtlpData.Logs(expired, "expired", trace: s_expiredTrace,
            span: 3.ToString("x16", CultureInfo.InvariantCulture)), clock, context.CancellationToken);
        await StorageTestData.SeedMetrics(store, OtlpData.Metrics(expired, service: "expired"), context.CancellationToken);
        await StorageTestData.SeedTraces(store, OtlpData.Traces(recent, offset: 10), context.CancellationToken);
        await StorageTestData.SeedLogs(store, OtlpData.Logs(recent, "recent", trace: s_recentTrace,
            span: 11.ToString("x16", CultureInfo.InvariantCulture)), clock, context.CancellationToken);
        await StorageTestData.SeedMetrics(store, OtlpData.Metrics(recent), context.CancellationToken);

        var log = new CycleLog();
        using var service = new RetentionService(store, Options.Create(new RetentionOptions { Days = 1 }), clock, log);
        await service.StartAsync(context.CancellationToken);
        try
        {
            // The service waits ten seconds of its clock before the first cycle; advance until the cycle reports.
            while (!log.Finished.Task.IsCompleted)
            {
                clock.Advance(TimeSpan.FromSeconds(1));
                await Task.WhenAny(log.Finished.Task, Task.Delay(TimeSpan.FromMilliseconds(20), context.CancellationToken));
                context.CancellationToken.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.AreEqual(RetentionCompleted, await log.Finished.Task, "The retention cycle failed.");
        var snapshot = await store.ReadSnapshotAsync(ProjectScope.DefaultProjectId, ct: context.CancellationToken);
        Assert.AreSequenceEqual([s_recentTrace], snapshot.Spans.Select(static s => s.TraceId).ToArray());
        Assert.AreSequenceEqual([s_recentTrace], snapshot.Logs.Select(static l => l.TraceId).ToArray());
        Assert.AreSequenceEqual(["checkout"], snapshot.Metrics.Select(static m => m.ServiceName).ToArray());
    }

    private const int RetentionCompleted = 3001;

    /// <summary>A root span, its child and its grandchild in one trace that ended two days ago.</summary>
    private static string TraceTree(ulong start)
    {
        var payload = JsonNode.Parse(OtlpData.Traces(start, count: 3))!;
        var spans = payload["resourceSpans"]![0]!["scopeSpans"]![0]!["spans"]!.AsArray();
        for (var i = 0; i < spans.Count; i++)
        {
            spans[i]!["traceId"] = s_expiredTrace;
            if (i > 0) spans[i]!["parentSpanId"] = i.ToString("x16", CultureInfo.InvariantCulture);
        }

        return payload.ToJsonString();
    }

    /// <summary>Completes with the event id of the first cycle's completion or failure entry.</summary>
    private sealed class CycleLog : ILogger<RetentionService>
    {
        public TaskCompletionSource<int> Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id is 3001 or 3002) Finished.TrySetResult(eventId.Id);
        }
    }
}
