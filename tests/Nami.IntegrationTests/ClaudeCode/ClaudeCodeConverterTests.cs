using System.Text;
using Google.Protobuf;
using Nami.Collector.Ingestion;
using Nami.SemanticConventions;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using LogRecord = OpenTelemetry.Proto.Logs.V1.LogRecord;

namespace Nami.IntegrationTests.ClaudeCode;

/// <summary>
///     Claude Code's records at the converter: stored as sent, whatever instrumentation scope they arrive under.
///     The event name field and the deprecated <c>event.name</c> attribute are both kept, values keep their type,
///     <c>tool_parameters</c> stays whole, and no key has a bound of its own. A span status message loses its
///     credentials and is cut at 1024 bytes. Each case runs under one of Claude Code's scopes and under another
///     sender's, and must come out the same.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class ClaudeCodeConverterTests
{
    private const string EventsScope = "com.anthropic.claude_code.events";
    private const string TracingScope = "com.anthropic.claude_code.tracing";
    private const string MeterScope = "com.anthropic.claude_code";
    private const string ForeignScope = "com.example.checkout";
    private const string Marker = "…[truncated]";
    private const int MarkerBytes = 14;

    // An ordinary key next to each of Claude Code's: it shows the record was converted.
    private const string Ordinary = "gen_ai.agent.name";

    /// <summary>The record's own EventName field is stored as sent, and so is the deprecated attribute next to it.</summary>
    [TestMethod]
    [DataRow(EventsScope, DisplayName = "On Claude Code's events scope")]
    [DataRow(ForeignScope, DisplayName = "On another scope")]
    public void EventName_TheFieldAndTheDeprecatedAttribute_AreBothStoredAsSent(string scope)
    {
        var log = ConvertLog(scope, new LogRecord
        {
            EventName = "checkout.completed", Attributes = { Text("event.name", "tool_result"), Text(Ordinary, "a") }
        });

        Assert.AreEqual("checkout.completed", log.EventName);
        Assert.AreEqual("tool_result", log.Attributes["event.name"].AsString(), "the deprecated key is stored as sent");
    }

    [TestMethod]
    [DataRow("success", "yes", DisplayName = "A boolean that is neither true nor false")]
    [DataRow("success", "True", DisplayName = "A boolean in another spelling")]
    [DataRow("duration_ms", "12ms", DisplayName = "An int with a unit")]
    [DataRow("duration_ms", " 12", DisplayName = "An int with a space")]
    [DataRow("duration_ms", "1e3", DisplayName = "An int in exponent form")]
    [DataRow("duration_ms", "99999999999999999999", DisplayName = "An int beyond 64 bits")]
    [DataRow("cost_usd", "NaN", DisplayName = "A double that is not a number")]
    [DataRow("cost_usd", "1,5", DisplayName = "A double with a comma")]
    [DataRow("model", "123", DisplayName = "A string key that looks like a number")]
    [DataRow("duration_ms", "4011", DisplayName = "An int sent as a string")]
    [DataRow("success", "true", DisplayName = "A boolean sent as a string")]
    [DataRow("cost_usd", "0.00332877", DisplayName = "A double sent as a string")]
    public void StringValues_OfProducerKeys_StayTheStringsThatWereSent(string key, string sent)
    {
        var log = ConvertLog(EventsScope, new LogRecord { Attributes = { Text(key, sent) } });

        Assert.AreEqual(sent, log.Attributes[key].AsString());
    }

    [TestMethod]
    public void TypedValues_OfProducerKeys_StayAsSent()
    {
        var log = ConvertLog(EventsScope, new LogRecord
        {
            Attributes =
            {
                new KeyValue { Key = "duration_ms", Value = new AnyValue { IntValue = 4011 } },
                new KeyValue { Key = "success", Value = new AnyValue { BoolValue = true } },
                new KeyValue { Key = "cost_usd", Value = new AnyValue { DoubleValue = 0.5 } }
            }
        });

        Assert.AreEqual(4011L, log.Attributes["duration_ms"].AsInt64());
        Assert.AreEqual(true, log.Attributes["success"].AsBool());
        Assert.AreEqual(0.5, log.Attributes["cost_usd"].AsDouble());
    }

    /// <summary>
    ///     <c>tool_parameters</c> is stored as the text it is, whatever that text is: ingest reads no member out of
    ///     it, and a record that carries it is kept.
    /// </summary>
    [TestMethod]
    [DataRow("""{"bash_command":"python3","full_command":"python3 -m unittest -q","timeout":120000}""", DisplayName = "A JSON object")]
    [DataRow("not json at all", DisplayName = "Text that is not JSON")]
    [DataRow("""["full_command","python3"]""", DisplayName = "A JSON array")]
    [DataRow("""{"full_command":"\ud800"}""", DisplayName = "A member with an unpaired surrogate escape")]
    [DataRow("""{"full_command":"python3" """, DisplayName = "A truncated object")]
    public void ToolParameters_AreStoredAsTheTextTheyAre(string parameters)
    {
        var request = Logs(EventsScope, new LogRecord
        {
            TimeUnixNano = 1, Attributes = { Text("tool_parameters", parameters), Text("tool_name", "Bash") }
        });

        var batch = OtlpConverter.ConvertLogs(request, TimeProvider.System);

        Assert.AreEqual(0L, batch.RejectedLogRecords, batch.RejectionMessage);
        var log = Assert.ContainsSingle(batch.Logs);
        string[] keys = ["tool_name", "tool_parameters"];
        Assert.AreSequenceEqual(keys, Keys(log.Attributes));
        Assert.AreEqual(parameters, log.Attributes["tool_parameters"].AsString());
    }

    /// <summary>A bounded key is cut when it holds a string. A value of another type is kept in the type it was sent in.</summary>
    [TestMethod]
    public void FullCommand_ThatIsNotAString_IsKeptAsSent()
    {
        var list = new AnyValue
        {
            ArrayValue = new ArrayValue { Values = { new AnyValue { StringValue = "python3" }, new AnyValue { IntValue = 3 } } }
        };

        var span = ConvertSpan(TracingScope, NewSpan([new KeyValue { Key = "full_command", Value = list }, Text("tool_name", "Bash")]));

        Assert.AreEqual(OtlpAttributeValueKind.Array, span.Attributes["full_command"].Kind);
    }

    /// <summary>
    ///     A span status message is free text that no key names, and Claude Code copies the text of its
    ///     <c>error</c> attribute into it. Every sender's passes the credential stage (observer's rule, 16 Nov) and
    ///     is then cut at 1024 UTF-8 bytes with the marker (observer's ruling, 15 Nov, for every sender).
    /// </summary>
    [TestMethod]
    [DataRow(TracingScope, DisplayName = "On Claude Code's tracing scope")]
    [DataRow(ForeignScope, DisplayName = "On another scope")]
    public void SpanStatusMessage_OfEverySender_LosesItsCredentials_AndIsKeptUpTo1024Bytes(string scope)
    {
        const string failure = "Shell command failed";
        const string leaking = "Request failed with 401\nAuthorization: Bearer sk-live-0123456789abcdef\n" +
                               "connection password=hunter2-and-more;retry in 5 s";
        var text = new string('é', 2000);

        const string redacted =
            "Request failed with 401\nAuthorization: Bearer [REDACTED]\nconnection password=[REDACTED];retry in 5 s";
        var span = ConvertSpan(scope, WithStatus(leaking));
        Assert.AreEqual(redacted, span.StatusMessage);
        Assert.AreEqual((int)Status.Types.StatusCode.Error, span.StatusCode.GetValueOrDefault());
        Assert.AreEqual(failure, ConvertSpan(scope, WithStatus(failure)).StatusMessage, "text without credentials is kept");
        var cut = ConvertSpan(scope, WithStatus(text)).StatusMessage!;
        Assert.AreEqual(new string('é', (1024 - MarkerBytes) / 2) + Marker, cut);
        Assert.IsLessThanOrEqualTo(1024, Encoding.UTF8.GetByteCount(cut));
        var atTheBound = new string('a', 1024);
        Assert.AreEqual(atTheBound, ConvertSpan(scope, WithStatus(atTheBound)).StatusMessage, "exactly 1024 bytes are kept whole");

        // The credential sits where the cut falls: it is removed before the text is cut, not cut in half.
        var straddling = ConvertSpan(scope, WithStatus(new string('a', 1000) + " password=hunter2-and-more-secret-text")).StatusMessage!;
        Assert.DoesNotContain("hunter2", straddling);
        Assert.IsLessThanOrEqualTo(1024, Encoding.UTF8.GetByteCount(straddling));

        static Span WithStatus(string message)
        {
            var span = NewSpan([]);
            span.Status = new Status { Code = Status.Types.StatusCode.Error, Message = message };
            return span;
        }
    }

    /// <summary>
    ///     Claude Code's <c>error</c> attribute passes the credential stage as every string does, under every scope,
    ///     and has no bound of its own: the one cap of every value holds for it (<c>AttributeBoundsTests</c>).
    /// </summary>
    [TestMethod]
    public void Error_UnderEveryScope_LosesItsCredentials_AndKeepsTheRest()
    {
        const string leaking = "Request failed with 401\nAuthorization: Bearer sk-live-0123456789abcdef\n" +
                               "connection password=hunter2-and-more;retry in 5 s";
        const string sanitized = "Request failed with 401\nAuthorization: Bearer [REDACTED]\nconnection password=[REDACTED];retry in 5 s";
        var text = new string('é', 2000);

        Assert.AreEqual(sanitized, ConvertLog(EventsScope, new LogRecord { Attributes = { Text("error", leaking) } })
            .Attributes["error"].AsString());
        Assert.AreEqual(sanitized, ConvertSpan(TracingScope, NewSpan([Text("error", leaking)])).Attributes["error"].AsString());
        Assert.AreEqual(sanitized, ConvertLog(ForeignScope, new LogRecord { Attributes = { Text("error", leaking) } })
            .Attributes["error"].AsString());
        Assert.AreEqual("Shell command failed", ConvertLog(EventsScope,
            new LogRecord { Attributes = { Text("error", "Shell command failed") } }).Attributes["error"].AsString());
        Assert.AreEqual(text, ConvertLog(EventsScope, new LogRecord { Attributes = { Text("error", text) } }).Attributes["error"].AsString(),
            "2000 characters are far below the cap of every value");

        // Error text is a string; a value of another type stays as it was sent.
        var number = ConvertLog(EventsScope, new LogRecord
        {
            Attributes = { new KeyValue { Key = "error", Value = new AnyValue { IntValue = 500 } } }
        });
        Assert.AreEqual(500L, number.Attributes["error"].AsInt64());
    }

    private static string[] Keys(NormalizedAttributes attributes) =>
        attributes.Keys.Order(StringComparer.Ordinal).ToArray();

    private static KeyValue Text(string key, string value) =>
        new() { Key = key, Value = new AnyValue { StringValue = value } };

    private static ExportLogsServiceRequest Logs(string scope, LogRecord record) => new()
    {
        ResourceLogs =
        {
            new ResourceLogs
            {
                ScopeLogs = { new ScopeLogs { Scope = new InstrumentationScope { Name = scope }, LogRecords = { record } } }
            }
        }
    };

    private static LogIngestionRecord ConvertLog(string scope, LogRecord record)
    {
        record.TimeUnixNano = 1_794_745_800_000_000_000;
        var batch = OtlpConverter.ConvertLogs(Logs(scope, record), TimeProvider.System);
        Assert.AreEqual(0L, batch.RejectedLogRecords, batch.RejectionMessage);
        return Assert.ContainsSingle(batch.Logs);
    }

    /// <summary>A span with the given attributes; with event and link attributes it carries one event and one link.</summary>
    private static Span NewSpan(KeyValue[] attributes, KeyValue[]? eventAttributes = null, KeyValue[]? linkAttributes = null)
    {
        var span = new Span
        {
            TraceId = ByteString.CopyFrom(Enumerable.Repeat((byte)0xfa, 16).ToArray()),
            SpanId = ByteString.CopyFrom(Enumerable.Repeat((byte)0xde, 8).ToArray()),
            Name = "claude_code.tool",
            StartTimeUnixNano = 1_794_745_800_000_000_000,
            EndTimeUnixNano = 1_794_745_801_000_000_000,
            Attributes = { attributes.Select(static attribute => attribute.Clone()) }
        };
        if (eventAttributes is not null)
        {
            span.Events.Add(new Span.Types.Event
            {
                Name = "tool.output", TimeUnixNano = span.EndTimeUnixNano,
                Attributes = { eventAttributes.Select(static attribute => attribute.Clone()) }
            });
        }

        if (linkAttributes is not null)
        {
            span.Links.Add(new Span.Types.Link
            {
                TraceId = span.TraceId, SpanId = ByteString.CopyFrom(Enumerable.Repeat((byte)0xad, 8).ToArray()),
                Attributes = { linkAttributes.Select(static attribute => attribute.Clone()) }
            });
        }

        return span;
    }

    private static SpanIngestionRecord ConvertSpan(string scope, Span span)
    {
        var batch = OtlpConverter.ConvertTraceRequest(new ExportTraceServiceRequest
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
                    ScopeSpans = { new ScopeSpans { Scope = new InstrumentationScope { Name = scope }, Spans = { span } } }
                }
            }
        });
        Assert.AreEqual(0L, batch.RejectedSpans, batch.RejectionMessage);
        return Assert.ContainsSingle(batch.Spans);
    }
}
