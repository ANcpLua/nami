using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Microsoft.Extensions.Time.Testing;
using Nami.Collector.Storage;
using Nami.Tests.Infrastructure;
using Nami.Tools;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace Nami.IntegrationTests;

/// <summary>
///     Stored as sent, end to end: every kind of OTLP value goes in over each transport, through ingest and the
///     store, and comes back in a tool answer with its type and value intact. The cases are those the data model
///     of the OpenTelemetry Common Specification allows and nami chooses to keep (empty value, empty string, zero, empty array, a null inside an array,
///     key case) and those a JSON answer can lose (64-bit integers beyond 2^53, NaN and the infinities, bytes, a
///     string of digits), with arrays and maps mixed down to the depth bound.
///     OTLP/JSON, the third transport, also ignores unknown fields, takes a 64-bit integer as a number or a string,
///     and answers every request, an error included, as application/json.
/// </summary>
[TestClass]
[TestCategory("Integration")]
[DependsOn(typeof(IngestStoreTests), nameof(IngestStoreTests.Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
public sealed class ValueRoundTripTests(TestContext context)
{
    private const string TraceId = "0af7651916cd43dd8448eb211c80319c";

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    [DataRow(CollectorHost.JsonTransport, DisplayName = "OTLP/HTTP JSON")]
    [DataRow(CollectorHost.ProtobufTransport, DisplayName = "OTLP/HTTP protobuf")]
    [DataRow(CollectorHost.GrpcTransport, DisplayName = "OTLP/gRPC")]
    public async Task EveryKindOfValue_ThroughIngestStoreAndToolAnswer_KeepsItsTypeAndValue(string transport)
    {
        var clock = new FakeTimeProvider(TestData.s_now);
        var time = TestData.Nanos(TestData.s_now.AddSeconds(-2));
        var nested = new AnyValue { IntValue = 7 };
        for (var level = 0; level < 16; level++)
        {
            nested = level % 2 == 0
                ? new AnyValue { ArrayValue = new ArrayValue { Values = { nested, new AnyValue { StringValue = "beside" } } } }
                : new AnyValue { KvlistValue = new KeyValueList { Values = { new KeyValue { Key = "Inner.Key", Value = nested } } } };
        }

        var span = new Span
        {
            TraceId = ByteString.CopyFrom(Convert.FromHexString(TraceId)),
            SpanId = ByteString.CopyFrom(Convert.FromHexString("b7ad6b7169203331")),
            Name = "values", StartTimeUnixNano = time, EndTimeUnixNano = time + 1_000_000,
            Attributes =
            {
                new KeyValue { Key = "acme.empty", Value = new AnyValue() },
                new KeyValue { Key = "acme.empty_string", Value = new AnyValue { StringValue = "" } },
                new KeyValue { Key = "acme.zero", Value = new AnyValue { IntValue = 0 } },
                new KeyValue { Key = "acme.empty_array", Value = new AnyValue { ArrayValue = new ArrayValue() } },
                new KeyValue { Key = "acme.empty_map", Value = new AnyValue { KvlistValue = new KeyValueList() } },
                new KeyValue
                {
                    Key = "acme.null_inside",
                    Value = new AnyValue { ArrayValue = new ArrayValue { Values = { new AnyValue { StringValue = "a" }, new AnyValue(), new AnyValue { BoolValue = true } } } }
                },
                new KeyValue { Key = "acme.bytes", Value = new AnyValue { BytesValue = ByteString.CopyFrom(0, 1, 2, 250, 255) } },
                new KeyValue { Key = "acme.int_min", Value = new AnyValue { IntValue = long.MinValue } },
                new KeyValue { Key = "acme.int_max", Value = new AnyValue { IntValue = long.MaxValue } },
                new KeyValue { Key = "acme.int_past_2_53", Value = new AnyValue { IntValue = 9007199254740993 } },
                new KeyValue { Key = "acme.nan", Value = new AnyValue { DoubleValue = double.NaN } },
                new KeyValue { Key = "acme.infinity", Value = new AnyValue { DoubleValue = double.PositiveInfinity } },
                new KeyValue { Key = "acme.minus_infinity", Value = new AnyValue { DoubleValue = double.NegativeInfinity } },
                new KeyValue { Key = "acme.double", Value = new AnyValue { DoubleValue = 0.1 } },
                new KeyValue { Key = "acme.digits", Value = new AnyValue { StringValue = "0042" } },
                new KeyValue { Key = "acme.nested", Value = nested },
                new KeyValue { Key = "Acme.KeyCase", Value = new AnyValue { StringValue = "upper" } },
                new KeyValue { Key = "acme.keycase", Value = new AnyValue { StringValue = "lower" } }
            }
        };
        var request = new ExportTraceServiceRequest
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
                    Resource = new Resource { Attributes = { new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = "checkout" } } } },
                    ScopeSpans = { new ScopeSpans { Spans = { span } } }
                }
            }
        };
        var path = Path.Combine(
            context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"),
            "values-" + transport + ".duckdb");

        JsonElement answer;
        await using (var host = await CollectorHost.StartAsync(path, context.CancellationToken, clock: clock))
        {
            switch (transport)
            {
                case CollectorHost.JsonTransport:
                    {
                        using var response = await host.PostJsonAsync(Signals.Traces, CredentialGateTests.OtlpJson(request), context.CancellationToken);
                        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(context.CancellationToken));
                        break;
                    }
                case CollectorHost.ProtobufTransport:
                    {
                        using var response = await host.PostProtobufAsync(Signals.Traces, request.ToByteArray(), context.CancellationToken);
                        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                        break;
                    }
                default:
                    _ = await host.ExportGrpcAsync(Signals.Traces, request.ToByteArray(), context.CancellationToken);
                    break;
            }

            clock.Advance(TimeSpan.FromSeconds(1));
            using var input = JsonDocument.Parse(new JsonObject { ["trace_id"] = TraceId, ["max_bytes"] = 262144 }.ToJsonString());
            var result = await new NamiToolService(host.Store).InvokeAsync("traces_get", input.RootElement,
                ProjectScope.DefaultProjectId, context.CancellationToken);
            Assert.IsFalse(result.IsError, result.Content.GetRawText());
            answer = result.Content.Clone();
        }

        var attributes = Assert.ContainsSingle(answer.GetProperty("trace").GetProperty("spans").EnumerateArray().ToArray())
            .GetProperty("attributes").EnumerateArray()
            .ToDictionary(static attribute => attribute.GetProperty("key").GetString()!,
                static attribute => attribute.TryGetProperty("value", out var value) ? value : default, StringComparer.Ordinal);
        context.WriteLine(answer.GetRawText());

        Assert.IsTrue(attributes["acme.empty"].ValueKind is JsonValueKind.Null or JsonValueKind.Undefined, "an empty value stays empty");
        Assert.AreEqual("", attributes["acme.empty_string"].GetString());
        Assert.AreEqual("0", Integer(attributes["acme.zero"]));
        Assert.AreEqual(0, attributes["acme.empty_array"].GetArrayLength());
        Assert.AreEqual("kvlist", attributes["acme.empty_map"].GetProperty("kind").GetString());
        Assert.IsEmpty(attributes["acme.empty_map"].GetProperty("values").EnumerateObject().ToArray());
        var inside = attributes["acme.null_inside"].EnumerateArray().ToArray();
        Assert.HasCount(3, inside);
        Assert.AreEqual("a", inside[0].GetString());
        Assert.AreEqual(JsonValueKind.Null, inside[1].ValueKind, "a null inside an array stays where it was");
        Assert.AreEqual(JsonValueKind.True, inside[2].ValueKind);
        Assert.AreEqual("bytes", attributes["acme.bytes"].GetProperty("kind").GetString());
        byte[] sent = [0, 1, 2, 250, 255];
        Assert.AreSequenceEqual(sent, attributes["acme.bytes"].GetProperty("base64").GetBytesFromBase64());
        // A 64-bit integer is answered as text, so a JavaScript client reads every digit.
        Assert.AreEqual("-9223372036854775808", Integer(attributes["acme.int_min"]));
        Assert.AreEqual("9223372036854775807", Integer(attributes["acme.int_max"]));
        Assert.AreEqual("9007199254740993", Integer(attributes["acme.int_past_2_53"]));
        Assert.AreEqual("NaN", Double(attributes["acme.nan"]));
        Assert.AreEqual("Infinity", Double(attributes["acme.infinity"]));
        Assert.AreEqual("-Infinity", Double(attributes["acme.minus_infinity"]));
        Assert.AreEqual("0.1", Double(attributes["acme.double"]));
        Assert.AreEqual(JsonValueKind.String, attributes["acme.digits"].ValueKind, "a string of digits stays a string");
        Assert.AreEqual("0042", attributes["acme.digits"].GetString());
        Assert.AreEqual("upper", attributes["Acme.KeyCase"].GetString());
        Assert.AreEqual("lower", attributes["acme.keycase"].GetString());

        var value = attributes["acme.nested"];
        for (var level = 15; level >= 0; level--)
        {
            if (level % 2 == 0)
            {
                Assert.AreEqual(JsonValueKind.Array, value.ValueKind, "level " + level.ToString(CultureInfo.InvariantCulture));
                Assert.AreEqual("beside", value[1].GetString());
                value = value[0];
            }
            else
            {
                Assert.AreEqual("kvlist", value.GetProperty("kind").GetString(), "level " + level.ToString(CultureInfo.InvariantCulture));
                value = value.GetProperty("values").GetProperty("Inner.Key");
            }
        }

        Assert.AreEqual("7", Integer(value), "the value at the depth bound");

        static string Integer(JsonElement value)
        {
            Assert.AreEqual("int", value.GetProperty("kind").GetString());
            Assert.AreEqual(JsonValueKind.String, value.GetProperty("value").ValueKind);
            return value.GetProperty("value").GetString()!;
        }

        static string Double(JsonElement value)
        {
            Assert.AreEqual("double", value.GetProperty("kind").GetString());
            var number = value.GetProperty("value");
            return number.ValueKind == JsonValueKind.String ? number.GetString()! : number.GetRawText();
        }
    }

    /// <summary>
    ///     OTLP/JSON: a decoder ignores fields it does not know and takes a 64-bit integer as a number or as a string.
    ///     The second row is red at this commit: a JSON number above 2^53 loses its last bit in the protobuf JSON
    ///     parser (9007199254740993 arrives as 9007199254740992). The parser belongs to ingress/bounded-parse.
    /// </summary>
    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    [DataRow("9007199254740991", DisplayName = "A number a double holds exactly")]
    public async Task OtlpJson_UnknownFieldsAreIgnored_AndA64BitIntegerIsTakenAsNumberOrString(string number)
    {
        var clock = new FakeTimeProvider(TestData.s_now);
        var time = TestData.Nanos(TestData.s_now.AddSeconds(-2)).ToString(CultureInfo.InvariantCulture);
        var json = """
            {"futureField":{"x":1},"resourceSpans":[{"resource":{"attributes":[{"key":"service.name","value":{"stringValue":"checkout"}}],"futureField":true},
             "scopeSpans":[{"spans":[{"traceId":"TRACE","spanId":"b7ad6b7169203331","name":"json","futureField":[1,2],
               "startTimeUnixNano":TIME,"endTimeUnixNano":"TIME",
               "attributes":[{"key":"acme.as_number","value":{"intValue":NUMBER}},
                             {"key":"acme.as_string","value":{"intValue":"9007199254740993","futureField":"x"}}]}]}]}]}
            """.Replace("TRACE", TraceId, StringComparison.Ordinal).Replace("TIME", time, StringComparison.Ordinal)
            .Replace("NUMBER", number, StringComparison.Ordinal);
        var path = Path.Combine(
            context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"), "json.duckdb");
        await using var host = await CollectorHost.StartAsync(path, context.CancellationToken, clock: clock);

        using var response = await host.PostJsonAsync(Signals.Traces, json, context.CancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(context.CancellationToken));
        Assert.AreEqual("application/json", response.Content.Headers.ContentType?.MediaType);
        var span = Assert.ContainsSingle((await host.Store.ReadSnapshotAsync("default", ct: context.CancellationToken)).Spans);
        Assert.Contains("\"acme.as_number\":{\"type\":\"int\",\"value\":\"" + number + "\"}", span.AttributesJson!);
        Assert.Contains("\"acme.as_string\":{\"type\":\"int\",\"value\":\"9007199254740993\"}", span.AttributesJson!);
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    [DataRow("unparseable", HttpStatusCode.BadRequest, DisplayName = "JSON that does not parse")]
    [DataRow("accepted", HttpStatusCode.OK, DisplayName = "A span that is accepted")]
    [DataRow("refused", HttpStatusCode.OK, DisplayName = "A span that is refused through partial success")]
    public async Task OtlpJson_EveryResponse_IsApplicationJson_AnErrorIncluded(string kind, HttpStatusCode expected)
    {
        // Refused: five priority attributes of 256 KiB do not fit one record, so the span goes and the request stays.
        var usage = string.Join(",", "abcde".Select(static name =>
            "{\"key\":\"gen_ai.usage." + name + "\",\"value\":{\"stringValue\":\"" + new string('a', 262_144) + "\"}}"));
        var span = "{\"resourceSpans\":[{\"scopeSpans\":[{\"spans\":[{\"traceId\":\"" + TraceId +
                   "\",\"spanId\":\"b7ad6b7169203331\",\"name\":\"s\",\"startTimeUnixNano\":\"1794745800000000000\",\"endTimeUnixNano\":\"1794745800000000001\",\"attributes\":[ATTRIBUTES]}]}]}]}";
        var body = kind switch
        {
            "unparseable" => "{\"resourceSpans\":",
            "accepted" => span.Replace("ATTRIBUTES", "", StringComparison.Ordinal),
            _ => span.Replace("ATTRIBUTES", usage, StringComparison.Ordinal)
        };
        var path = Path.Combine(
            context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"), "json-errors.duckdb");
        await using var host = await CollectorHost.StartAsync(path, context.CancellationToken);

        using var response = await host.PostJsonAsync(Signals.Traces, body, context.CancellationToken);

        var text = await response.Content.ReadAsStringAsync(context.CancellationToken);
        Assert.AreEqual(expected, response.StatusCode, text);
        Assert.AreEqual("application/json", response.Content.Headers.ContentType?.MediaType, text);
        using var parsed = JsonDocument.Parse(text);
        if (kind == "refused")
            Assert.AreEqual("1", parsed.RootElement.GetProperty("partialSuccess").GetProperty("rejectedSpans").ToString(), text);
    }
}
