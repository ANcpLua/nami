using Google.Protobuf;
using Google.Protobuf.Collections;
using Nami.Collector.Ingestion.Primitives;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using ProtoAnyValue = OpenTelemetry.Proto.Common.V1.AnyValue;
using ProtoLogRecord = OpenTelemetry.Proto.Logs.V1.LogRecord;
using ProtoResource = OpenTelemetry.Proto.Resource.V1.Resource;
using ProtoSeverityNumber = OpenTelemetry.Proto.Logs.V1.SeverityNumber;
using ProtoSpan = OpenTelemetry.Proto.Trace.V1.Span;
using ProtoStatusCode = OpenTelemetry.Proto.Trace.V1.Status.Types.StatusCode;

namespace Nami.Collector.Ingestion;

internal static partial class OtlpConverter
{
    // OTLP accepts a record whose enum field holds an undefined value; nami keeps the record, stores the
    // field's zero value and answers the sender with a partial-success warning that rejects nothing.
    // https://github.com/open-telemetry/opentelemetry-proto/blob/v1.11.1/opentelemetry/proto/collector/logs/v1/logs_service.proto
    private const string SpanKindWarning = "OTLP span kind outside 0-5 is recorded as SPAN_KIND_UNSPECIFIED (0).";
    private const string StatusCodeWarning = "OTLP span status code outside 0-2 is recorded as STATUS_CODE_UNSET (0).";

    private const string SeverityNumberWarning =
        "OTLP log severity_number outside 0-24 is recorded as SEVERITY_NUMBER_UNSPECIFIED (0).";

    private static int DefinedOrZero(int value, int maximum, string warning, ISet<string> warnings)
    {
        if (value >= 0 && value <= maximum)
            return value;

        warnings.Add(warning);
        return 0;
    }

    #region OTLP Trace Conversion

    /// <summary>Converts with the bounds given; without any, with the defaults of <see cref="IngestLimits" />.</summary>
    public static TraceIngestionBatch ConvertTraceRequest(ExportTraceServiceRequest request, IngestLimits? limits) =>
        WithLimits(limits, () => ConvertTraceRequest(request));

    public static TraceIngestionBatch ConvertTraceRequest(ExportTraceServiceRequest request)
    {
        var spans = new List<SpanIngestionRecord>();
        long rejected = 0;
        var reasons = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var resourceSpan in request.ResourceSpans)
        {
            ResourceProjection resource;
            uint resourceDropped;
            try { resource = ExtractResourceProjection(resourceSpan.Resource, out resourceDropped); }
            catch (InvalidDataException error)
            {
                Reject(resourceSpan.ScopeSpans.Sum(static scope => scope.Spans.Count), error);
                continue;
            }

            var serviceName = ExtractServiceNameFromProto(resourceSpan.Resource);
            var project = ExtractProjectIdHintFromProto(resourceSpan.Resource);
            var resourceSchema = KeptOrNull(resourceSpan.SchemaUrl);
            foreach (var scope in resourceSpan.ScopeSpans)
            {
                NormalizedAttributes scopeAttributes;
                uint scopeDropped;
                try
                {
                    scopeAttributes = ConvertAttributes(scope.Scope?.Attributes ?? [], new RecordBudget(), true,
                        out scopeDropped);
                }
                catch (InvalidDataException error)
                {
                    Reject(scope.Spans.Count, error);
                    continue;
                }

                var scopeSchema = KeptOrNull(scope.SchemaUrl);
                var scopeName = KeptOrNull(scope.Scope?.Name);
                var scopeVersion = KeptOrNull(scope.Scope?.Version);
                foreach (var span in scope.Spans)
                {
                    try
                    {
                        // OTLP spans have required bounds, unlike optional log event timestamps.
                        // https://github.com/open-telemetry/opentelemetry-proto/blob/v1.11.1/opentelemetry/proto/trace/v1/trace.proto
                        if (span.EndTimeUnixNano < span.StartTimeUnixNano ||
                            span is { StartTimeUnixNano: 0, EndTimeUnixNano: 0 })
                            throw new InvalidDataException("OTLP span requires start and end times with end >= start.");
                        spans.Add(CreateSpanRecordFromProto(span, project, serviceName,
                                resource.Attributes, resource.EntityRefs,
                                scopeSchema ?? resourceSchema, scopeName, scopeVersion, reasons) with
                        {
                            ScopeSchemaUrl = scopeSchema,
                            ScopeAttributes = scopeAttributes,
                            ScopeDroppedAttributesCount = (scope.Scope?.DroppedAttributesCount ?? 0) + scopeDropped,
                            ResourceSchemaUrl = resourceSchema,
                            ResourceDroppedAttributesCount =
                                (resourceSpan.Resource?.DroppedAttributesCount ?? 0) + resourceDropped
                        });
                    }
                    catch (InvalidDataException error) { Reject(1, error); }
                }
            }
        }

