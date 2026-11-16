using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using Nami.Collector.Ingestion;
using Nami.Collector.Storage;
using Nami.Tests.Infrastructure;
using Nami.Tools;

namespace Nami.IntegrationTests;

/// <summary>
///     Telemetry is stored as it was sent, so what a tool needs in one form it derives when it reads: the event name
///     of a sender that names its events in the <c>event.name</c> attribute, the value of a deprecated key where its
///     successor is asked for, and a value too long for an answer, which the answer shortens and marks.
/// </summary>
[TestClass]
[TestCategory("Integration")]
[DependsOn(typeof(IngestStoreTests), nameof(IngestStoreTests.Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
public sealed class ReadSideDerivationTests(TestContext context)
{
    [TestMethod]
    public async Task ErrorsRecent_AnExceptionNamedOnlyInTheEventNameAttribute_CountsLikeOneNamedInTheField()
    {
        var clock = new FakeTimeProvider(TestData.s_now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        var time = TestData.Nanos(TestData.s_now.AddSeconds(-2));
        var field = JsonNode.Parse(OtlpData.Logs(time, exception: true))!;
        var attribute = JsonNode.Parse(OtlpData.Logs(time + 1, exception: true))!;
        var record = attribute["resourceLogs"]![0]!["scopeLogs"]![0]!["logRecords"]![0]!.AsObject();
        record.Remove("eventName");
        record["attributes"]!.AsArray().Add((JsonNode)OtlpData.Attribute("event.name", "exception"));
        await StorageTestData.SeedLogs(store, field.ToJsonString(), clock, context.CancellationToken);
        await StorageTestData.SeedLogs(store, attribute.ToJsonString(), clock, context.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));

        var result = await CallAsync(store, "errors_recent", new JsonObject());

        var group = Assert.ContainsSingle(result.GetProperty("groups").EnumerateArray().ToArray());
        Assert.AreEqual(2, group.GetProperty("count").GetInt64());
        Assert.AreEqual("OrderFailure", group.GetProperty("error_type").GetString());
    }

    [TestMethod]
    public async Task ChangesRecent_ADeprecatedKey_IsReadWhereItsSuccessorIsAskedFor()
    {
        var clock = new FakeTimeProvider(TestData.s_now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        var payload = JsonNode.Parse(OtlpData.Traces(TestData.Nanos(TestData.s_now.AddSeconds(-1))))!;
        payload["resourceSpans"]![0]!["resource"]!["attributes"]!.AsArray()
            .Add((JsonNode)OtlpData.Attribute("deployment.environment", "staging"));
        await StorageTestData.SeedTraces(store, payload.ToJsonString(), context.CancellationToken);
        _ = await CallAsync(store, "sources_status", new JsonObject());
        clock.Advance(TimeSpan.FromSeconds(1));

        var result = await CallAsync(store, "changes_recent", new JsonObject());

        var change = Assert.ContainsSingle(result.GetProperty("changes").EnumerateArray().ToArray());
        Assert.AreEqual("environment", change.GetProperty("kind").GetString());
        Assert.AreEqual("staging", change.GetProperty("to").GetString());
        Assert.AreEqual("deployment.environment", change.GetProperty("source_key").GetString(),
            "the answer names the key the value was stored under");
    }

    /// <summary>
    ///     Ingest keeps up to 256 KiB of a value; an answer is 32 KiB by default and 256 KiB at most. A span that
    ///     holds such a value is still answered: the answer carries the first 4096 bytes and says how many are stored.
    /// </summary>
    [TestMethod]
    [DataRow(32768, DisplayName = "max_bytes 32768, the default")]
    [DataRow(262144, DisplayName = "max_bytes 262144, the maximum")]
    public async Task TracesGet_ASpanWithAValueOfTheCap_IsAnswered_WithTheValueShortenedAndMarked(int maxBytes)
    {
        var clock = new FakeTimeProvider(TestData.s_now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        var large = new string('a', OtlpConverter.MaxValueUtf8Bytes);
        await StorageTestData.SeedTraces(store,
            OtlpData.Traces(TestData.Nanos(TestData.s_now.AddSeconds(-2)),
                attributes: new JsonArray(OtlpData.Attribute("acme.large", large))), context.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var trace = await CallAsync(store, "traces_get",
            new JsonObject { ["trace_id"] = 1.ToString("x32", CultureInfo.InvariantCulture), ["max_bytes"] = maxBytes });
        context.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"traces_get, one 256 KiB attribute, max_bytes {maxBytes}: {Encoding.UTF8.GetByteCount(trace.GetRawText())} bytes answered in {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms"));

        Assert.IsLessThanOrEqualTo(maxBytes, Encoding.UTF8.GetByteCount(trace.GetRawText()));
        var span = Assert.ContainsSingle(trace.GetProperty("trace").GetProperty("spans").EnumerateArray().ToArray());
        var value = Assert.ContainsSingle(span.GetProperty("attributes").EnumerateArray()
            .Where(static attribute => attribute.GetProperty("key").GetString() == "acme.large").ToArray())
            .GetProperty("value").GetString()!;
        Assert.AreEqual(ToolDispatcher.MaxAnswerValueUtf8Bytes, Encoding.UTF8.GetByteCount(value));
        Assert.StartsWith(new string('a', 1024), value);
        Assert.EndsWith("...[shortened in this answer: 262144 bytes stored]", value);
    }

    private async Task<JsonElement> CallAsync(DuckDbStore store, string tool, JsonObject input)
    {
        using var document = JsonDocument.Parse(input.ToJsonString());
        var result = await new NamiToolService(store)
            .InvokeAsync(tool, document.RootElement, ProjectScope.DefaultProjectId, context.CancellationToken);
        Assert.IsFalse(result.IsError, result.Content.GetRawText());
        return result.Content;
    }
}
