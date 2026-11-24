using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Nami.Tests.Wire.OpenTelemetry.Proto.Collector.Logs.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Collector.Trace.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Common.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Logs.V1;
using WireLogRecord = Nami.Tests.Wire.OpenTelemetry.Proto.Logs.V1.LogRecord;
using Nami.Tests.Wire.OpenTelemetry.Proto.Trace.V1;

namespace Nami.Tests.Infrastructure;

/// <summary>One OTLP/HTTP protobuf export body: the route it is posted to, its session label, its name and bytes.</summary>
internal sealed record OtlpBody(string Signal, string Session, string Name, byte[] Payload);

/// <summary>
///     A gate run to plant into a session: the command line, its outcome, when and in which order it ran, and
///     whether Bash started it in the background.
/// </summary>
internal sealed record PlantedRun(string Command, bool Success, TimeSpan AfterSessionEnd, long Sequence, string ToolUseId,
    bool InBackground = false);

/// <summary>
///     The scrubbed export of three signed-in Claude Code sessions in
///     <c>tests/Nami.IntegrationTests/Fixtures/ClaudeCodeOtlp/signed-in</c> (see its README). Bodies load in arrival
///     order, and each must match the SHA-256 that <c>manifest.json</c> records, so no test runs on an edited
///     fixture. A synthetic variant is a decoded copy changed in memory; the fixture files are never written.
/// </summary>
internal static class ClaudeCodeCapture
{
    private static readonly Lazy<IReadOnlyList<OtlpBody>> s_signedIn = new(Load);

    /// <summary>Attributes every Claude Code event carries; a new event copies these from its neighbour.</summary>
    private static readonly HashSet<string> s_standardEventKeys =
    [
        "session.id", "user.id", "user.email", "user.account_uuid", "user.account_id", "organization.id",
        "terminal.type", "anthropic.provider", "anthropic.custom_base_url", "anthropic.plan", "gen_ai.agent.name",
        "prompt.id", "event.timestamp", "event.sequence"
    ];

    /// <summary>Detailed beta tracing's content attributes on <c>claude_code.llm_request</c> (research ledger).</summary>
    private static readonly string[] s_llmRequestContentKeys =
        ["new_context", "system_reminders", "system_prompt_preview", "user_system_prompt", "response.model_output"];

    /// <summary>The content attributes of the <c>tool.output</c> span event that OTEL_LOG_TOOL_CONTENT adds.</summary>
    private static readonly string[] s_toolOutputContentKeys = ["content", "output", "diff"];

    /// <summary>The names <see cref="WithContentSentinels" /> appends to its prefix, one per planted attribute.</summary>
    internal static IReadOnlyList<string> ContentSentinelNames { get; } =
    [
        "prompt", "prompt_text", "system_prompt", "system_prompt.prompt", "response", "tool_input",
        "api_request_body", "api_response_body", "user_prompt", "new_context", "system_reminders",
        "system_prompt_preview", "user_system_prompt", "response.model_output", "tool.output.content",
        "tool.output.output", "tool.output.diff"
    ];

    internal static string SignedInDirectory => Path.Combine(ProcessRunner.RepositoryRoot, "tests",
        "Nami.IntegrationTests", "Fixtures", "ClaudeCodeOtlp", "signed-in");

    /// <summary>The 35 bodies of sessions s1, s2 and s3 in the order Claude Code sent them.</summary>
    internal static IReadOnlyList<OtlpBody> SignedIn() => s_signedIn.Value;

