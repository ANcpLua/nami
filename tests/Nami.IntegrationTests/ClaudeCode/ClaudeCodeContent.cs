using Google.Protobuf;
using Google.Protobuf.Collections;
using Nami.SemanticConventions;
using Nami.Tests.Wire.OpenTelemetry.Proto.Collector.Logs.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Collector.Trace.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Common.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Trace.V1;
using ProtoLog = Nami.Tests.Wire.OpenTelemetry.Proto.Logs.V1.LogRecord;

namespace Nami.IntegrationTests.ClaudeCode;

/// <summary>The three markers that <see cref="ClaudeCodeContent.Plant" /> writes.</summary>
/// <param name="Content">Goes into every content key as <c>{Content}:{key}</c>.</param>
/// <param name="Detail">Goes into every tool-detail key as <c>{Detail}:{key}</c>.</param>
/// <param name="Control">Goes into <c>gen_ai.agent.name</c>, a key nami keeps, on every record that carries either.</param>
internal sealed record ContentMarkers(string Content, string Detail, string Control);

/// <summary>A capture with content planted, and how many records of each kind carry it.</summary>
/// <param name="Exports">The bodies in arrival order; metrics bodies are unchanged.</param>
/// <param name="Logs">Log records (events) that carry planted content or tool details.</param>
/// <param name="Spans">Spans that carry planted content or tool details in their own attributes.</param>
/// <param name="ToolOutputs">Spans that carry a planted <c>tool.output</c> span event.</param>
internal sealed record PlantedCapture(IReadOnlyList<ClaudeCodeExport> Exports, int Logs, int Spans, int ToolOutputs);

/// <summary>
///     Every place where Claude Code 2.1.325 can send prompt, response, file or tool content, and the tool details,
///     after code.claude.com/docs/en/monitoring-usage (read 15 Nov 2026) and the research ledger, "Claude Code
///     telemetry as sent". The content events that a capture without content variables lacks are added the way
///     Claude Code sends them, a log record whose <c>event.name</c> attribute is <c>{name}</c>, with the standard
///     attributes of the <c>api_request</c> record it follows, except for the body: Claude Code puts
///     <c>claude_code.{name}</c> there, and these records carry content there instead, because no key names a body
///     and so no registry decision covers it (the evaluator's gap on M2 ingest step 2). Every record that carries a
///     planted value also carries the control marker, so a reader can tell a record that was stored without its
///     content from one that was not stored at all.
/// </summary>
internal static class ClaudeCodeContent
{
    /// <summary>Content in event (log record) attributes, by event name; each event's gate variable is in the comment.</summary>
    internal static IReadOnlyDictionary<string, string[]> EventKeys { get; } = new Dictionary<string, string[]>
    {
        ["user_prompt"] = ["prompt", "prompt_text"], // OTEL_LOG_USER_PROMPTS
        ["assistant_response"] = ["response"], // OTEL_LOG_ASSISTANT_RESPONSES
        ["tool_result"] = ["tool_input"], // OTEL_LOG_TOOL_DETAILS: subagent prompts, old and new text of edits
        ["api_request_body"] = ["body"], // OTEL_LOG_RAW_API_BODIES=1: the Messages API request
        ["api_response_body"] = ["body"], // OTEL_LOG_RAW_API_BODIES=1: the Messages API response
        // ENABLE_BETA_TRACING_DETAILED with OTEL_LOG_USER_PROMPTS: "carries the complete system prompt". The
        // documentation names no attribute, so the text goes under the names Claude Code uses for its other content.
        ["system_prompt"] = ["system_prompt", "prompt", "body"]
    };

    /// <summary>Content in span attributes, by span name.</summary>
    internal static IReadOnlyDictionary<string, string[]> SpanKeys { get; } = new Dictionary<string, string[]>
    {
        // user_prompt: OTEL_LOG_USER_PROMPTS; new_context: ENABLE_BETA_TRACING_DETAILED, the user prompt
        ["claude_code.interaction"] = ["user_prompt", "new_context"],
        // ENABLE_BETA_TRACING_DETAILED with OTEL_LOG_USER_PROMPTS
        ["claude_code.llm_request"] =
            ["new_context", "system_reminders", "system_prompt_preview", "user_system_prompt", "response.model_output"],
        // ENABLE_BETA_TRACING_DETAILED: new_context (the tool result) and tool_input (the serialized tool input)
        ["claude_code.tool"] = ["new_context", "tool_input"]
    };

    /// <summary>Content in the <c>tool.output</c> span event on <c>claude_code.tool</c> (OTEL_LOG_TOOL_CONTENT).</summary>
    internal static string[] ToolOutputKeys { get; } = ["content", "output", "diff"];

    /// <summary>
    ///     Tool details (OTEL_LOG_TOOL_DETAILS) in event attributes: the tool parameters with the Bash command, and the
    ///     full error message of a failed tool.
    /// </summary>
    internal static IReadOnlyDictionary<string, string[]> EventDetailKeys { get; } = new Dictionary<string, string[]>
    {
        ["tool_decision"] = ["tool_parameters"],
        ["tool_result"] = ["tool_parameters", "error"]
    };

    /// <summary>Tool details in span attributes, by span name.</summary>
    internal static IReadOnlyDictionary<string, string[]> SpanDetailKeys { get; } = new Dictionary<string, string[]>
    {
        ["claude_code.tool"] = ["full_command", "file_path"],
        ["claude_code.tool.execution"] = ["error"]
    };

