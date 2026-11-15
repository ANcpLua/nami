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
