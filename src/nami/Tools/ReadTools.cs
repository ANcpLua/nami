using System.Text.Json.Nodes;
using System.Xml;
using Nami.Api.Contracts;
using Nami.Api.Contracts.OTel.Enums;
using Nami.Api.Contracts.OTel.Traces;
using Nami.Api.Contracts.Common;
using Nami.Api.Contracts.Tools;
using Nami.Collector.Storage;
using Attribute = Nami.Api.Contracts.Common.Attribute;
using Trace = Nami.Api.Contracts.OTel.Traces.Trace;

namespace Nami.Tools;

internal static class SourcesStatusTool
{
    public static ToolRows Run(TelemetrySnapshot snapshot, ToolWindow window)
    {
        var services = snapshot.Signals.Select(static s => s.ServiceName)
            .Concat(snapshot.Resources.Select(static r => r.ServiceName)).Distinct(StringComparer.Ordinal)
            .Where(window.Matches).Order(StringComparer.Ordinal).ToArray();
        if (services.Length == 0) services = [window.Service ?? "unknown"];
        var rows = new List<JsonObject>();
        foreach (var service in services)
        {
            var signals = new List<SignalStatus>();
            foreach (var signal in new[] { "traces", "metrics", "logs" })
            {
                var lastSeen = snapshot.Signals.Where(s => s.ServiceName == service && s.Signal == signal)
                    .Select(static s => s.LastSeenUnixNano).DefaultIfEmpty(0UL).Max();
                var active = lastSeen != 0 && lastSeen >= NamiTimeConversions.ToUnixNanoUnsigned(window.Start);
                SignalStatus status;
                if (active)
                    status = new ActiveSignalStatus { Signal = signal, LastSeen = ToolDispatcher.Timestamp(lastSeen) };
                else
                {
                    var last = lastSeen == 0 ? window.Start : NamiTimeConversions.NanosToDateTimeOffset(lastSeen);
                    status = new SilentSignalStatus
                    {
                        Signal = signal,
                        LastSeen = lastSeen == 0 ? null : ToolDispatcher.Timestamp(lastSeen),
                        SilentFor = WholeSeconds(window.End > last ? window.End - last : TimeSpan.Zero)
                    };
                }

                signals.Add(status);
            }

            rows.Add(ToolValues.Json(new SourceStatus { Service = service, Signals = signals }));
        }

        return new ToolRows("services", rows, snapshot.Signals.Count, ["traces", "metrics", "logs"],
            ContractFields.For<ProjectedSourceStatus>());
    }

    /// <summary>
    ///     The contract's IsoDuration has no fraction, so the silence is reported in the whole seconds that have
    ///     passed: 2.5 s is PT2S, less than a second is PT0S.
    /// </summary>
    private static string WholeSeconds(TimeSpan silence)
    {
        return XmlConvert.ToString(new TimeSpan(silence.Ticks - silence.Ticks % TimeSpan.TicksPerSecond));
    }
}

internal static class ErrorsRecentTool
{
    public static ToolRows Run(TelemetrySnapshot snapshot, ToolWindow window)
    {
        var errors = new List<Occurrence>();
        var spans = snapshot.Spans.ToDictionary(static s => (s.TraceId, s.SpanId));
        var correlatedLogs = new HashSet<(string?, string?)>();
        foreach (var log in snapshot.Logs)
        {
            var time = log.EffectiveTimeUnixNano() ?? log.ReceivedTimeUnixNano ?? 0;
            if (!window.Matches(log.ServiceName) || !window.Contains(time, "logs") ||
                ToolValues.EventName(log) != "exception") continue;
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
            errors.AddRange(from error in exceptions
                let time = error.TimeUnixNano == 0 ? span.StartTimeUnixNano : error.TimeUnixNano
                where window.Contains(time, "traces")
                select new Occurrence(
                    ToolValues.Attribute(error.AttributesJson, "exception.type") ?? "exception", span.Name, time,
                    span.TraceId, "span_event"));

            if (span.StatusCode == 2 && window.Contains(span.StartTimeUnixNano, "traces") && exceptions.Length == 0 &&
                !correlatedLogs.Contains((span.TraceId, span.SpanId)))
            {
                errors.Add(new Occurrence(ToolValues.Attribute(span.AttributesJson, "error.type") ?? "error", span.Name,
                    span.StartTimeUnixNano, span.TraceId, "span_status"));
            }
        }

        var groups = errors.GroupBy(static e => (e.Type, e.Operation)).OrderByDescending(static g => g.Count())
            .ThenBy(static g => g.Key.Type, StringComparer.Ordinal)
            .ThenBy(static g => g.Key.Operation, StringComparer.Ordinal)
            .Select(static g =>
            {
                var preferredSource = g.Any(static e => e.Source == "log_event") ? "log_event" :
                    g.Any(static e => e.Source == "span_event") ? "span_event" : "span_status";
                return ToolValues.Json(new ErrorGroup
                {
                    ErrorType = g.Key.Type,
                    Operation = g.Key.Operation,
                    FirstSeen = ToolDispatcher.Timestamp(g.Min(static e => e.Time)),
                    LastSeen = ToolDispatcher.Timestamp(g.Max(static e => e.Time)),
                    Count = g.LongCount(),
                    Source = preferredSource,
                    ExampleTraceId = g.Select(static e => e.TraceId).Where(static t => t is not null)
                        .Order(StringComparer.Ordinal).FirstOrDefault()
                });
            }).ToArray();
        return new ToolRows("groups", groups, snapshot.Spans.Count + snapshot.Logs.Count, ["traces", "logs"],
            ContractFields.For<ProjectedErrorGroup>());
    }

