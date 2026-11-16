using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Nami.Collector.Ingestion;
using Nami.Collector.Storage;
using Nami.Tests.Infrastructure;
using Nami.Tools;
using OpenTelemetry.Proto.Common.V1;

namespace Nami.IntegrationTests;

/// <summary>
///     E4d. OTLP/JSON lets a 64-bit integer arrive as a JSON number or as a string, and it has to arrive exactly in
///     both forms. Google.Protobuf's JSON parser reads a number through a double, which holds 53 bits:
///     9007199254740993 sent as a number arrived as 9007199254740992.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class JsonIntegerTests(TestContext context)
{
    private const string Key = "gen_ai.usage.input_tokens";

    /// <summary>Through ingest over OTLP/HTTP JSON, storage and the answer of a tool.</summary>
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow("9007199254740993", false, DisplayName = "2^53+1 as a number")]
    [DataRow("9007199254740993", true, DisplayName = "2^53+1 as a string")]
    [DataRow("9223372036854775807", false, DisplayName = "2^63-1 as a number")]
    [DataRow("9223372036854775807", true, DisplayName = "2^63-1 as a string")]
    [DataRow("-9223372036854775808", false, DisplayName = "-2^63 as a number")]
    [DataRow("-9223372036854775808", true, DisplayName = "-2^63 as a string")]
    public async Task AnIntValue_ArrivesExactly_ThroughIngestStorageAndATool(string integer, bool asString)
    {
        var directory = context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");
        await using var host = await CollectorHost.StartAsync(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".duckdb"),
            context.CancellationToken);
        var literal = asString ? "\"" + integer + "\"" : integer;
        var start = TestData.Nanos(DateTimeOffset.UtcNow.AddSeconds(-2));
        var attributes = new JsonArray(new JsonObject
            { ["key"] = Key, ["value"] = new JsonObject { ["intValue"] = "placeholder" } });
        var export = OtlpData.Traces(start, attributes: attributes)
            .Replace("\"placeholder\"", literal, StringComparison.Ordinal);
        Assert.Contains("\"intValue\":" + literal, export);

        using (var response = await host.PostJsonAsync(Signals.Traces, export, context.CancellationToken))
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        using var input = JsonDocument.Parse("{\"trace_id\":\"" + 1.ToString("x32", CultureInfo.InvariantCulture) + "\"}");
        var answer = await new NamiToolService(host.Store).InvokeAsync("traces_get", input.RootElement,
            ProjectScope.DefaultProjectId, context.CancellationToken);
        Assert.IsFalse(answer.IsError, answer.Content.GetRawText());
        var span = Assert.ContainsSingle(answer.Content.GetProperty("trace").GetProperty("spans").EnumerateArray().ToArray());
        var attribute = Assert.ContainsSingle(span.GetProperty("attributes").EnumerateArray()
            .Where(static a => a.GetProperty("key").GetString() == Key).ToArray());
        Assert.AreEqual("int", attribute.GetProperty("value").GetProperty("kind").GetString());
        Assert.AreEqual(integer, attribute.GetProperty("value").GetProperty("value").GetString());
    }

    /// <summary>
    ///     Every 64-bit integer field is read the same way: the fixed64 times of a span and the counts beside them.
    ///     A nanosecond timestamp of today has nineteen digits.
    /// </summary>
    [TestMethod]
    public async Task TimesAndCountsSentAsNumbers_ArriveExactly()
    {
        const string Json = "{\"resourceSpans\":[{\"scopeSpans\":[{\"spans\":[{\"name\":\"span\",\"kind\":2," +
                            "\"startTimeUnixNano\":1794745800000000123,\"endTimeUnixNano\":18446744073709551615," +
                            "\"droppedAttributesCount\":4294967295,\"status\":{\"code\":1}," +
                            "\"attributes\":[{\"key\":\"exact\",\"value\":{\"intValue\":-9007199254740993}}," +
                            "{\"key\":\"small\",\"value\":{\"intValue\":42}}," +
                            "{\"key\":\"real\",\"value\":{\"doubleValue\":12345678901234567890}}," +
                            "{\"key\":\"fifteen\",\"value\":{\"intValue\":999999999999999}}," +
                            "{\"key\":\"sixteen\",\"value\":{\"intValue\":1000000000000000}}," +
                            "{\"key\":\"sixteen.real\",\"value\":{\"doubleValue\":1000000000000000}}," +
                            "{\"key\":\"fraction\",\"value\":{\"doubleValue\":1234567890.1234567}}]}]}]}]}";
        var request = new DefaultHttpContext().Request;
        request.Body = new MemoryStream(Encoding.UTF8.GetBytes(Json));

        var parsed = await OtlpPayloadParser.ParseTraceRequestAsync(request, OtlpPayloadEncoding.Json,
            context.CancellationToken);

        var span = parsed.ResourceSpans[0].ScopeSpans[0].Spans[0];
        Assert.AreEqual(1794745800000000123UL, span.StartTimeUnixNano);
        Assert.AreEqual(ulong.MaxValue, span.EndTimeUnixNano);
        Assert.AreEqual(uint.MaxValue, span.DroppedAttributesCount);
        Assert.AreEqual(OpenTelemetry.Proto.Trace.V1.Span.Types.SpanKind.Server, span.Kind);
        Assert.AreEqual(OpenTelemetry.Proto.Trace.V1.Status.Types.StatusCode.Ok, span.Status.Code);
        Assert.AreEqual(-9007199254740993L, span.Attributes[0].Value.IntValue);
        Assert.AreEqual(42L, span.Attributes[1].Value.IntValue);
        // A double field given a long integer holds the nearest double, as it did.
        Assert.AreEqual(12345678901234567890d, span.Attributes[2].Value.DoubleValue);
        // Fifteen digits are always below 2^53 and stay a number; sixteen digits below 2^53 take the string form
        // and arrive as the same integer, and as the same double in a double field.
        Assert.AreEqual(999_999_999_999_999L, span.Attributes[3].Value.IntValue);
        Assert.AreEqual(1_000_000_000_000_000L, span.Attributes[4].Value.IntValue);
        Assert.AreEqual(AnyValue.ValueOneofCase.DoubleValue, span.Attributes[5].Value.ValueCase);
        Assert.AreEqual(1e15, span.Attributes[5].Value.DoubleValue);
        Assert.AreEqual(1234567890.1234567, span.Attributes[6].Value.DoubleValue);
    }
}
