using Google.Protobuf;
using Google.Protobuf.Collections;
using Nami.SemanticConventions;
using Nami.Tests.Wire.OpenTelemetry.Proto.Collector.Logs.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Collector.Trace.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Common.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Trace.V1;
using ProtoLog = Nami.Tests.Wire.OpenTelemetry.Proto.Logs.V1.LogRecord;

namespace Nami.IntegrationTests.ClaudeCode;

/// <summary>A capture with content planted, and how many records of each kind carry it.</summary>
/// <param name="Exports">The bodies in arrival order; metrics bodies are unchanged.</param>
/// <param name="Logs">Log records (events) that carry planted content.</param>
/// <param name="Spans">Spans that carry planted content in their own attributes.</param>
/// <param name="ToolOutputs">Spans that carry a planted <c>tool.output</c> span event.</param>
internal sealed record PlantedCapture(IReadOnlyList<ClaudeCodeExport> Exports, int Logs, int Spans, int ToolOutputs);

/// <summary>
///     Every place where Claude Code 2.1.325 can send prompt, response, file or tool content, after
///     code.claude.com/docs/en/monitoring-usage (read 15 Nov 2026) and the research ledger, "Claude Code telemetry
///     as sent". Each is filled with <c>{sentinel}:{key}</c>. The content events that a capture without content
///     variables lacks are added the way Claude Code sends them: a log record whose body is
///     <c>claude_code.{name}</c> and whose <c>event.name</c> attribute is <c>{name}</c>, with the standard attributes
///     of the <c>api_request</c> record it follows. Every record that carries content also carries the control marker
///     in <c>gen_ai.agent.name</c>, a key nami keeps, so a reader can tell a record that was stored without its
///     content from one that was not stored at all.
/// </summary>
internal static class ClaudeCodeContent
{
    /// <summary>Event (log record) attributes, by event name; each event's gate variable is in the comment.</summary>
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

    /// <summary>Span attributes, by span name.</summary>
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

    /// <summary>The <c>tool.output</c> span event on <c>claude_code.tool</c> (OTEL_LOG_TOOL_CONTENT).</summary>
    internal static string[] ToolOutputKeys { get; } = ["content", "output", "diff"];

    /// <summary>The events that Claude Code sends only with content variables, added after each api_request.</summary>
    private static readonly string[] s_bodyEvents = ["api_request_body", "api_response_body"];

    internal static PlantedCapture Plant(IReadOnlyList<ClaudeCodeExport> exports, string sentinel, string control)
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
                                var name = Text(record.Attributes, EventNameKey);
                                if (name is not null && EventKeys.TryGetValue(name, out var keys))
                                {
                                    Fill(record.Attributes, keys, sentinel, control);
                                    logs++;
                                }

                                if (name != "api_request") continue;
                                var added = s_bodyEvents.ToList();
                                if (sessionsWithSystemPrompt.Add(Text(record.Attributes, SessionAttributes.Id) ?? ""))
                                    added.Add("system_prompt");
                                foreach (var eventName in added)
                                {
                                    scope.LogRecords.Add(ContentEvent(record, eventName, sentinel, control));
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
                            if (!SpanKeys.TryGetValue(span.Name, out var keys)) continue;
                            Fill(span.Attributes, keys, sentinel, control);
                            spans++;
                            if (span.Name != "claude_code.tool") continue;
                            var output = new Span.Types.Event { Name = "tool.output", TimeUnixNano = span.EndTimeUnixNano };
                            Fill(output.Attributes, ToolOutputKeys, sentinel, control);
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

#pragma warning disable CS0618 // Claude Code 2.1.325 names each event only in this deprecated attribute; the fixture writes it as Claude Code does.
    private const string EventNameKey = EventAttributes.Name;
#pragma warning restore CS0618

    /// <summary>A content event that follows <paramref name="request" />, as Claude Code sends it.</summary>
    private static ProtoLog ContentEvent(ProtoLog request, string eventName, string sentinel, string control)
    {
        var record = request.Clone();
        record.Body = new AnyValue { StringValue = "claude_code." + eventName };
        Set(record.Attributes, EventNameKey, eventName);
        Fill(record.Attributes, EventKeys[eventName], sentinel, control);
        return record;
    }

    private static void Fill(RepeatedField<KeyValue> attributes, IEnumerable<string> keys, string sentinel, string control)
    {
        foreach (var key in keys) Set(attributes, key, sentinel + ":" + key);
        Set(attributes, GenAiAttributes.AgentName, control);
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
