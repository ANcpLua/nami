using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using Nami.Collector.Storage;
using Nami.Tests.Infrastructure;
using Nami.Tools;

namespace Nami.IntegrationTests;

/// <summary>
///     What an agent reads when a stored attribute value or log body reaches a tool answer. Each test ingests
///     OTLP and calls the tool service, the interface the engine protocol calls, so the tests hold whatever the
///     snapshot and the storage columns look like.
/// </summary>
[TestClass]
[TestCategory("Integration")]
[DependsOn(typeof(IngestStoreTests), nameof(IngestStoreTests.Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
public sealed class StoredValueToolTests(TestContext context)
{
    // Ingest keeps only registry keys inside a map body, so the map uses two of them: a string and an integer.
    private const string MapBody =
        "{\"kvlistValue\":{\"values\":[{\"key\":\"http.request.method\",\"value\":{\"stringValue\":\"GET\"}}," +
        "{\"key\":\"gen_ai.usage.input_tokens\",\"value\":{\"intValue\":\"3\"}}]}}";

    [TestMethod]
    [DataRow("{\"stringValue\":\"order accepted\"}", "order accepted", DisplayName = "String body")]
    [DataRow("{\"boolValue\":true}", "true", DisplayName = "Boolean body")]
    public async Task LogsSearch_StringOrBooleanBody_ReadsAsItsText(string body, string expected)
    {
        var record = Assert.ContainsSingle((await SearchAsync(body, expected)).EnumerateArray().ToArray());
        Assert.AreEqual(expected, record.GetProperty("body").GetString());
    }

    [TestMethod]
    public async Task TracesGet_IntegerAttribute_KeepsItsContractShape()
    {
        var clock = new FakeTimeProvider(TestData.s_now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        var attributes = new JsonArray(new JsonObject
            { ["key"] = "gen_ai.usage.input_tokens", ["value"] = new JsonObject { ["intValue"] = "42" } });
        await StorageTestData.SeedTraces(store,
            OtlpData.Traces(TestData.Nanos(TestData.s_now.AddSeconds(-2)), attributes: attributes),
            context.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));

        var trace = await CallAsync(store, "traces_get",
            new JsonObject { ["trace_id"] = 1.ToString("x32", CultureInfo.InvariantCulture) });

        var span = Assert.ContainsSingle(trace.GetProperty("trace").GetProperty("spans").EnumerateArray().ToArray());
        var attribute = Assert.ContainsSingle(span.GetProperty("attributes").EnumerateArray()
            .Where(static a => a.GetProperty("key").GetString() == "gen_ai.usage.input_tokens").ToArray());
        Assert.AreEqual("int", attribute.GetProperty("value").GetProperty("kind").GetString());
        Assert.AreEqual("42", attribute.GetProperty("value").GetProperty("value").GetString());
    }

    [TestMethod]
    [DataRow("{\"intValue\":\"42\"}", "42", DisplayName = "Integer body")]
    [DataRow("{\"doubleValue\":1.5}", "1.5", DisplayName = "Double body")]
    public async Task LogsSearch_NumberBody_ReadsAsTheNumber(string body, string expected)
    {
        var record = Assert.ContainsSingle((await SearchAsync(body, expected)).EnumerateArray().ToArray());
        Assert.AreEqual(expected, record.GetProperty("body").GetString());
    }

    [TestMethod]
    [DataRow("{\"intValue\":\"42\"}", DisplayName = "Integer body")]
    [DataRow(MapBody, DisplayName = "Map body")]
    public async Task LogsSearch_StoredEncodingTag_MatchesNothing(string body)
    {
        // Storage tags a stored number or map with "type"; the record itself does not contain the word.
        Assert.AreEqual(0, (await SearchAsync(body, "type")).GetArrayLength());
    }

    [TestMethod]
    public async Task LogsSearch_MapBody_ReadsAsTheContractEncoding()
    {
        var record = Assert.ContainsSingle((await SearchAsync(MapBody, "GET")).EnumerateArray().ToArray());
        Assert.AreEqual(
            "{\"kind\":\"kvlist\",\"values\":{\"gen_ai.usage.input_tokens\":{\"kind\":\"int\",\"value\":\"3\"}," +
            "\"http.request.method\":\"GET\"}}",
            record.GetProperty("body").GetString());
    }

    [TestMethod]
    public async Task ChangesRecent_IntegerVersion_ReportsTheNumbers()
    {
        var clock = new FakeTimeProvider(TestData.s_now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        await SeedVersion(1, 0);
        clock.Advance(TimeSpan.FromSeconds(2));
        await SeedVersion(2, 1);
        clock.Advance(TimeSpan.FromSeconds(1));

        var result = await CallAsync(store, "changes_recent", new JsonObject { ["since"] = "2026-10-24T12:00:01Z" });

        var change = Assert.ContainsSingle(result.GetProperty("changes").EnumerateArray().ToArray());
        Assert.AreEqual("1", change.GetProperty("from").GetString());
        Assert.AreEqual("2", change.GetProperty("to").GetString());
        return;

        async Task SeedVersion(int version, int id)
        {
            var payload = JsonNode.Parse(OtlpData.Traces(TestData.Nanos(TestData.s_now.AddSeconds(-1)), offset: id))!;
            payload["resourceSpans"]![0]!["resource"]!["attributes"]!.AsArray().Add((JsonNode)new JsonObject
            {
                ["key"] = "service.version",
                ["value"] = new JsonObject { ["intValue"] = version.ToString(CultureInfo.InvariantCulture) }
            });
            await StorageTestData.SeedTraces(store, payload.ToJsonString(), context.CancellationToken);
            _ = await CallAsync(store, "sources_status", new JsonObject());
        }
    }

    private async Task<JsonElement> SearchAsync(string body, string query)
    {
        var clock = new FakeTimeProvider(TestData.s_now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        var payload = JsonNode.Parse(OtlpData.Logs(TestData.Nanos(TestData.s_now.AddSeconds(-2))))!;
        payload["resourceLogs"]![0]!["scopeLogs"]![0]!["logRecords"]![0]!["body"] = JsonNode.Parse(body);
        await StorageTestData.SeedLogs(store, payload.ToJsonString(), clock, context.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));
        return (await CallAsync(store, "logs_search", new JsonObject { ["query"] = query })).GetProperty("records");
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