    private sealed record Occurrence(string Type, string Operation, ulong Time, string? TraceId, string Source);
}

internal static class TracesSlowestTool
{
    public static ToolRows Run(TelemetrySnapshot snapshot, ToolWindow window, string? operation, int limit)
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
            .OrderByDescending(static t => ToolValues.Elapsed(t.Start, t.End))
            .ThenBy(static t => t.Root.TraceId, StringComparer.Ordinal)
            .Take(limit)
            .Select(static t => ToolValues.Json(new SlowTrace
            {
                TraceId = t.Root.TraceId,
                Operation = t.Root.Name,
                DurationMs = ToolValues.Elapsed(t.Start, t.End) / 1_000_000d,
                StartedAt = ToolDispatcher.Timestamp(t.Start),
                Status = t.Spans.Any(static s => s.StatusCode == 2) ? "error" :
                    t.Spans.Any(static s => s.StatusCode == 1) ? "ok" : "unset"
            })).ToArray();
        return new ToolRows("traces", traces, snapshot.Spans.Count, ["traces"],
            ContractFields.For<ProjectedSlowTrace>());
    }
}

internal static class LogsSearchTool
{
    public static ToolRows Run(TelemetrySnapshot snapshot, ToolWindow window, string query)
    {
        var logs = snapshot.Logs.Where(l =>
                window.Matches(l.ServiceName) &&
                window.Contains(l.EffectiveTimeUnixNano() ?? l.ReceivedTimeUnixNano ?? 0, "logs"))
            .Select(static l => (Row: l, Body: ToolValues.Body(l.Body)))
            .Where(l => l.Body.Contains(query, StringComparison.Ordinal))
            .OrderByDescending(static l => l.Row.EffectiveTimeUnixNano())
            .ThenBy(static l => l.Row.LogId, StringComparer.Ordinal)
            .Select(static l => ToolValues.Json(new LogHit
                {
                    Timestamp = ToolDispatcher.Timestamp(l.Row.EffectiveTimeUnixNano() ??
                                                         l.Row.ReceivedTimeUnixNano ?? 0),
                    Severity = l.Row.SeverityText ?? ToolValues.Severity(l.Row.SeverityNumber),
                    Body = l.Body,
                    Service = l.Row.ServiceName ?? "unknown",
                    TraceId = l.Row.TraceId,
                    SpanId = l.Row.SpanId
                })).ToArray();
        return new ToolRows("records", logs, snapshot.Logs.Count, ["logs"],
            ContractFields.For<ProjectedLogHit>());
    }
}

