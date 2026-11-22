using Google.Protobuf;
using AttributeValue = Nami.Api.Contracts.Common.AttributeValue;
using Nami.Collector.Ingestion;
using Nami.Collector.Storage;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using LogRecord = OpenTelemetry.Proto.Logs.V1.LogRecord;
using static Nami.IntegrationTests.OtlpAttribute;

namespace Nami.IntegrationTests;

/// <summary>
///     nami stores telemetry as it was sent (observer's decision, 16 Nov 2026). At ingest only the credential stage
///     and size bounds change a record; keys, types, containers, event fields and content arrive in the stored row
///     as the sender wrote them. Each test sends one export through the converter and reads the row storage would
///     write.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class StoredAsSentTests
{
    private const string ClaudeCodeEvents = "com.anthropic.claude_code.events";
    private const string Foreign = "com.example.checkout";

    /// <summary>
    ///     F1. A key is stored under the name it was sent under, on every scope and carrier: a key the registry does
    ///     not know, a deprecated key (no rename to its successor), a baggage key and a key the registry describes
    ///     for one sender (Claude Code's <c>model</c>) on another sender's record.
    /// </summary>
    [TestMethod]
    public void Keys_UnknownDeprecatedBaggageAndAnotherSendersKeys_AreStoredUnderTheNameTheyWereSent()
    {
        KeyValue[] sent =
        [
            Text("acme.widget.id", "w-17"), Text("http.method", "POST"), Text("deployment.environment", "staging"),
            Text("baggage.tenant", "blue"), Text("model", "claude-haiku-5-5"), Text("http.route", "/orders/{id}")
        ];
        string[] keys = ["acme.widget.id", "baggage.tenant", "deployment.environment", "http.method", "http.route", "model"];

        var span = new Span
        {
            TraceId = ByteString.CopyFrom(Enumerable.Repeat((byte)0xfa, 16).ToArray()),
            SpanId = ByteString.CopyFrom(Enumerable.Repeat((byte)0xde, 8).ToArray()),
            Name = "POST /orders/{id}", StartTimeUnixNano = Time, EndTimeUnixNano = Time + 1_000_000,
            Attributes = { sent },
            Events = { new Span.Types.Event { Name = "retry", TimeUnixNano = Time, Attributes = { sent } } }
        };
        var batch = OtlpConverter.ConvertTraceRequest(new ExportTraceServiceRequest
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
                    Resource = new Resource { Attributes = { sent } },
                    ScopeSpans =
                    {
                        new ScopeSpans { Scope = new InstrumentationScope { Name = Foreign, Attributes = { sent } }, Spans = { span } }
                    }
                }
            }
        });
        Assert.AreEqual(0L, batch.RejectedSpans, batch.RejectionMessage);
        var row = Assert.ContainsSingle(IngestionStorageMapper.ToSpanStorageRows(batch));

        foreach (var (carrier, json) in new[]
                 {
                     ("span", row.AttributesJson), ("resource", row.ResourceJson), ("scope", row.ScopeAttributesJson),
                     ("event", Assert.ContainsSingle(SpanChildStorage.DeserializeEvents(row.EventsJson)!).AttributesJson)
                 })
        {
            var stored = Stored(json);
            Assert.AreSequenceEqual(keys, stored.Keys.Where(static key => key != "service.name").ToArray(), carrier);
            Assert.AreEqual("POST", stored["http.method"], carrier);
            Assert.AreEqual("staging", stored["deployment.environment"], carrier);
            Assert.IsFalse(stored.ContainsKey("http.request.method"), carrier + ": no successor is written");
        }

        Assert.IsEmpty(row.AttributeRenames, "ingest renames nothing, so it records no rename");
    }

    /// <summary>
    ///     F2. A value keeps the type it was sent in. Claude Code sends numbers and booleans as strings
    ///     (<c>duration_ms</c>, <c>success</c>); they are stored as those strings, and a reader that needs the
    ///     number parses it when it reads.
    /// </summary>
    [TestMethod]
    public void Types_AStringOfDigits_StaysAString()
    {
        var row = StoreLog(ClaudeCodeEvents, new LogRecord
        {
            Attributes =
            {
                Text("duration_ms", "4011"), Text("success", "true"), Text("cost_usd", "0.00332877"),
                new KeyValue { Key = "tool_result_size_bytes", Value = new AnyValue { IntValue = 512 } }
            }
        });

        var stored = StoredAttributeJson.Attributes(row.AttributesJson).ToDictionary(static a => a.Key, static a => a.Value);
        Assert.AreEqual("4011", (stored["duration_ms"] as AttributeValue.StringValue)?.Value);
        Assert.AreEqual("true", (stored["success"] as AttributeValue.StringValue)?.Value);
        Assert.AreEqual("0.00332877", (stored["cost_usd"] as AttributeValue.StringValue)?.Value);
        Assert.IsNotInstanceOfType<AttributeValue.StringValue>(stored["tool_result_size_bytes"], "a number sent as a number stays one");
    }

    /// <summary>
    ///     F3. A container is stored as the string it is. Claude Code's <c>tool_parameters</c> is JSON text; ingest
    ///     keeps that text and writes no attribute of its own out of it.
    /// </summary>
    [TestMethod]
    public void Containers_ToolParameters_IsStoredAsTheJsonStringItIs()
    {
        const string parameters =
            """{"bash_command":"python3","full_command":"python3 -m unittest -q","description":"Runs the unit tests","timeout":120000}""";

        var row = StoreLog(ClaudeCodeEvents, new LogRecord
        {
            Attributes = { Text("tool_parameters", parameters), Text("tool_name", "Bash") }
        });

        var stored = Stored(row.AttributesJson);
        Assert.AreEqual(parameters, stored["tool_parameters"]);
        string[] keys = ["tool_name", "tool_parameters"];
        Assert.AreSequenceEqual(keys, stored.Keys.ToArray(), "no member is lifted into an attribute of its own");
    }

    /// <summary>
    ///     F4. The record's event-name field, its body and its <c>event.name</c> attribute are stored as they
    ///     arrive: a name sent only in the attribute stays in the attribute and gets no prefix, the native column
    ///     holds what the field held, and the body is kept whatever it says.
    /// </summary>
    [TestMethod]
    public void EventFields_NameFieldBodyAndEventNameAttribute_AreStoredAsTheyArrive()
    {
        var attributeOnly = StoreLog(ClaudeCodeEvents, new LogRecord
        {
            Body = new AnyValue { StringValue = "the text a sender put in the body" },
            Attributes = { Text("event.name", "tool_result") }
        });
        var both = StoreLog(ClaudeCodeEvents, new LogRecord
        {
            EventName = "checkout.completed", Body = new AnyValue { StringValue = "claude_code.tool_result" },
            Attributes = { Text("event.name", "tool_result") }
        });

        Assert.IsNull(attributeOnly.EventName, "the field was empty, so the native column is");
        Assert.AreEqual("tool_result", Stored(attributeOnly.AttributesJson)["event.name"]);
        Assert.AreEqual("the text a sender put in the body",
            (StoredAttributeJson.Value(attributeOnly.Body) as AttributeValue.StringValue)?.Value);
        Assert.AreEqual("checkout.completed", both.EventName);
        Assert.AreEqual("tool_result", Stored(both.AttributesJson)["event.name"]);
        Assert.AreEqual("claude_code.tool_result", (StoredAttributeJson.Value(both.Body) as AttributeValue.StringValue)?.Value);
    }

    /// <summary>
    ///     F5. Content is stored as sent: prompts, responses, tool input and output, request and response bodies,
    ///     and the identity and URL keys the registry used to refuse. What a sender captures is decided at the
    ///     source (Claude Code's OTEL_LOG_* switches), not by nami.
    /// </summary>
    [TestMethod]
    [DataRow(ClaudeCodeEvents, DisplayName = "On Claude Code's events scope")]
    [DataRow(Foreign, DisplayName = "On another scope")]
    public void Content_PromptsBodiesAndIdentityKeys_AreStoredAsSent(string scope)
    {
        KeyValue[] sent =
        [
            Text("prompt", "Summarise the quarterly report"), Text("response", "Here is the summary."),
            Text("tool_input", """{"file_path":"/src/report.md"}"""), Text("output", "42 lines"),
            Text("gen_ai.input.messages", """[{"role":"user","parts":[{"type":"text","content":"hello"}]}]"""),
            Text("http.request.body.content", "{\"order\":17}"), Text("url.full", "https://shop.example.test/orders/17"),
            Text("user.email", "builder@example.test"), Text("db.query.text", "SELECT 1")
        ];

        var stored = Stored(StoreLog(scope, new LogRecord { Attributes = { sent } }).AttributesJson);

        foreach (var attribute in sent)
            Assert.AreEqual(attribute.Value.StringValue, stored.GetValueOrDefault(attribute.Key), attribute.Key);
    }

    private const ulong Time = 1_794_745_800_000_000_000;

    /// <summary>The stored attributes of one JSON column, each value as the text a tool would show.</summary>
    private static SortedDictionary<string, string?> Stored(string? json)
    {
        var stored = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in StoredAttributeJson.Attributes(json))
            stored[key] = value is AttributeValue.StringValue text ? text.Value : AttributeValue.StableString(value);
        return stored;
    }

    private static LogStorageRow StoreLog(string scope, LogRecord record)
    {
        record.TimeUnixNano = Time;
        var batch = OtlpConverter.ConvertLogs(new ExportLogsServiceRequest
        {
            ResourceLogs =
            {
                new ResourceLogs
                {
                    ScopeLogs = { new ScopeLogs { Scope = new InstrumentationScope { Name = scope }, LogRecords = { record } } }
                }
            }
        }, TimeProvider.System);
        Assert.AreEqual(0L, batch.RejectedLogRecords, batch.RejectionMessage);
        return Assert.ContainsSingle(IngestionStorageMapper.ToLogStorageRows(batch));
    }
}
