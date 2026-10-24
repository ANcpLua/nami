using System.Text.Json.Nodes;
using System.Xml;
using Nami.Collector.Storage;

namespace Nami.Tools;

internal static class SourcesStatusTool
{
    public static ToolRows Run(ISnapshot snapshot, ToolWindow window)
    {
        var services = snapshot.Signals.Select(static s => s.ServiceName)
            .Concat(snapshot.Resources.Select(static r => r.ServiceName)).Distinct(StringComparer.Ordinal)
            .Where(window.Matches).Order(StringComparer.Ordinal).ToArray();
        if (services.Length == 0) services = [window.Service ?? "unknown"];
        var rows = new List<JsonObject>();
        foreach (var service in services)
        {
            var signals = new JsonArray();
            foreach (var signal in new[] { "traces", "metrics", "logs" })
            {
                var lastSeen = snapshot.Signals.Where(s => s.ServiceName == service && s.Signal == signal)
                    .Select(static s => s.LastSeenUnixNano).DefaultIfEmpty(0UL).Max();
                var active = lastSeen != 0 && lastSeen >= NamiTimeConversions.ToUnixNanoUnsigned(window.Start);
                var status = new JsonObject { ["signal"] = signal, ["kind"] = active ? "active" : "silent" };
                if (lastSeen != 0) status["last_seen"] = ToolDispatcher.Timestamp(lastSeen);
                if (!active)
                {
                    var last = lastSeen == 0 ? window.Start : NamiTimeConversions.NanosToDateTimeOffset(lastSeen);
                    status["silent_for"] =
                        XmlConvert.ToString(window.RequestedEnd > last ? window.RequestedEnd - last : TimeSpan.Zero);
                }

                signals.Add((JsonNode)status);
            }

            rows.Add(new JsonObject { ["service"] = service, ["signals"] = signals });
        }

        return new ToolRows("services", rows, snapshot.Signals.Count, ["traces", "metrics", "logs"],
            ["service", "signals"]);
    }
}

internal static class ErrorsRecentTool
{
    public static ToolRows Run(ISnapshot snapshot, ToolWindow window)
    {
        var errors = new List<Occurrence>();
        var spans = snapshot.Spans.ToDictionary(static s => (s.TraceId, s.SpanId));
        var correlatedLogs = new HashSet<(string?, string?)>();
        foreach (var log in snapshot.Logs)
        {
            var time = log.EffectiveTimeUnixNano() ?? log.ReceivedTimeUnixNano ?? 0;
            if (!window.Matches(log.ServiceName) || !window.Contains(time, "logs") ||
                log.EventName != "exception") continue;
            var type = ToolValues.Attribute(log.AttributesJson, "exception.type") ??
                       ToolValues.Attribute(log.AttributesJson, "error.type") ?? "exception";
            var operation =
                log.TraceId is not null && log.SpanId is not null &&
                spans.TryGetValue((log.TraceId, log.SpanId), out var span)
                    ? span.Name
                    : "log";
            errors.Add(new Occurrence(type, operation, time, log.TraceId, "log_event"));
            correlatedLogs.Add((log.TraceId, log.SpanId));
        }

        foreach (var span in snapshot.Spans)
        {
            if (!window.Matches(span.ServiceName)) continue;
            var events = SpanChildStorage.DeserializeEvents(span.EventsJson) ?? [];
            var exceptions = events.Where(static e => e.Name == "exception").ToArray();
            foreach (var error in exceptions)
            {
                var time = error.TimeUnixNano == 0 ? span.StartTimeUnixNano : error.TimeUnixNano;
                if (window.Contains(time, "traces"))
                    errors.Add(new Occurrence(
                        ToolValues.Attribute(error.AttributesJson, "exception.type") ?? "exception", span.Name, time,
                        span.TraceId, "span_event"));
            }

            if (span.StatusCode == 2 && window.Contains(span.StartTimeUnixNano, "traces") && exceptions.Length == 0 &&
                !correlatedLogs.Contains((span.TraceId, span.SpanId)))
                errors.Add(new Occurrence(ToolValues.Attribute(span.AttributesJson, "error.type") ?? "error", span.Name,
                    span.StartTimeUnixNano, span.TraceId, "span_status"));
        }

        var groups = errors.GroupBy(static e => (e.Type, e.Operation)).OrderByDescending(static g => g.Count())
            .ThenBy(static g => g.Key.Type, StringComparer.Ordinal)
            .ThenBy(static g => g.Key.Operation, StringComparer.Ordinal)
            .Select(static g =>
            {
                var preferredSource = g.Any(static e => e.Source == "log_event") ? "log_event" :
                    g.Any(static e => e.Source == "span_event") ? "span_event" : "span_status";
                var row = new JsonObject
                {
                    ["error_type"] = g.Key.Type, ["operation"] = g.Key.Operation,
                    ["first_seen"] = ToolDispatcher.Timestamp(g.Min(static e => e.Time)),
                    ["last_seen"] = ToolDispatcher.Timestamp(g.Max(static e => e.Time)),
                    ["count"] = g.LongCount(), ["source"] = preferredSource
                };
                var traceId = g.Select(static e => e.TraceId).Where(static t => t is not null)
                    .Order(StringComparer.Ordinal).FirstOrDefault();
                if (traceId is not null) row["example_trace_id"] = traceId;
                return row;
            }).ToArray();
        return new ToolRows("groups", groups, snapshot.Spans.Count + snapshot.Logs.Count, ["traces", "logs"],
            ["error_type", "operation", "first_seen", "last_seen", "count", "example_trace_id", "source"]);
    }