    private static List<OtlpBody> Load()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(SignedInDirectory, "manifest.json")));
        var bodies = new List<OtlpBody>();
        foreach (var capture in manifest.RootElement.GetProperty("captures").EnumerateArray())
        {
            var file = capture.GetProperty("file").GetString()!;
            var payload = File.ReadAllBytes(Path.Combine(SignedInDirectory, file));
            var recorded = capture.GetProperty("sha256").GetString();
            var actual = Convert.ToHexStringLower(SHA256.HashData(payload));
            if (!string.Equals(actual, recorded, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"{file} has SHA-256 {actual}, but manifest.json records {recorded}; the fixture is read-only.");
            bodies.Add(new OtlpBody(capture.GetProperty("signal").GetString()!,
                capture.GetProperty("session").GetString()!, file, payload));
        }

        return bodies;
    }

    /// <summary>
    ///     A copy of <paramref name="bodies" /> in which the edits may change each decoded logs or traces body. A body the
    ///     edits leave equal keeps its exact bytes.
    /// </summary>
    internal static IReadOnlyList<OtlpBody> Edit(IReadOnlyList<OtlpBody> bodies,
        Action<OtlpBody, ExportLogsServiceRequest>? logs = null,
        Action<OtlpBody, ExportTraceServiceRequest>? traces = null)
    {
        return
        [
            .. bodies.Select(body => body.Signal switch
            {
                "logs" when logs is not null => Rewrite(body, ExportLogsServiceRequest.Parser,
                    request => logs(body, request)),
                "traces" when traces is not null => Rewrite(body, ExportTraceServiceRequest.Parser,
                    request => traces(body, request)),
                _ => body
            })
        ];
    }

    private static OtlpBody Rewrite<T>(OtlpBody body, MessageParser<T> parser, Action<T> edit)
        where T : IMessage<T>
    {
        var original = parser.ParseFrom(body.Payload);
        var copy = original.Clone();
        edit(copy);
        return copy.Equals(original) ? body : body with { Payload = copy.ToByteArray() };
    }

    /// <summary>The log records of every logs body, with the body each came from.</summary>
    internal static IEnumerable<(OtlpBody Body, ResourceLogs Resource, ScopeLogs Scope, WireLogRecord Record)> LogRecords(
        IReadOnlyList<OtlpBody> bodies)
    {
        foreach (var body in bodies.Where(static b => b.Signal == "logs"))
        {
            var request = ExportLogsServiceRequest.Parser.ParseFrom(body.Payload);
            foreach (var resource in request.ResourceLogs)
                foreach (var scope in resource.ScopeLogs)
                    foreach (var record in scope.LogRecords)
                        yield return (body, resource, scope, record);
        }
    }

    /// <summary>The trace ids of every traces body, as 32 lowercase hex characters.</summary>
    internal static IReadOnlyList<string> TraceIds(IReadOnlyList<OtlpBody> bodies)
    {
        return
        [
            .. bodies.Where(static b => b.Signal == "traces")
                .SelectMany(static b => ExportTraceServiceRequest.Parser.ParseFrom(b.Payload).ResourceSpans)
                .SelectMany(static r => r.ScopeSpans).SelectMany(static s => s.Spans)
                .Select(static span => Convert.ToHexStringLower(span.TraceId.Span)).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
        ];
    }

    /// <summary>
    ///     Plants a run into the session of <paramref name="sourceToolUseId" /> after its last event: a copy of that
    ///     tool call's <c>tool_decision</c> and <c>tool_result</c> with another command line and outcome, its own
    ///     tool_use_id, later times and event sequence numbers, and no trace context (no span was exported for it).
    ///     A background run differs as Claude Code 2.1.331 sent it (the fixture's README, s5): only its
    ///     <c>tool_input</c> says <c>"run_in_background":true</c>.
    /// </summary>
    internal static OtlpBody PlantRun(IReadOnlyList<OtlpBody> bodies, string sourceToolUseId, PlantedRun run)
    {
        var records = LogRecords(bodies).ToArray();
        var source = records.Where(r => Text(r.Record.Attributes, "tool_use_id") == sourceToolUseId).ToArray();
        var result = source.Single(static r => Text(r.Record.Attributes, "event.name") == "tool_result");
        var decision = source.Single(static r => Text(r.Record.Attributes, "event.name") == "tool_decision");
        var sessionEnd = records.Where(r => r.Body.Session == result.Body.Session).Max(static r => r.Record.TimeUnixNano);
        var started = sessionEnd + checked((ulong)run.AfterSessionEnd.Ticks * 100);
        const ulong Duration = 500_000_000;
        var plantedDecision = decision.Record.Clone();
        Stamp(plantedDecision, started, run.Sequence, run.ToolUseId);
        Set(plantedDecision.Attributes, "tool_parameters", Text(Parameters(run.Command)));
        var plantedResult = result.Record.Clone();
        Stamp(plantedResult, started + Duration, run.Sequence + 1, run.ToolUseId);
        Set(plantedResult.Attributes, "tool_parameters", Text(Parameters(run.Command)));
        var input = new JsonObject { ["command"] = run.Command, ["description"] = "Run the gate" };
        if (run.InBackground)
            input["run_in_background"] = true;
        Set(plantedResult.Attributes, "tool_input", Text(input.ToJsonString()));
        Set(plantedResult.Attributes, "success", Text(run.Success ? "true" : "false"));
        Set(plantedResult.Attributes, "duration_ms", Text("500"));
        if (run.Success)
        {
            Remove(plantedResult.Attributes, "error_type");
            Remove(plantedResult.Attributes, "error");
        }
        else
        {
            Set(plantedResult.Attributes, "error_type", Text("ShellError"));
            Set(plantedResult.Attributes, "error", Text("Shell command failed"));
        }

        var scope = new ScopeLogs { Scope = result.Scope.Scope?.Clone(), SchemaUrl = result.Scope.SchemaUrl };
        scope.LogRecords.Add(plantedDecision);
        scope.LogRecords.Add(plantedResult);
        var resource = new ResourceLogs { Resource = result.Resource.Resource?.Clone(), SchemaUrl = result.Resource.SchemaUrl };
        resource.ScopeLogs.Add(scope);
        var request = new ExportLogsServiceRequest();
        request.ResourceLogs.Add(resource);
        return new OtlpBody("logs", result.Body.Session, "planted " + run.ToolUseId + ": " + run.Command,
            request.ToByteArray());

        static void Stamp(WireLogRecord record, ulong time, long sequence, string toolUseId)
        {
            record.TimeUnixNano = time;
            record.ObservedTimeUnixNano = time;
            record.TraceId = ByteString.Empty;
            record.SpanId = ByteString.Empty;
            record.Flags = 0;
            Set(record.Attributes, "event.timestamp", Text(Timestamp(time)));
            Set(record.Attributes, "event.sequence", Integer(sequence));
            Set(record.Attributes, "tool_use_id", Text(toolUseId));
        }
    }

    /// <summary>
    ///     Changes the command line of one recorded Bash call in place, on its events and its <c>claude_code.tool</c>
    ///     span, keeping its time, order and outcome.
    /// </summary>
    internal static IReadOnlyList<OtlpBody> WithCommand(IReadOnlyList<OtlpBody> bodies, string toolUseId,
        string command)
    {
        return Edit(bodies,
            logs: (_, request) =>
            {
                foreach (var record in request.ResourceLogs.SelectMany(static r => r.ScopeLogs)
                             .SelectMany(static s => s.LogRecords)
                             .Where(r => Text(r.Attributes, "tool_use_id") == toolUseId))
                {
                    Set(record.Attributes, "tool_parameters", Text(Parameters(command)));
                    if (Text(record.Attributes, "tool_input") is not null)
                        Set(record.Attributes, "tool_input",
                            Text(new JsonObject { ["command"] = command, ["description"] = "Run the gate" }
                                .ToJsonString()));
                }
            },
            traces: (_, request) =>
            {
                foreach (var span in request.ResourceSpans.SelectMany(static r => r.ScopeSpans)
                             .SelectMany(static s => s.Spans)
                             .Where(s => Text(s.Attributes, "tool_use_id") == toolUseId &&
                                         Text(s.Attributes, "full_command") is not null))
                {
                    Set(span.Attributes, "full_command", Text(command));
                    Set(span.Attributes, "bash_argv0", Text(ArgumentZero(command)));
                }
            });
    }

    /// <summary>
    ///     Puts a sentinel into every content-bearing attribute the research ledger lists (15 Nov), each as
    ///     <paramref name="prefix" /> followed by the attribute's name: <c>prompt</c> and <c>prompt_text</c> of
    ///     <c>user_prompt</c>, <c>response</c> of <c>assistant_response</c>, every string inside <c>tool_input</c>,
    ///     the <c>api_request_body</c> and <c>api_response_body</c> events, the <c>system_prompt</c> event, the
    ///     interaction span's <c>user_prompt</c>, the detailed-tracing span attributes <c>new_context</c>,
    ///     <c>system_reminders</c>, <c>system_prompt_preview</c>, <c>user_system_prompt</c> and
    ///     <c>response.model_output</c>, and a <c>tool.output</c> span event. Commands in <c>tool_parameters</c> stay.
    /// </summary>
    internal static IReadOnlyList<OtlpBody> WithContentSentinels(IReadOnlyList<OtlpBody> bodies, string prefix)
    {
        var sequence = 10_000L;
        return Edit(bodies,
            logs: (_, request) =>
            {
                foreach (var scope in request.ResourceLogs.SelectMany(static r => r.ScopeLogs))
                {
                    var planted = new List<WireLogRecord>();
                    foreach (var record in scope.LogRecords)
                    {
                        switch (Text(record.Attributes, "event.name"))
                        {
                            case "user_prompt":
                                Set(record.Attributes, "prompt", Text(prefix + "prompt"));
                                Set(record.Attributes, "prompt_text", Text(prefix + "prompt_text"));
                                planted.Add(Event(record, "system_prompt", ("system_prompt", prefix + "system_prompt"),
                                    ("prompt", prefix + "system_prompt.prompt")));
                                break;
                            case "assistant_response":
                                Set(record.Attributes, "response", Text(prefix + "response"));
                                break;
                            case "tool_result" when Text(record.Attributes, "tool_input") is { } input:
                                Set(record.Attributes, "tool_input", Text(WithSentinelStrings(input, prefix + "tool_input")));
                                break;
                            case "api_request":
                                planted.Add(Event(record, "api_request_body",
                                    ("body", "{\"messages\":[{\"role\":\"user\",\"content\":\"" + prefix + "api_request_body\"}]}")));
                                planted.Add(Event(record, "api_response_body",
                                    ("body", "{\"content\":[{\"type\":\"text\",\"text\":\"" + prefix + "api_response_body\"}]}")));
                                break;
                        }
                    }

                    scope.LogRecords.Add(planted);
                }
            },
            traces: (_, request) =>
            {
                foreach (var span in request.ResourceSpans.SelectMany(static r => r.ScopeSpans)
                             .SelectMany(static s => s.Spans))
                {
                    switch (span.Name)
                    {
                        case "claude_code.interaction":
                            Set(span.Attributes, "user_prompt", Text(prefix + "user_prompt"));
                            Set(span.Attributes, "new_context", Text(prefix + "new_context"));
                            break;
                        case "claude_code.llm_request":
                            foreach (var key in s_llmRequestContentKeys)
                                Set(span.Attributes, key, Text(prefix + key));
                            break;
                        case "claude_code.tool":
                            Set(span.Attributes, "new_context", Text(prefix + "new_context"));
                            var output = new Span.Types.Event { Name = "tool.output", TimeUnixNano = span.EndTimeUnixNano };
                            foreach (var key in s_toolOutputContentKeys)
                                output.Attributes.Add(new KeyValue { Key = key, Value = Text(prefix + "tool.output." + key) });
                            span.Events.Add(output);
                            break;
                    }
                }
            });

        WireLogRecord Event(WireLogRecord neighbour, string name, params (string Key, string Value)[] content)
        {
            var record = neighbour.Clone();
            record.Body = Text("claude_code." + name);
            var kept = record.Attributes.Where(static a => s_standardEventKeys.Contains(a.Key)).ToArray();
            record.Attributes.Clear();
            record.Attributes.Add(kept);
            Set(record.Attributes, "event.name", Text(name));
            Set(record.Attributes, "event.sequence", Integer(++sequence));
            foreach (var (key, value) in content)
            {
                Set(record.Attributes, key, Text(value));
                Set(record.Attributes, key + "_length", Integer(value.Length));
            }

            return record;
        }
    }

    private static string WithSentinelStrings(string json, string sentinel)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        foreach (var (key, value) in node.ToArray())
            if (value is JsonValue text && text.TryGetValue<string>(out var current))
                node[key] = current + " " + sentinel;
        return node.ToJsonString();
    }

    private static string Parameters(string command)
    {
        return new JsonObject
        {
            ["bash_command"] = ArgumentZero(command), ["full_command"] = command, ["description"] = "Run the gate"
        }.ToJsonString();
    }

    private static string ArgumentZero(string command) => command.Split(' ', 2)[0];

    private static string Timestamp(ulong unixNano)
    {
        return DateTimeOffset.UnixEpoch.AddTicks(checked((long)(unixNano / 100)))
            .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>The string value of an attribute, or null when it is absent or not a string.</summary>
    internal static string? Text(RepeatedField<KeyValue> attributes, string key)
    {
        var value = attributes.LastOrDefault(a => a.Key == key)?.Value;
        return value?.ValueCase == AnyValue.ValueOneofCase.StringValue ? value.StringValue : null;
    }

    internal static AnyValue Text(string value) => new() { StringValue = value };

    private static AnyValue Integer(long value) => new() { IntValue = value };

    private static void Set(RepeatedField<KeyValue> attributes, string key, AnyValue value)
    {
        Remove(attributes, key);
        attributes.Add(new KeyValue { Key = key, Value = value });
    }

    private static void Remove(RepeatedField<KeyValue> attributes, string key)
    {
        for (var i = attributes.Count - 1; i >= 0; i--)
            if (attributes[i].Key == key)
                attributes.RemoveAt(i);
    }
}