    /// <summary>Tool details in the <c>tool.output</c> span event.</summary>
    internal static string[] ToolOutputDetailKeys { get; } = ["bash_command", "file_path"];

    /// <summary>Every tool-detail key that is planted, once.</summary>
    internal static IReadOnlyList<string> DetailKeys { get; } =
    [
        .. EventDetailKeys.Values.Concat(SpanDetailKeys.Values).Append(ToolOutputDetailKeys)
            .SelectMany(static keys => keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
    ];

    /// <summary>The events that Claude Code sends only with content variables, added after each api_request.</summary>
    private static readonly string[] s_bodyEvents = ["api_request_body", "api_response_body"];

#pragma warning disable CS0618 // Claude Code 2.1.325 names each event only in this deprecated attribute; the fixture writes it as Claude Code does.
    private const string EventNameKey = EventAttributes.Name;
#pragma warning restore CS0618

    internal static PlantedCapture Plant(IReadOnlyList<ClaudeCodeExport> exports, ContentMarkers markers)
    {
        var planted = new List<ClaudeCodeExport>(exports.Count);
        var (logs, spans, toolOutputs) = (0, 0, 0);
        var sessionsWithSystemPrompt = new HashSet<string>(StringComparer.Ordinal);
        foreach (var export in exports)
        {
            switch (export.Signal)
            {
                case Signals.Logs:
                    {
                        var request = ExportLogsServiceRequest.Parser.ParseFrom(export.Payload);
                        foreach (var scope in request.ResourceLogs.SelectMany(static resource => resource.ScopeLogs))
                        {
                            var records = scope.LogRecords.ToArray();
                            scope.LogRecords.Clear();
                            foreach (var record in records)
                            {
                                scope.LogRecords.Add(record);
                                var name = Text(record.Attributes, EventNameKey) ?? "";
                                if (Fill(record.Attributes, EventKeys.GetValueOrDefault(name),
                                        EventDetailKeys.GetValueOrDefault(name), markers))
                                    logs++;

                                if (name != "api_request") continue;
                                var added = s_bodyEvents.ToList();
                                if (sessionsWithSystemPrompt.Add(Text(record.Attributes, SessionAttributes.Id) ?? ""))
                                    added.Add("system_prompt");
                                foreach (var eventName in added)
                                {
                                    scope.LogRecords.Add(ContentEvent(record, eventName, markers));
                                    logs++;
                                }
                            }
                        }

                        planted.Add(export with { Payload = request.ToByteArray() });
                        break;
                    }
                case Signals.Traces:
                    {
                        var request = ExportTraceServiceRequest.Parser.ParseFrom(export.Payload);
                        foreach (var span in request.ResourceSpans.SelectMany(static resource => resource.ScopeSpans)
                                     .SelectMany(static scope => scope.Spans))
                        {
                            if (!Fill(span.Attributes, SpanKeys.GetValueOrDefault(span.Name),
                                    SpanDetailKeys.GetValueOrDefault(span.Name), markers))
                                continue;
                            spans++;
                            if (span.Name != "claude_code.tool") continue;
                            var output = new Span.Types.Event { Name = "tool.output", TimeUnixNano = span.EndTimeUnixNano };
                            Fill(output.Attributes, ToolOutputKeys, ToolOutputDetailKeys, markers);
                            span.Events.Add(output);
                            toolOutputs++;
                        }

                        planted.Add(export with { Payload = request.ToByteArray() });
                        break;
                    }
                default:
                    planted.Add(export);
                    break;
            }
        }

        return new PlantedCapture(planted, logs, spans, toolOutputs);
    }

    /// <summary>A content event that follows <paramref name="request" />, with content in its attributes and its body.</summary>
    private static ProtoLog ContentEvent(ProtoLog request, string eventName, ContentMarkers markers)
    {
        var record = request.Clone();
        record.Body = new AnyValue { StringValue = markers.Content + ":body" };
        Set(record.Attributes, EventNameKey, eventName);
        Fill(record.Attributes, EventKeys[eventName], null, markers);
        return record;
    }

    /// <summary>Plants the content and the tool details; false when the record carries neither.</summary>
    private static bool Fill(RepeatedField<KeyValue> attributes, string[]? content, string[]? details,
        ContentMarkers markers)
    {
        if (content is null && details is null) return false;
        foreach (var key in content ?? []) Set(attributes, key, markers.Content + ":" + key);
        foreach (var key in details ?? []) Set(attributes, key, markers.Detail + ":" + key);
        Set(attributes, GenAiAttributes.AgentName, markers.Control);
        return true;
    }

    private static void Set(RepeatedField<KeyValue> attributes, string key, string value)
    {
        var text = new AnyValue { StringValue = value };
        if (attributes.FirstOrDefault(attribute => attribute.Key == key) is { } existing) existing.Value = text;
        else attributes.Add(new KeyValue { Key = key, Value = text });
    }

    private static string? Text(RepeatedField<KeyValue> attributes, string key)
    {
        return attributes.FirstOrDefault(attribute => attribute.Key == key)?.Value is
            { ValueCase: AnyValue.ValueOneofCase.StringValue } value
            ? value.StringValue
            : null;
    }
}
