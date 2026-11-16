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
///     E3. A JSON string over the value cap (Claude Code's <c>tool_parameters</c>, a request body, GenAI messages) is
///     cut like any string, so what nami stores of it is no JSON any more. Nothing that reads the stored string may
///     throw on that or take it for a whole document: it falls back to the bounded text as it is stored, which ends in
///     the marker. nami never has the uncut value again. The reader of stored strings today is the credential stage
///     on the read side; the cases cut the value inside an object, inside a string and inside an escape.
/// </summary>
[TestClass]
[TestCategory("Integration")]
[DependsOn(typeof(IngestStoreTests), nameof(IngestStoreTests.Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
public sealed class CutJsonTests(TestContext context)
{
    private const int Cap = OtlpConverter.MaxValueUtf8Bytes;
    private const string Marker = "…[truncated]";
    private const int Payload = Cap - 14;

    private static string Json(string shape)
    {
        var text = new StringBuilder();
        switch (shape)
        {
            case "object":
                text.Append('{');
                for (var i = 0; text.Length < Cap + 4096; i++)
                    text.Append(CultureInfo.InvariantCulture, $"\"k{i:0000000}\":{i},");
                text.Length--;
                return text.Append('}').ToString();
            case "string":
                return "{\"full_command\":\"" + new string('a', Cap + 4096) + "\",\"timeout\":120000}";
            default:
                // {"text":" is nine bytes, so each two-byte escape \" starts at an odd offset and the cut, at an even
                // one, falls between its backslash and its quote.
                text.Append("{\"text\":\"");
                while (text.Length < Cap + 4096) text.Append("\\\"");
                return text.Append("\"}").ToString();
        }
    }

    [TestMethod]
    [DataRow("object", DisplayName = "Cut inside an object")]
    [DataRow("string", DisplayName = "Cut inside a string")]
    [DataRow("escape", DisplayName = "Cut inside an escape")]
    public async Task AJsonValueOverTheCap_IsStoredAsItsCutText_AndReadWithoutAnErrorAsThatText(string shape)
    {
        var sent = Json(shape);
        using (JsonDocument.Parse(sent)) { }

        var clock = new FakeTimeProvider(TestData.s_now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        await StorageTestData.SeedTraces(store,
            OtlpData.Traces(TestData.Nanos(TestData.s_now.AddSeconds(-2)),
                attributes: new JsonArray(OtlpData.Attribute("tool_parameters", sent))), context.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));

        var stored = (StoredAttributeJson.Attribute(
            Assert.ContainsSingle((await store.ReadSnapshotAsync("default", ct: context.CancellationToken)).Spans).AttributesJson,
            "tool_parameters") as Nami.Api.Contracts.Common.AttributeValue.StringValue)?.Value;
        Assert.IsNotNull(stored);
        Assert.AreEqual(sent[..Payload] + Marker, stored, "the bounded raw text, as sent up to the cut, and the marker");
        Assert.AreEqual(Cap, Encoding.UTF8.GetByteCount(stored));
        if (shape == "escape") Assert.EndsWith("\\" + Marker, stored, "the cut fell between the backslash and its quote");
        Assert.Throws<JsonException>(() => JsonDocument.Parse(stored).Dispose(), "what is stored is no whole document");

        // The read side's stage reads the cut text as text: no error, the same text back.
        Assert.AreEqual(stored, CredentialRedaction.Redact(stored));

        using var input = JsonDocument.Parse(new JsonObject
        {
            ["trace_id"] = 1.ToString("x32", CultureInfo.InvariantCulture), ["max_bytes"] = 262144
        }.ToJsonString());
        var answer = await new NamiToolService(store).InvokeAsync("traces_get", input.RootElement,
            ProjectScope.DefaultProjectId, context.CancellationToken);
        Assert.IsFalse(answer.IsError, answer.Content.GetRawText());
        var value = Assert.ContainsSingle(Assert.ContainsSingle(answer.Content.GetProperty("trace").GetProperty("spans").EnumerateArray().ToArray())
                .GetProperty("attributes").EnumerateArray()
                .Where(static attribute => attribute.GetProperty("key").GetString() == "tool_parameters").ToArray())
            .GetProperty("value").GetString()!;
        Assert.StartsWith(sent[..1024], value, "the answer carries the raw text");
        Assert.EndsWith("...[shortened in this answer: 262144 bytes stored]", value, "and says that it is not all of it");
    }
}