    private sealed record Occurrence(string Type, string Operation, ulong Time, string? TraceId, string Source);
}

internal static class TracesSlowestTool
{
    public static ToolRows Run(ISnapshot snapshot, ToolWindow window, string? operation, int limit)
    {
        var traces = snapshot.Spans.GroupBy(static s => s.TraceId).Select(g =>
            {
                var spans = g.OrderBy(static s => s.StartTimeUnixNano)
                    .ThenBy(static s => s.SpanId, StringComparer.Ordinal).ToArray();
                var ids = spans.Select(static s => s.SpanId).ToHashSet(StringComparer.Ordinal);
                var root = spans.FirstOrDefault(s => s.ParentSpanId is null || !ids.Contains(s.ParentSpanId)) ??
                           spans[0];
                return (Spans: spans, Root: root, Start: spans.Min(static s => s.StartTimeUnixNano),
                    End: spans.Max(static s => s.EndTimeUnixNano));
            }).Where(t =>
                window.Contains(t.Start, "traces") && t.Spans.Any(s => window.Matches(s.ServiceName)) &&
                (operation is null || t.Root.Name == operation))
            .OrderByDescending(static t => t.End - t.Start).ThenBy(static t => t.Root.TraceId, StringComparer.Ordinal)
            .Take(limit)
            .Select(static t => new JsonObject
            {
                ["trace_id"] = t.Root.TraceId, ["operation"] = t.Root.Name,
                ["duration_ms"] = (t.End - t.Start) / 1_000_000d,
                ["started_at"] = ToolDispatcher.Timestamp(t.Start),
                ["status"] = t.Spans.Any(static s => s.StatusCode == 2) ? "error" :
                    t.Spans.Any(static s => s.StatusCode == 1) ? "ok" : "unset"
            }).ToArray();
        return new ToolRows("traces", traces, snapshot.Spans.Count, ["traces"],
            ["trace_id", "operation", "duration_ms", "started_at", "status"]);
    }
}

