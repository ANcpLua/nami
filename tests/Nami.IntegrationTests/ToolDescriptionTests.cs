using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using Nami.Collector.Storage;
using Nami.Tests.Infrastructure;
using Nami.Tools;

namespace Nami.IntegrationTests;

/// <summary>
///     What tools/list tells an agent about three answer members (<c>contracts/models/tools.tsp</c>): the
///     <c>source</c> of an error group, the <c>status</c> of a slow trace, and the <c>kind</c> and
///     <c>source_key</c> of a change. Each test ingests OTLP and calls the tool service, so a rule that changes
///     in the tool without its description fails here.
/// </summary>
[TestClass]
[TestCategory("Integration")]
[DependsOn(typeof(IngestStoreTests), nameof(IngestStoreTests.Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
public sealed class ToolDescriptionTests(TestContext context)
{
    private static string TraceId(int number)
    {
        return number.ToString("x32", CultureInfo.InvariantCulture);
    }

    private static string SpanId(int number)
    {
        return number.ToString("x16", CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     "Where the errors were recorded, the first that applies to any of them": log_event, then span_event,
    ///     then span_status. The group holds one error of each chosen kind, all of type OrderFailure in GET /orders,
    ///     and the span with only the error status is ingested first.
    /// </summary>
    [TestMethod]
    [DataRow(false, false, true, "span_status", DisplayName = "Status alone")]
    [DataRow(false, true, false, "span_event", DisplayName = "Span event alone")]
    [DataRow(true, false, false, "log_event", DisplayName = "Log event alone")]
    [DataRow(false, true, true, "span_event", DisplayName = "Span event before status")]
    [DataRow(true, false, true, "log_event", DisplayName = "Log event before status")]
    [DataRow(true, true, false, "log_event", DisplayName = "Log event before span event")]
    [DataRow(true, true, true, "log_event", DisplayName = "Log event before both")]
    public async Task ErrorsRecent_GroupSource_IsTheFirstOfLogEventSpanEventSpanStatus(bool logEvent, bool spanEvent,
        bool spanStatus, string expected)
    {
        var clock = new FakeTimeProvider(TestData.s_now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        var time = TestData.Nanos(TestData.s_now.AddSeconds(-5));
        if (spanStatus)
        {
            await StorageTestData.SeedTraces(store,
                OtlpData.Traces(time, error: true,
                    attributes: new JsonArray(OtlpData.Attribute("error.type", "OrderFailure"))),
                context.CancellationToken);
        }

        if (spanEvent)
        {
            await StorageTestData.SeedTraces(store,
                OtlpData.Traces(time, offset: 1, error: true, exceptionEvent: true), context.CancellationToken);
        }

        if (logEvent)
        {
            // The log names a span without an error of its own, which gives the log's error its operation.
            await StorageTestData.SeedTraces(store, OtlpData.Traces(time, offset: 2), context.CancellationToken);
            await StorageTestData.SeedLogs(store,
                OtlpData.Logs(time, exception: true, trace: TraceId(3), span: SpanId(3)), clock,
                context.CancellationToken);
        }

        clock.Advance(TimeSpan.FromSeconds(1));

        var result = await CallAsync(store, "errors_recent");

        var group = Assert.ContainsSingle(result.GetProperty("groups").EnumerateArray().ToArray());
        Assert.AreEqual("OrderFailure GET /orders",
            group.GetProperty("error_type").GetString() + " " + group.GetProperty("operation").GetString());
        Assert.AreEqual((logEvent ? 1 : 0) + (spanEvent ? 1 : 0) + (spanStatus ? 1 : 0),
            group.GetProperty("count").GetInt32(), "Every seeded error must be a member of the one group.");
        Assert.AreEqual(expected, group.GetProperty("source").GetString());
    }

    /// <summary>
    ///     "error when any span of the trace has status error, otherwise ok when any has status ok, otherwise
    ///     unset." The codes are the OTLP status codes of one trace's spans, the root span first.
    /// </summary>
    [TestMethod]
    [DataRow(new[] { 0, 0 }, "unset", DisplayName = "No span has a status")]
    [DataRow(new[] { 0, 1 }, "ok", DisplayName = "Ok on a child")]
    [DataRow(new[] { 1, 0 }, "ok", DisplayName = "Ok on the root")]
    [DataRow(new[] { 0, 2 }, "error", DisplayName = "Error on a child")]
    [DataRow(new[] { 1, 2 }, "error", DisplayName = "Error after ok")]
    [DataRow(new[] { 2, 1 }, "error", DisplayName = "Error before ok")]
    public async Task TracesSlowest_TraceStatus_IsErrorThenOkThenUnsetOverAllSpans(int[] codes, string expected)
    {
        var clock = new FakeTimeProvider(TestData.s_now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        var payload = JsonNode.Parse(OtlpData.Traces(TestData.Nanos(TestData.s_now.AddSeconds(-5)), codes.Length))!;
        var spans = payload["resourceSpans"]![0]!["scopeSpans"]![0]!["spans"]!.AsArray();
        for (var i = 0; i < codes.Length; i++)
        {
            spans[i]!["traceId"] = TraceId(1);
            spans[i]!["status"] = new JsonObject { ["code"] = codes[i] };
            if (i > 0) spans[i]!["parentSpanId"] = SpanId(1);
        }

        await StorageTestData.SeedTraces(store, payload.ToJsonString(), context.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));

        var result = await CallAsync(store, "traces_slowest");

        var trace = Assert.ContainsSingle(result.GetProperty("traces").EnumerateArray().ToArray());
        Assert.AreEqual(TraceId(1), trace.GetProperty("trace_id").GetString());
        Assert.AreEqual(expected, trace.GetProperty("status").GetString());
    }

    /// <summary>
    ///     "The resource attribute that changed, named in source_key: revision is vcs.ref.head.revision, version is
    ///     service.version, environment is deployment.environment.name, pipeline is any cicd.* attribute." A
    ///     resource attribute outside that list is no change.
    /// </summary>
    [TestMethod]
    public async Task ChangesRecent_KindAndSourceKey_NameTheResourceAttribute()
    {
        var clock = new FakeTimeProvider(TestData.s_now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        var payload = JsonNode.Parse(OtlpData.Traces(TestData.Nanos(TestData.s_now.AddSeconds(-5))))!;
        var resource = payload["resourceSpans"]![0]!["resource"]!["attributes"]!.AsArray();
        resource.Add((JsonNode)OtlpData.Attribute("host.name", "web-1"));
        resource.Add((JsonNode)OtlpData.Attribute("service.version", "1.4.0"));
        resource.Add((JsonNode)OtlpData.Attribute("cicd.pipeline.run.id", "run-17"));
        resource.Add((JsonNode)OtlpData.Attribute("vcs.ref.head.revision", "9f2c1e7"));
        resource.Add((JsonNode)OtlpData.Attribute("deployment.environment.name", "production"));
        resource.Add((JsonNode)OtlpData.Attribute("cicd.pipeline.name", "deploy"));
        await StorageTestData.SeedTraces(store, payload.ToJsonString(), context.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));

        var result = await CallAsync(store, "changes_recent");

        // One observation of one service: the rows are in the order of source_key.
        Assert.AreSequenceEqual(
        [
            "pipeline cicd.pipeline.name deploy",
            "pipeline cicd.pipeline.run.id run-17",
            "environment deployment.environment.name production",
            "version service.version 1.4.0",
            "revision vcs.ref.head.revision 9f2c1e7"
        ],
        [
            .. result.GetProperty("changes").EnumerateArray().Select(static change =>
                change.GetProperty("kind").GetString() + " " + change.GetProperty("source_key").GetString() + " " +
                change.GetProperty("to").GetString())
        ]);
    }

    private async Task<JsonElement> CallAsync(DuckDbStore store, string tool)
    {
        using var document = JsonDocument.Parse("{}");
        var result = await new NamiToolService(store)
            .InvokeAsync(tool, document.RootElement, ProjectScope.DefaultProjectId, context.CancellationToken);
        Assert.IsFalse(result.IsError, result.Content.GetRawText());
        return result.Content;
    }
}