internal static class ChangesRecentTool
{
    public static ToolRows Run(TelemetrySnapshot snapshot, ToolWindow window)
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
                var attributes = StoredAttributeJson.Attributes(resource.AttributesJson)
                    .ToDictionary(static a => a.Key, static a => a.Value, StringComparer.Ordinal);
                foreach (var (name, kind) in Keys(attributes.Keys))
                {
                    // Keys are stored as sent, so a sender's deprecated key (deployment.environment) is read where
                    // its successor is asked for. source_key names the key the value was stored under.
                    var key = ToolValues.StoredKey(attributes, name);
                    var value = key is null ? null : ToolValues.Text(attributes[key]);
                    if (key is null || value is null) continue;
                    prior.TryGetValue(name, out var previous);
                    if (value != previous && window.Contains(resource.ObservedUnixNano))
                    {
                        changes.Add(ToolValues.Json(new Change
                        {
                            ObservedAt = ToolDispatcher.Timestamp(resource.ObservedUnixNano),
                            Service = service.Key,
                            Kind = kind,
                            To = value,
                            SourceKey = key,
                            From = previous
                        }));
                    }

                    prior[name] = value;
                }
            }
        }

        var rows = changes.OrderByDescending(static r => r["observed_at"]!.GetValue<string>(), StringComparer.Ordinal)
            .ThenBy(static r => r["service"]!.GetValue<string>(), StringComparer.Ordinal)
            .ThenBy(static r => r["source_key"]!.GetValue<string>(), StringComparer.Ordinal).ToArray();
        return new ToolRows("changes", rows, snapshot.Resources.Count, ["traces", "metrics", "logs"],
            ContractFields.For<ProjectedChange>());
    }

    private static IEnumerable<(string Key, string Kind)> Keys(IEnumerable<string> keys)
    {
        yield return ("vcs.ref.head.revision", "revision");
        yield return ("service.version", "version");
        yield return ("deployment.environment.name", "environment");
        foreach (var key in keys.Where(static k => k.StartsWith("cicd.", StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
            yield return (key, "pipeline");
    }
}

internal static class TracesGetTool
{
    public static ToolRows Run(TelemetrySnapshot snapshot, ToolWindow window, string traceId)
    {
        var spans = snapshot.Spans.Where(s => s.TraceId == traceId && window.Contains(s.StartTimeUnixNano, "traces"))
            .OrderBy(static s => s.StartTimeUnixNano).ThenBy(static s => s.SpanId, StringComparer.Ordinal).ToArray();
        if (spans.Length == 0 || !spans.Any(s => window.Matches(s.ServiceName)))
            throw new ToolNotFoundException("trace_id", traceId);
        var ids = spans.Select(static s => s.SpanId).ToHashSet(StringComparer.Ordinal);
        var root = spans.FirstOrDefault(s => s.ParentSpanId is null || !ids.Contains(s.ParentSpanId)) ?? spans[0];
        var start = spans.Min(static s => s.StartTimeUnixNano);
        var end = spans.Max(static s => s.EndTimeUnixNano);
        var result = ToolValues.Json(new Trace
        {
            TraceId = traceId,
            RootSpan = ToolValues.SpanModel(root),
            SpanCount = spans.Length,
            DurationNs = ToolValues.Elapsed(start, end),
            StartTime = ToolDispatcher.Timestamp(start),
            EndTime = ToolDispatcher.Timestamp(end),
            Services = [.. spans.Select(static span => span.ServiceName ?? "unknown").Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)],
            HasError = spans.Any(static span => span.StatusCode == 2),
            Spans = [.. spans.Select(ToolValues.SpanModel)]
        });
        return new ToolRows("trace", [result], spans.Length, ["traces"],
            ContractFields.For<ProjectedTrace>(), true);
    }
}

internal static class ToolValues
{
    /// <summary>
    ///     The time from a stored start to a stored end, and zero when the end precedes the start: the bounds are
    ///     unsigned, and a database written before a repeated span id replaced the whole row can hold such a span.
    /// </summary>
    internal static ulong Elapsed(ulong startUnixNano, ulong endUnixNano)
    {
        return endUnixNano > startUnixNano ? endUnixNano - startUnixNano : 0;
    }

    internal static JsonObject Json<T>(T value)
    {
        return JsonSerializer.SerializeToNode(value, ContractTypes.TypeInfo<T>())!.AsObject();
    }

    // Derived at read time: telemetry is stored as it was sent, so a tool that needs one name reads the keys a sender
    // may have used for it. The registry's successor table says which deprecated keys mean the same as a key.
    private static readonly Dictionary<string, string[]> s_deprecatedKeysOf = Nami.SemanticConventions.AttributeCatalog.All
        .Where(static definition => definition.Successor is not null &&
                                    !definition.Type.StartsWith("template[", StringComparison.Ordinal))
        .GroupBy(static definition => definition.Successor!, StringComparer.Ordinal)
        .ToDictionary(static group => group.Key,
            static group => group.Select(static definition => definition.Key).Order(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);

    /// <summary>
    ///     The text of the attribute stored under <paramref name="key" />, or, when the record has none, under a
    ///     deprecated key whose successor is <paramref name="key" />.
    /// </summary>
    internal static string? Attribute(string? json, string key)
    {
        if (Text(StoredAttributeJson.Attribute(json, key)) is { } value) return value;
        if (json is null || !s_deprecatedKeysOf.TryGetValue(key, out var deprecated)) return null;
        return deprecated.Where(name => json.Contains(name, StringComparison.Ordinal))
            .Select(name => Text(StoredAttributeJson.Attribute(json, name))).FirstOrDefault(static text => text is not null);
    }

    /// <summary>The key a record stores <paramref name="key" /> under: the key itself or a deprecated key that means it.</summary>
    internal static string? StoredKey(Dictionary<string, AttributeValue?> attributes, string key)
    {
        if (attributes.ContainsKey(key)) return key;
        return s_deprecatedKeysOf.TryGetValue(key, out var deprecated)
            ? deprecated.FirstOrDefault(attributes.ContainsKey)
            : null;
    }

    /// <summary>
    ///     The event name of a log record, derived at read time: the record's event-name field, or, for a sender that
    ///     names its events in the deprecated <c>event.name</c> attribute only, that attribute as it was sent.
    /// </summary>
    internal static string? EventName(LogStorageRow log)
    {
        if (log.EventName is not null) return log.EventName;
        return log.AttributesJson is { } json && json.Contains("\"event.name\"", StringComparison.Ordinal)
            ? Text(StoredAttributeJson.Attribute(json, "event.name"))
            : null;
    }

    internal static string Body(string? json)
    {
        return Text(StoredAttributeJson.Value(json)) ?? "";
    }

    /// <summary>
    ///     A value in a string field of an answer: a string, boolean or number as its plain text (a double as the
    ///     contract's canonical number), bytes, arrays and maps as the contract's canonical JSON.
    /// </summary>
    internal static string? Text(AttributeValue? value)
    {
        return value switch
        {
            null => null,
            AttributeValue.StringValue text => text.Value,
            AttributeValue.BoolValue flag => flag.Value ? "true" : "false",
            AttributeValue.ObjectValue structured => structured.Value switch
            {
                AttributeIntValue number => number.Value.ToString(CultureInfo.InvariantCulture),
                AttributeDoubleValue => CanonicalNumber(structured),
                _ => structured.ToStableString()
            },
            _ => value.ToStableString()
        };
    }

    private static string CanonicalNumber(AttributeValue value)
    {
        using var document = JsonDocument.Parse(value.ToStableString());
        var number = document.RootElement.GetProperty("value");
        return number.ValueKind == JsonValueKind.String ? number.GetString()! : number.GetRawText();
    }

    internal static string Severity(byte severity)
    {
        return severity switch
        {
            >= 21 => "FATAL",
            >= 17 => "ERROR",
            >= 13 => "WARN",
            >= 9 => "INFO",
            >= 5 => "DEBUG",
            >= 1 => "TRACE",
            _ => "UNSPECIFIED"
        };
    }

    private static IReadOnlyList<Attribute> AttributeModels(string? json)
    {
        return [.. StoredAttributeJson.Attributes(json).Select(static attribute => new Attribute
        {
            Key = attribute.Key,
            Value = attribute.Value,
            Stability = AttributeStabilityWire.Name(attribute.Key)
        })];
    }

    internal static Span SpanModel(SpanStorageRow span)
    {
        return ModelMappings.ToModel(span, new SpanApiValues
        {
            Kind = (SpanKind)span.Kind,
            Attributes = AttributeModels(span.AttributesJson),
            Status = new SpanStatus { Code = (SpanStatusCode)span.StatusCode, Message = span.StatusMessage },
            Resource = ModelMappings.ToModel(span, new ResourceApiValues
            {
                ServiceName = span.ServiceName ?? "unknown",
                Attributes = AttributeModels(span.ResourceJson),
                EntityRefs = span.ResourceEntityRefsJson is { } json
                    ? JsonSerializer.Deserialize(json, StorageJsonSerializerContext.Default.StoredResourceEntityRefList)
                        ?.Select(static value => ModelMappings.ToModel(value)).ToArray()
                    : null
            }),
            InstrumentationScope = span.ScopeName is not null || span.ScopeVersion is not null || span.ScopeAttributesJson is not null
                ? ModelMappings.ToModel(span, new ScopeApiValues { ScopeAttributes = AttributeModels(span.ScopeAttributesJson) })
                : null,
            Events = SpanChildStorage.DeserializeEvents(span.EventsJson)?.Select(static value =>
                ModelMappings.ToModel(value, new EventApiValues { Attributes = AttributeModels(value.AttributesJson) })).ToArray(),
            Links = SpanChildStorage.DeserializeLinks(span.LinksJson)?.Select(static value =>
                ModelMappings.ToModel(value, new LinkApiValues { Attributes = AttributeModels(value.AttributesJson) })).ToArray()
        });
    }
}