internal static class LogsSearchTool
{
    public static ToolRows Run(ISnapshot snapshot, ToolWindow window, string query)
    {
        var logs = snapshot.Logs.Where(l =>
                window.Matches(l.ServiceName) &&
                window.Contains(l.EffectiveTimeUnixNano() ?? l.ReceivedTimeUnixNano ?? 0, "logs"))
            .Select(static l => (Row: l, Body: ToolValues.Body(l.Body)))
            .Where(l => l.Body.Contains(query, StringComparison.Ordinal))
            .OrderByDescending(static l => l.Row.EffectiveTimeUnixNano())
            .ThenBy(static l => l.Row.LogId, StringComparer.Ordinal)
            .Select(static l =>
            {
                var row = new JsonObject
                {
                    ["timestamp"] =
                        ToolDispatcher.Timestamp(l.Row.EffectiveTimeUnixNano() ?? l.Row.ReceivedTimeUnixNano ?? 0),
                    ["severity"] = l.Row.SeverityText ?? ToolValues.Severity(l.Row.SeverityNumber),
                    ["body"] = l.Body, ["service"] = l.Row.ServiceName ?? "unknown"
                };
                if (l.Row.TraceId is not null) row["trace_id"] = l.Row.TraceId;
                if (l.Row.SpanId is not null) row["span_id"] = l.Row.SpanId;
                return row;
            }).ToArray();
        return new ToolRows("records", logs, snapshot.Logs.Count, ["logs"],
            ["timestamp", "severity", "body", "trace_id", "span_id", "service"]);
    }
}

internal static class ChangesRecentTool
{
    public static ToolRows Run(ISnapshot snapshot, ToolWindow window)
    {
        var changes = new List<JsonObject>();
        foreach (var service in snapshot.Resources.Where(r => window.Matches(r.ServiceName))
                     .GroupBy(static r => r.ServiceName).OrderBy(static g => g.Key, StringComparer.Ordinal))
        {
            var prior = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var resource in service.OrderBy(static r => r.ObservedUnixNano)
                         .ThenBy(static r => r.SequenceNumber))
            {
                if (resource.ObservedUnixNano >= NamiTimeConversions.ToUnixNanoUnsigned(window.End)) continue;
                foreach (var (key, kind) in Keys(resource.AttributesJson))
                {
                    var value = ToolValues.Attribute(resource.AttributesJson, key);
                    if (value is null) continue;
                    prior.TryGetValue(key, out var previous);
                    if (value != previous && window.Contains(resource.ObservedUnixNano))
                    {
                        var row = new JsonObject
                        {
                            ["observed_at"] = ToolDispatcher.Timestamp(resource.ObservedUnixNano),
                            ["service"] = service.Key,
                            ["kind"] = kind, ["to"] = value, ["source_key"] = key
                        };
                        if (previous is not null) row["from"] = previous;
                        changes.Add(row);
                    }

                    prior[key] = value;
                }
            }
        }

        var rows = changes.OrderByDescending(static r => r["observed_at"]!.GetValue<string>(), StringComparer.Ordinal)
            .ThenBy(static r => r["service"]!.GetValue<string>(), StringComparer.Ordinal)
            .ThenBy(static r => r["source_key"]!.GetValue<string>(), StringComparer.Ordinal).ToArray();
        return new ToolRows("changes", rows, snapshot.Resources.Count, ["traces", "metrics", "logs"],
            ["observed_at", "service", "kind", "from", "to", "source_key"]);
    }

    private static IEnumerable<(string Key, string Kind)> Keys(string json)
    {
        yield return ("vcs.ref.head.revision", "revision");
        yield return ("service.version", "version");
        yield return ("deployment.environment.name", "environment");
        if (JsonNode.Parse(json) is JsonObject obj)
            foreach (var key in obj.Select(static p => p.Key)
                         .Where(static k => k.StartsWith("cicd.", StringComparison.Ordinal))
                         .Order(StringComparer.Ordinal))
                yield return (key, "pipeline");
    }
}

