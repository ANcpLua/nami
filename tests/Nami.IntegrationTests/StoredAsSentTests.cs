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

    private const ulong Time = 1_794_745_800_000_000_000;

    private static KeyValue Text(string key, string value) =>
        new() { Key = key, Value = new AnyValue { StringValue = value } };

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
