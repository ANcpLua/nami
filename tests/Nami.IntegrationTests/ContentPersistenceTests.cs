using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using Nami.Collector.Storage;
using Nami.SemanticConventions;
using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

/// <summary>
///     Finish-line item 5: nami stores no prompt, response or tool content, even when a sender sends
///     it. Each case ingests a span and a log record that carry one content key next to an ordinary
///     key, and reads both back from the store.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class ContentPersistenceTests(TestContext context)
{
    private const string OrdinaryKey = "http.request.method";

    [TestMethod]
    [DataRow("gen_ai.input.messages", DisplayName = "Prompt: gen_ai.input.messages")]
    [DataRow("gen_ai.output.messages", DisplayName = "Response: gen_ai.output.messages")]
    [DataRow("gen_ai.system_instructions", DisplayName = "System prompt: gen_ai.system_instructions")]
    [DataRow("gen_ai.tool.call.arguments", DisplayName = "Tool input: gen_ai.tool.call.arguments")]
    [DataRow("gen_ai.tool.call.result", DisplayName = "Tool output: gen_ai.tool.call.result")]
    [DataRow("gen_ai.tool.definitions", DisplayName = "Tool definitions: gen_ai.tool.definitions")]
    [DataRow("gen_ai.retrieval.documents", DisplayName = "Retrieved documents: gen_ai.retrieval.documents")]
    [DataRow("gen_ai.retrieval.query.text", DisplayName = "Retrieval query: gen_ai.retrieval.query.text")]
    [DataRow("gen_ai.memory.query.text", DisplayName = "Memory query: gen_ai.memory.query.text")]
    [DataRow("gen_ai.memory.records", DisplayName = "Memory records: gen_ai.memory.records")]
    [DataRow("gen_ai.prompt.variable.city", DisplayName = "Prompt variable: gen_ai.prompt.variable.<key>")]
    [DataRow("http.request.body.content", DisplayName = "Request body: http.request.body.content")]
    [DataRow("http.response.body.content", DisplayName = "Response body: http.response.body.content")]
    public async Task Ingest_ContentKey_IsNotStored(string key)
    {
        Assert.IsNotNull(AttributeCatalog.Find(key), key + " is a registry key, so the registry decides about it");
        var clock = new FakeTimeProvider(TestData.s_now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        var time = TestData.Nanos(TestData.s_now.AddSeconds(-1));
        var content = "CONTENT-OF-" + key;
        var attributes = new JsonArray(OtlpData.Attribute(OrdinaryKey, "POST"), OtlpData.Attribute(key, content));

        await StorageTestData.SeedTraces(store, OtlpData.Traces(time, attributes: attributes), context.CancellationToken);
        await StorageTestData.SeedLogs(store, Logs(time, attributes), clock, context.CancellationToken);
        var snapshot = await store.ReadSnapshotAsync("default", ct: context.CancellationToken);

        var span = Assert.ContainsSingle(snapshot.Spans).AttributesJson!;
        var log = Assert.ContainsSingle(snapshot.Logs).AttributesJson!;
        Assert.Contains("\"" + OrdinaryKey + "\":\"POST\"", span, "the ordinary key is stored");
        Assert.Contains("\"" + OrdinaryKey + "\":\"POST\"", log, "the ordinary key is stored");
        Assert.DoesNotContain(content, span);
        Assert.DoesNotContain(content, log);
        Assert.DoesNotContain(key, span);
        Assert.DoesNotContain(key, log);
    }

    private static string Logs(ulong time, JsonArray attributes) => new JsonObject
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
                    ["attributes"] = attributes.DeepClone()
                })
            })
        })
    }.ToJsonString();
}