internal static class TracesGetTool
{
    public static ToolRows Run(ISnapshot snapshot, ToolWindow window, string traceId)
    {
        var spans = snapshot.Spans.Where(s => s.TraceId == traceId && window.Contains(s.StartTimeUnixNano, "traces"))
            .OrderBy(static s => s.StartTimeUnixNano).ThenBy(static s => s.SpanId, StringComparer.Ordinal).ToArray();
        if (spans.Length == 0 || !spans.Any(s => window.Matches(s.ServiceName)))
            throw new ToolNotFoundException("trace_id", traceId);
        var ids = spans.Select(static s => s.SpanId).ToHashSet(StringComparer.Ordinal);
        var root = spans.FirstOrDefault(s => s.ParentSpanId is null || !ids.Contains(s.ParentSpanId)) ?? spans[0];
        var start = spans.Min(static s => s.StartTimeUnixNano);
        var end = spans.Max(static s => s.EndTimeUnixNano);
        var result = new JsonObject
        {
            ["trace_id"] = traceId, ["root_span"] = ToolValues.Span(root), ["span_count"] = spans.Length,
            ["duration_ns"] = (end - start).ToString(CultureInfo.InvariantCulture),
            ["start_time"] = ToolDispatcher.Timestamp(start), ["end_time"] = ToolDispatcher.Timestamp(end),
            ["services"] = new JsonArray(spans.Select(static s => s.ServiceName ?? "unknown")
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(static s => JsonValue.Create(s))
                .ToArray()),
            ["has_error"] = spans.Any(static s => s.StatusCode == 2),
            ["spans"] = new JsonArray(spans.Select(ToolValues.Span).ToArray())
        };
        return new ToolRows("trace", [result], spans.Length, ["traces"],
        [
            "trace_id", "root_span", "span_count", "duration_ns", "start_time", "end_time", "services", "has_error",
            "spans"
        ], true);
    }
}

internal static class ToolValues
{
    internal static string? Attribute(string? json, string key)
    {
        return json is not null && JsonNode.Parse(json) is JsonObject obj && obj[key] is { } value
            ? Body(value.ToJsonString())
            : null;
    }

    internal static string Body(string? json)
    {
        if (json is null) return "";
        try
        {
            var node = JsonNode.Parse(json);
            return node is JsonValue scalar && scalar.TryGetValue<string>(out var text)
                ? text
                : node?.ToJsonString() ?? "";
        }
        catch (JsonException)
        {
            // Rows predating typed log-body storage contain plain text.
            return json;
        }
    }

    internal static string Severity(byte severity)
    {
        return severity switch
        {
            >= 21 => "FATAL", >= 17 => "ERROR", >= 13 => "WARN", >= 9 => "INFO", >= 5 => "DEBUG", >= 1 => "TRACE",
            _ => "UNSPECIFIED"
        };
    }

    internal static JsonArray Attributes(string? json)
    {
        return JsonNode.Parse(json ?? "{}") is JsonObject obj
            ? new JsonArray(obj.OrderBy(static p => p.Key, StringComparer.Ordinal).Select(static p => new JsonObject
            {
                ["key"] = p.Key, ["value"] = ProjectAttributeValue(p.Value),
                ["stability"] = AttributeStabilityWire.Name(p.Key)
            }).ToArray())
            : [];
    }

    private static JsonNode? ProjectAttributeValue(JsonNode? stored)
    {
        if (stored is JsonArray array)
            return new JsonArray(array.Select(ProjectAttributeValue).ToArray());
        if (stored is not JsonObject tagged || tagged["type"] is not JsonValue tagValue ||
            !tagValue.TryGetValue<string>(out var tag) || tag is not ("int" or "double" or "bytes" or "kvlist"))
            return stored?.DeepClone();
        var result = new JsonObject { ["kind"] = tag };
        foreach (var property in tagged)
        {
            if (property.Key == "type") continue;
            // A kvlist's values object is a user map: its keys are data, even when named type/kind.
            result[property.Key] = tag == "kvlist" && property is { Key: "values", Value: JsonObject values }
                ? new JsonObject(values.Select(static p => KeyValuePair.Create(p.Key, ProjectAttributeValue(p.Value))))
                : ProjectAttributeValue(property.Value);
        }

        return result;
    }

