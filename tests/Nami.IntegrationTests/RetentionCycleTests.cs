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
///     Retention as the hosted service runs it and as storage decides it. An expired span stays while a log of its
///     trace remains or a span below it stays, so the logs go first.
/// </summary>
[TestClass]
[TestCategory("Integration")]
[DependsOn(typeof(IngestStoreTests), nameof(IngestStoreTests.Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
public sealed class RetentionCycleTests(TestContext context)
{
    private static readonly string s_expiredTrace = 1.ToString("x32", CultureInfo.InvariantCulture);
    private static readonly string s_recentTrace = 11.ToString("x32", CultureInfo.InvariantCulture);
    private static readonly int[] s_heldSpans = [0x31, 0x32, 0x33, 0x41, 0x42, 0x43, 0x51, 0x52];

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

    /// <summary>
    ///     A span that names itself as its parent and spans that name each other in a circle have no end to wait
    ///     for: they expire with their trace. A circle stays, like any parent, while a span below it has not
    ///     expired or a log names one.
    /// </summary>
    [TestMethod]
    public async Task Expired_SelfParentedSpanAndParentCycle_RemovedUnlessSomethingBelowStays()
    {
        var clock = new FakeTimeProvider(TestData.s_now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        var expired = TestData.Nanos(TestData.s_now.AddDays(-2));
        var recent = TestData.Nanos(TestData.s_now.AddHours(-1));
        await StorageTestData.SeedTraces(store, Spans(
            // Trace 1: its own parent.
            (1, 0x11, 0x11, expired),
            // Trace 2: two spans that name each other, and a child of one of them.
            (2, 0x21, 0x22, expired), (2, 0x22, 0x21, expired), (2, 0x23, 0x21, expired),
            // Trace 3: the same circle, with a child that has not expired.
            (3, 0x31, 0x32, expired), (3, 0x32, 0x31, expired), (3, 0x33, 0x31, recent),
            // Trace 4: the same circle, with an expired child that a log without a trace id names.
            (4, 0x41, 0x42, expired), (4, 0x42, 0x41, expired), (4, 0x43, 0x41, expired),
            // Trace 5: an expired parent of a span that has not expired.
            (5, 0x51, null, expired), (5, 0x52, 0x51, recent)), context.CancellationToken);
        await StorageTestData.SeedLogs(store, OtlpData.Logs(recent, span: Id(0x43)), clock,
            context.CancellationToken);

        var deleted = await store.DeleteExpiredAsync(TestData.Nanos(TestData.s_now.AddDays(-1)),
            context.CancellationToken);

        var snapshot = await store.ReadSnapshotAsync(ProjectScope.DefaultProjectId, ct: context.CancellationToken);
        Assert.AreSequenceEqual(s_heldSpans.Select(Id).ToArray(),
            snapshot.Spans.Select(static s => s.SpanId).Order(StringComparer.Ordinal).ToArray());
        Assert.AreEqual(4L, deleted.Spans);
        Assert.AreEqual(0L, deleted.Logs);

        static string Id(int span) => span.ToString("x16", CultureInfo.InvariantCulture);

        static string Spans(params (int Trace, int Span, int? Parent, ulong Start)[] spans)
        {
            var payload = JsonNode.Parse(OtlpData.Traces(spans[0].Start, count: spans.Length))!;
            var rows = payload["resourceSpans"]![0]!["scopeSpans"]![0]!["spans"]!.AsArray();
            for (var i = 0; i < spans.Length; i++)
            {
                rows[i]!["traceId"] = spans[i].Trace.ToString("x32", CultureInfo.InvariantCulture);
                rows[i]!["spanId"] = Id(spans[i].Span);
                if (spans[i].Parent is { } parent) rows[i]!["parentSpanId"] = Id(parent);
                rows[i]!["startTimeUnixNano"] = spans[i].Start.ToString(CultureInfo.InvariantCulture);
                rows[i]!["endTimeUnixNano"] = (spans[i].Start + 1000000UL).ToString(CultureInfo.InvariantCulture);
            }

            return payload.ToJsonString();
        }
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
