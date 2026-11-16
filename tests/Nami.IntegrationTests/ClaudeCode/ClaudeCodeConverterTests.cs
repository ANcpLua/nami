using System.Text;
using Google.Protobuf;
using Nami.Collector.Ingestion;
using Nami.SemanticConventions;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using LogRecord = OpenTelemetry.Proto.Logs.V1.LogRecord;

namespace Nami.IntegrationTests.ClaudeCode;

/// <summary>
///     What the registry declares for a producer, at the converter: on records under one of Claude Code's
///     instrumentation scopes (<c>Producers.Scopes</c>) the event name is the registry's, the body is kept only when
///     it is that name, string values become the stored type, the named members of <c>tool_parameters</c> become
///     attributes and the container goes, <c>full_command</c> is cut at the registry's bound, and a span status
///     message loses its credentials and is cut at 1024 bytes. On any other scope the same keys are another sender's
///     unknown keys. Each rule is shown on both sides, so a rule that fired everywhere, or nowhere, fails.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class ClaudeCodeConverterTests
{
    private const string Producer = "claude_code";
    private const string EventsScope = "com.anthropic.claude_code.events";
    private const string TracingScope = "com.anthropic.claude_code.tracing";
    private const string MeterScope = "com.anthropic.claude_code";
    private const string ForeignScope = "com.example.checkout";
    private const string Marker = "...[truncated]";

    // A key the registry persists for every sender, next to each producer key: it shows the record was converted.
    private const string Ordinary = "gen_ai.agent.name";

    private static readonly string[] s_scopes = [MeterScope, EventsScope, TracingScope];
    private static readonly string[] s_ordinaryOnly = [Ordinary];
    private static readonly string[] s_toolNameOnly = ["tool_name"];
    private static readonly string[] s_members = ["bash_command", "full_command", "subagent_type"];
    private static readonly string[] s_membersAndToolName = ["bash_command", "full_command", "subagent_type", "tool_name"];

    [TestMethod]
    public void Scopes_AreTheThreeTheRegistryNamesForClaudeCode()
    {
        Assert.AreSequenceEqual(s_scopes, Producers.Scopes[Producer].Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>The evaluator's nit on step 2: the record's own EventName field wins over the deprecated attribute.</summary>
    [TestMethod]
    [DataRow(EventsScope, DisplayName = "On Claude Code's events scope")]
    [DataRow(ForeignScope, DisplayName = "On another scope")]
    public void EventName_TheFieldWinsOverTheDeprecatedAttribute(string scope)
    {
        var log = ConvertLog(scope, new LogRecord
        {
            EventName = "checkout.completed", Attributes = { Text("event.name", "tool_result"), Text(Ordinary, "a") }
        });

        Assert.AreEqual("checkout.completed", log.EventName);
        Assert.AreEqual("tool_result", log.Attributes["event.name"].AsString(), "the deprecated key is stored as sent");
    }

    [TestMethod]
    [DataRow(EventsScope, "tool_result", "claude_code.tool_result", DisplayName = "Claude Code's unqualified name gets the registry's namespace")]
    [DataRow(EventsScope, "claude_code.tool_result", "claude_code.tool_result", DisplayName = "A name that has the namespace keeps it once")]
    [DataRow(EventsScope, "hook_execution", "claude_code.hook_execution", DisplayName = "An event the registry does not define yet is named the same way")]
    [DataRow(ForeignScope, "tool_result", "tool_result", DisplayName = "Another sender's name stays as sent")]
    public void EventName_FromTheDeprecatedAttribute_IsTheRegistrysNameOnAProducersScope(string scope, string sent,
        string expected)
    {
        var log = ConvertLog(scope, new LogRecord { Attributes = { Text("event.name", sent) } });

        Assert.AreEqual(expected, log.EventName);
        if (sent == "tool_result" && scope == EventsScope)
            Assert.Contains(expected, Events.All.Select(static definition => definition.Name));
    }

    [TestMethod]
    public void Body_OnAProducersScope_IsKeptOnlyWhenItIsTheEventName()
    {
        var named = ConvertLog(EventsScope, new LogRecord
        {
            Body = new AnyValue { StringValue = "claude_code.tool_result" }, Attributes = { Text("event.name", "tool_result") }
        });
        var text = ConvertLog(EventsScope, new LogRecord
        {
            Body = new AnyValue { StringValue = "the prompt, a response or a file" },
            Attributes = { Text("event.name", "tool_result") }
        });
        var map = ConvertLog(EventsScope, new LogRecord
        {
            Body = new AnyValue { KvlistValue = new KeyValueList { Values = { Text(Ordinary, "inside a map body") } } },
            Attributes = { Text("event.name", "tool_result") }
        });
        var unnamed = ConvertLog(EventsScope, new LogRecord { Body = new AnyValue { StringValue = "claude_code.tool_result" } });
        var foreign = ConvertLog(ForeignScope, new LogRecord
        {
            Body = new AnyValue { StringValue = "order 17 failed" }, Attributes = { Text("event.name", "tool_result") }
        });

        Assert.AreEqual("claude_code.tool_result", named.Body.AsString());
        Assert.AreEqual(OtlpAttributeValueKind.Empty, text.Body.Kind, "free text in a producer's body is not kept");
        Assert.AreEqual("claude_code.tool_result", text.EventName, "the event itself is kept, with its name");
        Assert.AreEqual(OtlpAttributeValueKind.Empty, map.Body.Kind, "a map in a producer's body is not kept");
        Assert.AreEqual(OtlpAttributeValueKind.Empty, unnamed.Body.Kind, "without an event name there is nothing to equal");
        Assert.AreEqual("order 17 failed", foreign.Body.AsString(), "another sender's body is its log message");
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

    [TestMethod]
    public void ToolParameters_NamedMembersBecomeAttributes_AndTheContainerIsDropped()
    {
        const string parameters = """
            {"bash_command":"python3","full_command":"python3 -m unittest -q","description":"Runs the unit tests",
             "subagent_type":"general-purpose","git_commit_id":"c0ffee0000000000000000000000000000000001",
             "timeout":120000,"dangerouslyDisableSandbox":false}
            """;
        var lifted = s_members;
        Assert.AreSequenceEqual(lifted, WireContainers.Members["tool_parameters"]);

        var log = ConvertLog(EventsScope, new LogRecord
        {
            Attributes = { Text("tool_parameters", parameters), Text("tool_name", "Bash") }
        });
        var span = ConvertSpan(TracingScope, NewSpan([Text("tool_parameters", parameters)]));
        var foreign = ConvertLog(ForeignScope, new LogRecord
        {
            Attributes = { Text("tool_parameters", parameters), Text(Ordinary, "builder") }
        });

        Assert.AreSequenceEqual(s_membersAndToolName, Keys(log.Attributes));
        Assert.AreEqual("python3", log.Attributes["bash_command"].AsString());
        Assert.AreEqual("python3 -m unittest -q", log.Attributes["full_command"].AsString());
        Assert.AreEqual("general-purpose", log.Attributes["subagent_type"].AsString());
        Assert.AreSequenceEqual(lifted, Keys(span.Attributes).Where(static key => key != "service.name"));
        Assert.AreSequenceEqual(s_ordinaryOnly, Keys(foreign.Attributes),
            "under another scope tool_parameters is an unknown key and nothing is read out of it");
    }

    [TestMethod]
    public void ToolParameters_AnAttributeOfTheRecordWinsOverAMemberOfTheSameName()
    {
        var log = ConvertLog(EventsScope, new LogRecord
        {
            Attributes =
            {
                Text("tool_parameters", """{"full_command":"from the container","bash_command":"git"}"""),
                Text("full_command", "from the record")
            }
        });

        Assert.AreEqual("from the record", log.Attributes["full_command"].AsString());
        Assert.AreEqual("git", log.Attributes["bash_command"].AsString());
    }

    [TestMethod]
    [DataRow("not json at all", DisplayName = "Text that is not JSON")]
    [DataRow("""["full_command","python3"]""", DisplayName = "A JSON array")]
    [DataRow("\"full_command\"", DisplayName = "A JSON string")]
    [DataRow("""{"full_command":{"nested":"python3"},"bash_command":["python3"],"subagent_type":null}""", DisplayName = "Members that are not values")]
    [DataRow("""{"full_command":"\ud800"}""", DisplayName = "A member with an unpaired surrogate escape")]
    [DataRow("""{"full_command":"python3" """, DisplayName = "A truncated object")]
    public void ToolParameters_ThatHoldNoReadableMember_YieldNothing_AndTheRecordIsKept(string parameters)
    {
        var request = Logs(EventsScope, new LogRecord
        {
            TimeUnixNano = 1, Attributes = { Text("tool_parameters", parameters), Text("tool_name", "Bash") }
        });

        var batch = OtlpConverter.ConvertLogs(request, TimeProvider.System);

        Assert.AreEqual(0L, batch.RejectedLogRecords, batch.RejectionMessage);
        Assert.AreSequenceEqual(s_toolNameOnly, Keys(Assert.ContainsSingle(batch.Logs).Attributes));
    }

    /// <summary>
    ///     <c>full_command</c> is persisted and bounded (observer's ruling, 15 Nov): at most
    ///     <c>ValueLimits.MaxUtf8Bytes</c> bytes of UTF-8, the marker that log bodies use included, cut between
    ///     characters. It is bounded wherever it arrives: on the tool span and inside <c>tool_parameters</c>.
    /// </summary>
    [TestMethod]
    public void FullCommand_IsKeptUpToTheRegistrysBound_WithTheTruncationMarker()
    {
        var bound = ValueLimits.MaxUtf8Bytes["full_command"];
        Assert.AreEqual(4096, bound);
        var atTheBound = new string('a', bound);
        var onePast = new string('a', bound + 1);
        var twoByteCharacters = new string('é', bound);
        var heredoc = "git commit -m \"$(cat <<'EOF'\n" + new string('x', 3 * bound) + "\nEOF\n)\"";

        Assert.AreEqual(atTheBound, SpanCommand(atTheBound), "a command of exactly the bound is kept whole");
        Assert.AreEqual(new string('a', bound - Marker.Length) + Marker, SpanCommand(onePast));
        var cut = SpanCommand(twoByteCharacters)!;
        Assert.AreEqual(new string('é', (bound - Marker.Length) / 2) + Marker, cut, "cut between characters, never inside one");
        Assert.IsLessThanOrEqualTo(bound, Encoding.UTF8.GetByteCount(cut));

        var lifted = ConvertLog(EventsScope, new LogRecord
        {
            Attributes = { Text("tool_parameters", new System.Text.Json.Nodes.JsonObject { ["full_command"] = heredoc }.ToJsonString()) }
        }).Attributes["full_command"].AsString()!;
        Assert.AreEqual(bound, Encoding.UTF8.GetByteCount(lifted));
        Assert.StartsWith("git commit -m", lifted);
        Assert.EndsWith(Marker, lifted);

        static string? SpanCommand(string command) =>
            ConvertSpan(TracingScope, NewSpan([Text("full_command", command)])).Attributes["full_command"].AsString();
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
    ///     A span status message is free text outside the allowlist, and Claude Code copies the text of its
    ///     <c>error</c> attribute into it. A producer's is kept as error text is kept elsewhere (observer's ruling, 15
    ///     Nov): through the sanitizer exception text gets, then up to 1024 UTF-8 bytes with the marker. Another
    ///     sender's is stored as sent.
    /// </summary>
    [TestMethod]
    public void SpanStatusMessage_OnAProducersScope_LosesItsCredentials_AndIsKeptUpTo1024Bytes()
    {
        const string failure = "Shell command failed";
        const string leaking = "Request failed with 401\nAuthorization: Bearer sk-live-0123456789abcdef\n" +
                               "connection password=hunter2-and-more;retry in 5 s";
        var text = new string('é', 2000);

        var ours = ConvertSpan(TracingScope, WithStatus(leaking));
        Assert.AreEqual("Request failed with 401\nAuthorization: Bearer [REDACTED]\nconnection password=[REDACTED];retry in 5 s",
            ours.StatusMessage);
        Assert.AreEqual((int)Status.Types.StatusCode.Error, ours.StatusCode.GetValueOrDefault());
        Assert.AreEqual(failure, ConvertSpan(TracingScope, WithStatus(failure)).StatusMessage, "text without credentials is kept");
        var cut = ConvertSpan(TracingScope, WithStatus(text)).StatusMessage!;
        Assert.AreEqual(new string('é', (1024 - Marker.Length) / 2) + Marker, cut);
        Assert.IsLessThanOrEqualTo(1024, Encoding.UTF8.GetByteCount(cut));

        Assert.AreEqual(leaking, ConvertSpan(ForeignScope, WithStatus(leaking)).StatusMessage,
            "no general rule yet: another sender's status message is stored as sent");
        Assert.AreEqual(text, ConvertSpan(ForeignScope, WithStatus(text)).StatusMessage);

        static Span WithStatus(string message)
        {
            var span = NewSpan([]);
            span.Status = new Status { Code = Status.Types.StatusCode.Error, Message = message };
            return span;
        }
    }

    /// <summary>
    ///     Claude Code's <c>error</c> attribute is persisted since registry 849dd8b, as error text: through the
    ///     sanitizer of <c>exception.message</c> first and up to <c>ValueLimits.MaxUtf8Bytes</c> (1024) after it, on
    ///     the events and the spans that carry it, under every scope.
    /// </summary>
    [TestMethod]
    public void Error_OnAProducersScope_LosesItsCredentials_AndIsKeptUpToTheRegistrysBound()
    {
        var bound = ValueLimits.MaxUtf8Bytes["error"];
        Assert.AreEqual(1024, bound);
        const string leaking = "Request failed with 401\nAuthorization: Bearer sk-live-0123456789abcdef\n" +
                               "connection password=hunter2-and-more;retry in 5 s";
        const string sanitized = "Request failed with 401\nAuthorization: Bearer [REDACTED]\nconnection password=[REDACTED];retry in 5 s";
        var text = new string('é', 2000);

        Assert.AreEqual(sanitized, ConvertLog(EventsScope, new LogRecord { Attributes = { Text("error", leaking) } })
            .Attributes["error"].AsString());
        Assert.AreEqual(sanitized, ConvertSpan(TracingScope, NewSpan([Text("error", leaking)])).Attributes["error"].AsString());
        Assert.AreEqual("Shell command failed", ConvertLog(EventsScope,
            new LogRecord { Attributes = { Text("error", "Shell command failed") } }).Attributes["error"].AsString());
        var cut = ConvertLog(EventsScope, new LogRecord { Attributes = { Text("error", text) } }).Attributes["error"].AsString()!;
        Assert.AreEqual(new string('é', (bound - Marker.Length) / 2) + Marker, cut);

        // The credential sits where the cut falls: it is removed before the text is cut, not cut in half.
        var straddling = new string('a', bound - 30) + " password=hunter2-and-more-secret-text";
        var kept = ConvertLog(EventsScope, new LogRecord { Attributes = { Text("error", straddling) } }).Attributes["error"].AsString()!;
        Assert.DoesNotContain("hunter2", kept);
        Assert.IsLessThanOrEqualTo(bound, Encoding.UTF8.GetByteCount(kept));

        // Error text is a string; a value of another type stays as it was sent.
        var number = ConvertLog(EventsScope, new LogRecord
        {
            Attributes = { new KeyValue { Key = "error", Value = new AnyValue { IntValue = 500 } } }
        });
        Assert.AreEqual(500L, number.Attributes["error"].AsInt64());

        // A key is stored on every scope (keys as sent); error text loses its credentials there too.
        var foreign = ConvertLog(ForeignScope, new LogRecord { Attributes = { Text("error", leaking), Text(Ordinary, "builder") } });
        Assert.AreEqual(sanitized, foreign.Attributes["error"].AsString());
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

    private static MetricPointIngestionRecord ConvertPoint(string scope, KeyValue[] attributes)
    {
        var batch = OtlpConverter.ConvertMetrics(new ExportMetricsServiceRequest
        {
            ResourceMetrics =
            {
                new ResourceMetrics
                {
                    ScopeMetrics =
                    {
                        new ScopeMetrics
                        {
                            Scope = new InstrumentationScope { Name = scope },
                            Metrics =
                            {
                                new Metric
                                {
                                    Name = "claude_code.token.usage",
                                    Sum = new Sum
                                    {
                                        AggregationTemporality = AggregationTemporality.Delta, IsMonotonic = true,
                                        DataPoints =
                                        {
                                            new NumberDataPoint
                                            {
                                                TimeUnixNano = 1_794_745_800_000_000_000, AsInt = 7,
                                                Attributes = { attributes.Select(static attribute => attribute.Clone()) }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        });
        Assert.AreEqual(0L, batch.RejectedDataPoints, batch.RejectionMessage);
        return Assert.ContainsSingle(batch.Points);
    }
}