        return new TraceIngestionBatch(spans)
        {
            RejectedSpans = rejected,
            RejectionMessage = reasons.Count is 0 ? null : string.Join("; ", reasons)
        };

        void Reject(int count, InvalidDataException error)
        {
            rejected += count;
            reasons.Add(error.Message);
        }
    }

    private static string ExtractServiceNameFromProto(ProtoResource? resource)
    {
        if (resource is null)
        {
            NamiCollectorMetrics.MissingServiceName();
            return "unknown";
        }

        foreach (var attr in resource.Attributes)
        {
            if (attr is not
                {
                    Key: CollectorSemanticAttributeCatalog.ServiceName,
                    Value.ValueCase: ProtoAnyValue.ValueOneofCase.StringValue
                })
                continue;
            if (!string.IsNullOrWhiteSpace(attr.Value.StringValue)) return Kept(attr.Value.StringValue);
            break;
        }

        NamiCollectorMetrics.MissingServiceName();
        return "unknown";
    }

    private static string? ExtractProjectIdHintFromProto(ProtoResource? resource)
    {
        if (resource is null)
            return null;

        // Only the generated project resource key determines the partition.
        foreach (var key in AttributeKeySets.ProjectIdResourceKeyPrecedence)
        {
            foreach (var attr in resource.Attributes)
            {
                if (!string.Equals(attr.Key, key, StringComparison.Ordinal) ||
                    attr.Value.ValueCase is not ProtoAnyValue.ValueOneofCase.StringValue ||
                    string.IsNullOrWhiteSpace(attr.Value.StringValue))
                {
                    continue;
                }

                return Kept(attr.Value.StringValue);
            }
        }

        return null;
    }

    private static ResourceProjection ExtractResourceProjection(ProtoResource? resource, out uint dropped)
    {
        dropped = 0;
        if (resource is null) return new ResourceProjection(new NormalizedAttributes(), []);

        // A resource is exempt from the count of attributes, not from the bytes: it is copied into every row.
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        var attrs = ConvertAttributes(resource.Attributes, new RecordBudget(), false, out dropped, keys: keys);
        var projectedKeys = keys.ToDictionary(static pair => pair.Key,
            static pair => new ResourceAttributeProjectionKey(pair.Value), StringComparer.Ordinal);
        return new ResourceProjection(attrs, ExtractResourceEntityRefs(resource, projectedKeys, attrs));
    }

    private static IReadOnlyList<ResourceEntityRefIngestionRecord> ExtractResourceEntityRefs(
        ProtoResource resource,
        IReadOnlyDictionary<string, ResourceAttributeProjectionKey> projectedKeys,
        NormalizedAttributes projectedAttributes)
    {
        if (resource.EntityRefs.Count is 0) return [];

        var entityRefs = new List<(string Identity, ResourceEntityRefIngestionRecord Value)>(
            resource.EntityRefs.Count);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entityRef in resource.EntityRefs)
        {
            if (string.IsNullOrWhiteSpace(entityRef.Type))
                throw new InvalidDataException("OTLP resource entity type must not be empty.");
            if (entityRef.IdKeys.Count is 0)
                throw new InvalidDataException("OTLP resource entity must contain at least one id_key.");

            var idKeys = NormalizeEntityReferenceKeys(entityRef.IdKeys, projectedKeys, "id_key");
            var descriptionKeys = NormalizeEntityReferenceKeys(
                entityRef.DescriptionKeys,
                projectedKeys,
                "description_key");
            var value = new ResourceEntityRefIngestionRecord(
                KeptOrNull(entityRef.SchemaUrl),
                Kept(entityRef.Type),
                idKeys,
                descriptionKeys);
            var identity = GetEntityReferenceIdentity(value, projectedAttributes);
            if (!identities.Add(identity))
                throw new InvalidDataException("OTLP resource contains a duplicate entity reference.");
            entityRefs.Add((identity, value));
        }

        entityRefs.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Identity, right.Identity));
        return [.. entityRefs.Select(static item => item.Value)];
    }

    private static IReadOnlyList<string> NormalizeEntityReferenceKeys(
        RepeatedField<string> keys,
        IReadOnlyDictionary<string, ResourceAttributeProjectionKey> projectedKeys,
        string fieldName)
    {
        var normalized = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidDataException($"OTLP resource entity {fieldName} must not be empty.");
            if (!projectedKeys.TryGetValue(key, out var projection))
            {
                throw new InvalidDataException(
                    $"OTLP resource entity {fieldName} '{key}' does not reference a persisted resource attribute.");
            }

            var projectedKey = projection.PersistedKey;
            if (!normalized.Add(projectedKey))
            {
                throw new InvalidDataException(
                    $"OTLP resource entity contains duplicate {fieldName} '{projectedKey}'.");
            }
        }

        return [.. normalized.Order(StringComparer.Ordinal)];
    }

    private static string GetEntityReferenceIdentity(
        ResourceEntityRefIngestionRecord entityRef,
        NormalizedAttributes resourceAttributes)
    {
        var builder = new StringBuilder();
        builder.AppendSegment(entityRef.Type);
        builder.Append(entityRef.IdKeys.Count.ToString(CultureInfo.InvariantCulture)).Append(':');
        foreach (var key in entityRef.IdKeys)
        {
            builder.AppendSegment(key);
            builder.AppendSegment(resourceAttributes[key].ToStableString());
        }

        return builder.ToString();
    }



    private readonly record struct ResourceAttributeProjectionKey(string PersistedKey);

    private static List<SpanEventIngest> BuildSpanEvents(ProtoSpan span, RecordBudget budget)
    {
        if (span.Events.Count is 0) return [];

        var events = new List<SpanEventIngest>(span.Events.Count);
        foreach (var e in span.Events)
        {
            var attributes = ConvertAttributes(e.Attributes, budget, true, out var dropped);
            events.Add(new SpanEventIngest(Kept(e.Name ?? ""), e.TimeUnixNano, attributes,
                e.DroppedAttributesCount + dropped));
        }

        return events;
    }

    private static List<SpanLinkIngest> BuildSpanLinks(ProtoSpan span, RecordBudget budget)
    {
        if (span.Links.Count is 0) return [];

        var links = new List<SpanLinkIngest>(span.Links.Count);
        foreach (var l in span.Links)
        {
            var attributes = ConvertAttributes(l.Attributes, budget, true, out var dropped);
            links.Add(new SpanLinkIngest(
                RequireId(l.TraceId, 16, "link.trace_id"),
                RequireId(l.SpanId, 8, "link.span_id"),
                attributes, KeptOrNull(l.TraceState), l.Flags,
                l.DroppedAttributesCount + dropped));
        }

        return links;
    }

    // One span is one record: its attributes, its events and its links share the record's 1 MiB of attributes.
    private static SpanIngestionRecord CreateSpanRecordFromProto(
        ProtoSpan span,
        string? projectIdHint,
        string serviceName,
        NormalizedAttributes resourceAttributes,
        IReadOnlyList<ResourceEntityRefIngestionRecord> resourceEntityRefs,
        string? schemaUrl,
        string? scopeName,
        string? scopeVersion,
        ISet<string> warnings)
    {
        var budget = new RecordBudget();
        // The one attribute ingest adds: the resource's service name, on every span.
        budget.Store(StoredString(CollectorSemanticAttributeCatalog.ServiceName) + 2 + StoredString(serviceName));
        var attributes = new NormalizedAttributes
        {
            [CollectorSemanticAttributeCatalog.ServiceName] = OtlpAttributeValue.FromString(serviceName)
        };
        ConvertAttributes(span.Attributes, budget, true, out var dropped, attributes);
        return new SpanIngestionRecord
        {
            ProjectIdHint = projectIdHint,
            SpanId = RequireId(span.SpanId, 8, "span_id"),
            TraceId = RequireId(span.TraceId, 16, "trace_id"),
            ParentSpanId = RequireIdOrAbsent(span.ParentSpanId, 8, "parent_span_id"),
            TraceState = KeptOrNull(span.TraceState),
            Flags = span.Flags,
            DroppedAttributesCount = span.DroppedAttributesCount + dropped,
            DroppedEventsCount = span.DroppedEventsCount,
            DroppedLinksCount = span.DroppedLinksCount,
            Name = Kept(span.Name ?? "unknown"),
            Kind = DefinedOrZero((int)span.Kind, (int)ProtoSpan.Types.SpanKind.Consumer, SpanKindWarning, warnings),
            StartTimeUnixNano = span.StartTimeUnixNano,
            EndTimeUnixNano = span.EndTimeUnixNano,
            StatusCode = span.Status is not null
                ? DefinedOrZero((int)span.Status.Code, (int)ProtoStatusCode.Error, StatusCodeWarning, warnings)
                : null,
            StatusMessage = StatusMessageOf(span.Status),
            ServiceName = serviceName,
            Attributes = attributes,
            ResourceAttributes = resourceAttributes,
            ResourceEntityRefs = resourceEntityRefs,
            SchemaUrl = schemaUrl,
            ScopeName = scopeName,
            ScopeVersion = scopeVersion,
            Events = BuildSpanEvents(span, budget),
            Links = BuildSpanLinks(span, budget)
        };
    }

    #endregion

    #region Shared Helpers

    // OTLP requires nonzero trace/span IDs with exact byte lengths; malformed records are
    // rejected through partial_success rather than storing unjoinable identifiers.
    // https://github.com/open-telemetry/opentelemetry-proto/blob/v1.11.1/opentelemetry/proto/trace/v1/trace.proto
    private static string RequireId(ByteString value, int requiredBytes, string field)
    {
        if (value.Length != requiredBytes || value.Span.IndexOfAnyExcept((byte)0) < 0)
        {
            throw new InvalidDataException(
                $"OTLP field '{field}' must be a nonzero {requiredBytes}-byte identifier; got {value.Length}.");
        }

        return Convert.ToHexStringLower(value.Span);
    }

    private static string? RequireIdOrAbsent(ByteString value, int requiredBytes, string field) =>
        value.Length is 0 ? null : RequireId(value, requiredBytes, field);

    private static byte[] CopyBytes(ByteString value)
    {
        var bytes = new byte[value.Length];
        value.CopyTo(bytes, 0);
        return bytes;
    }

    #endregion

    #region Logs Conversion

    /// <inheritdoc cref="ConvertTraceRequest(ExportTraceServiceRequest, IngestLimits?)" />
    public static LogIngestionBatch ConvertLogs(ExportLogsServiceRequest otlp, TimeProvider timeProvider,
        IngestLimits? limits) => WithLimits(limits, () => ConvertLogs(otlp, timeProvider));

    public static LogIngestionBatch ConvertLogs(ExportLogsServiceRequest otlp, TimeProvider timeProvider)
    {
        var logs = new List<LogIngestionRecord>();
        long rejected = 0;
        var ordinal = 0;
        var reasons = new SortedSet<string>(StringComparer.Ordinal);

        // Observation time belongs to the injected receive clock only when the producer omitted it.
        // https://github.com/open-telemetry/opentelemetry-specification/blob/v1.61.0/specification/logs/data-model.md#field-observedtimestamp
        var observedTime = TimeConversions.ToUnixNanoUnsigned(timeProvider.GetUtcNow());
        foreach (var resourceLogs in otlp.ResourceLogs)
        {
            ResourceProjection resource;
            uint resourceDropped;
            try { resource = ExtractResourceProjection(resourceLogs.Resource, out resourceDropped); }
            catch (InvalidDataException error)
            {
                var count = resourceLogs.ScopeLogs.Sum(static scope => scope.LogRecords.Count);
                Reject(count, error);
                ordinal += count;
                continue;
            }

            var serviceName = ExtractServiceNameFromProto(resourceLogs.Resource);
            var project = ExtractProjectIdHintFromProto(resourceLogs.Resource);
            var resourceSchema = KeptOrNull(resourceLogs.SchemaUrl);
            foreach (var scope in resourceLogs.ScopeLogs)
            {
                NormalizedAttributes scopeAttributes;
                uint scopeDropped;
                try
                {
                    scopeAttributes = ConvertAttributes(scope.Scope?.Attributes ?? [], new RecordBudget(), true,
                        out scopeDropped);
                }
                catch (InvalidDataException error)
                {
                    Reject(scope.LogRecords.Count, error);
                    ordinal += scope.LogRecords.Count;
                    continue;
                }

                var scopeName = KeptOrNull(scope.Scope?.Name);
                var scopeVersion = KeptOrNull(scope.Scope?.Version);
                var scopeSchema = KeptOrNull(scope.SchemaUrl);
                foreach (var log in scope.LogRecords)
                {
                    var requestOrdinal = ordinal++;
                    try
                    {
                        logs.Add(
                            CreateLogRecord(log, project, serviceName, resource.Attributes, resource.EntityRefs,
                                    observedTime, reasons) with
                            {
                                UntimedRequestOrdinal =
                                    log is { TimeUnixNano: 0, ObservedTimeUnixNano: 0 } ? requestOrdinal : null,
                                ScopeName = scopeName,
                                ScopeVersion = scopeVersion,
                                ScopeSchemaUrl = scopeSchema,
                                ScopeAttributes = scopeAttributes,
                                ScopeDroppedAttributesCount = (scope.Scope?.DroppedAttributesCount ?? 0) + scopeDropped,
                                ResourceSchemaUrl = resourceSchema,
                                ResourceDroppedAttributesCount =
                                    (resourceLogs.Resource?.DroppedAttributesCount ?? 0) + resourceDropped
                            });
                    }
                    catch (InvalidDataException error) { Reject(1, error); }
                }
            }
        }

        return new LogIngestionBatch(logs)
        {
            RejectedLogRecords = rejected,
            RejectionMessage = reasons.Count is 0 ? null : string.Join("; ", reasons)
        };

        void Reject(int count, InvalidDataException error)
        {
            rejected += count;
            reasons.Add(error.Message);
        }
    }

    private static LogIngestionRecord CreateLogRecord(
        ProtoLogRecord log,
        string? projectIdHint,
        string serviceName,
        NormalizedAttributes resourceAttributes,
        IReadOnlyList<ResourceEntityRefIngestionRecord> resourceEntityRefs,
        ulong observedTime,
        ISet<string> warnings)
    {
        // One log record is one record: its body and its attributes share the record's 1 MiB. The body is read
        // first and bounded like any value: a string is cut to the cap, a list or map within the cap is kept as
        // sent, with its structure. A body over the cap as a structure, or one that does not fit the record, is
        // dropped whole: it is stored as an empty value and counted in the record's dropped_attributes_count, the
        // only count a log record has.
        var budget = new RecordBudget();
        var body = ConvertValue(log.Body, null, budget, 0);
        var bodyDropped = 0U;
        if (budget.Exhausted)
        {
            (body, budget.Remaining) = (OtlpAttributeValue.Empty, Limits.MaxRecordAttributeBytes);
            NamiCollectorMetrics.AttributeDropped();
            bodyDropped = 1;
        }

        budget.Value = Limits.MaxValueUtf8Bytes;
        var attributes = ConvertAttributes(log.Attributes, budget, true, out var dropped);
        dropped += bodyDropped;

        var severityNumber = DefinedOrZero((int)log.SeverityNumber, (int)ProtoSeverityNumber.Fatal4,
            SeverityNumberWarning, warnings);

        return new LogIngestionRecord
        {
            ProjectIdHint = projectIdHint,
            TraceId = RequireIdOrAbsent(log.TraceId, 16, "trace_id"),
            SpanId = RequireIdOrAbsent(log.SpanId, 8, "span_id"),
            // The event-name field, the body and the event.name attribute are stored as they arrive. A sender that
            // names its events only in the attribute (Claude Code 2.1.325) leaves this field empty; a reader that
            // needs one name derives it from both when it reads.
            EventName = KeptOrNull(log.EventName) ?? "derived",
            Flags = log.Flags,
            DroppedAttributesCount = log.DroppedAttributesCount + dropped,
            TimeOrigin =
                new LogTimeOrigin
                {
                    ProducerEventTime = log.TimeUnixNano is 0 ? null : log.TimeUnixNano,
                    ProducerObservedTime = log.ObservedTimeUnixNano is 0 ? null : log.ObservedTimeUnixNano,
                    CollectorReceivedTime = observedTime
                },
            SeverityNumber = severityNumber,
            SeverityText = Kept(log.SeverityText),
            Body = body,
            ServiceName = serviceName,
            Attributes = attributes,
            ResourceAttributes = resourceAttributes,
            ResourceEntityRefs = resourceEntityRefs
        };
    }

    #endregion
}