    internal static JsonObject Span(SpanStorageRow span)
    {
        var row = new JsonObject
        {
            ["span_id"] = span.SpanId, ["trace_id"] = span.TraceId, ["name"] = span.Name, ["kind"] = span.Kind,
            ["start_time_unix_nano"] = span.StartTimeUnixNano.ToString(CultureInfo.InvariantCulture),
            ["end_time_unix_nano"] = span.EndTimeUnixNano.ToString(CultureInfo.InvariantCulture),
            ["attributes"] = Attributes(span.AttributesJson),
            ["status"] = new JsonObject { ["code"] = span.StatusCode, ["message"] = span.StatusMessage },
            ["resource"] = new JsonObject
            {
                ["service_name"] = span.ServiceName ?? "unknown", ["attributes"] = Attributes(span.ResourceJson),
                ["schema_url"] = span.ResourceSchemaUrl
            }
        };
        if (span.Flags is { } flags) row["flags"] = flags;
        if (span.DroppedAttributesCount is { } droppedAttributes) row["dropped_attributes_count"] = droppedAttributes;
        if (span.DroppedEventsCount is { } droppedEvents) row["dropped_events_count"] = droppedEvents;
        if (span.DroppedLinksCount is { } droppedLinks) row["dropped_links_count"] = droppedLinks;
        var resource = (JsonObject)row["resource"]!;
        if (span.ResourceDroppedAttributesCount is { } resourceDropped)
            resource["dropped_attributes_count"] = resourceDropped;
        if (span.ResourceEntityRefsJson is { } refs && JsonNode.Parse(refs) is JsonArray references)
            resource["entity_refs"] = new JsonArray(references.OfType<JsonObject>().Select(static r =>
                new JsonObject
                    { ["type"] = r["type"]?.DeepClone(), ["id_keys"] = r["idKeys"]?.DeepClone() }).ToArray());
        if (span.ScopeName is not null || span.ScopeVersion is not null || span.ScopeAttributesJson is not null)
        {
            row["instrumentation_scope"] = new JsonObject
            {
                ["name"] = span.ScopeName, ["version"] = span.ScopeVersion,
                ["attributes"] = Attributes(span.ScopeAttributesJson), ["schema_url"] = span.ScopeSchemaUrl,
                ["dropped_attributes_count"] = span.ScopeDroppedAttributesCount
            };
        }

        if (span.ParentSpanId is not null) row["parent_span_id"] = span.ParentSpanId;
        if (span.TraceState is not null) row["trace_state"] = span.TraceState;
        var events = SpanChildStorage.DeserializeEvents(span.EventsJson);
        if (events is { Count: > 0 })
        {
            row["events"] = new JsonArray(events.Select(static e => new JsonObject
            {
                ["name"] = e.Name, ["time_unix_nano"] = e.TimeUnixNano.ToString(CultureInfo.InvariantCulture),
                ["attributes"] = Attributes(e.AttributesJson), ["dropped_attributes_count"] = e.DroppedAttributesCount
            }).ToArray());
        }

        var links = SpanChildStorage.DeserializeLinks(span.LinksJson);
        if (links is { Count: > 0 })
        {
            row["links"] = new JsonArray(links.Select(static l => new JsonObject
            {
                ["trace_id"] = l.TraceId, ["span_id"] = l.SpanId, ["trace_state"] = l.TraceState,
                ["attributes"] = Attributes(l.AttributesJson), ["dropped_attributes_count"] = l.DroppedAttributesCount,
                ["flags"] = l.Flags
            }).ToArray());
        }

        return row;
    }
}
