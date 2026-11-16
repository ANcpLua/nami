using Google.Protobuf;
using Google.Protobuf.Collections;
using Nami.Collector.Ingestion.Primitives;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using ProtoAnyValue = OpenTelemetry.Proto.Common.V1.AnyValue;
using ProtoArrayValue = OpenTelemetry.Proto.Common.V1.ArrayValue;
using ProtoKeyValue = OpenTelemetry.Proto.Common.V1.KeyValue;
using ProtoKeyValueList = OpenTelemetry.Proto.Common.V1.KeyValueList;
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

    // The converter reads a sender's text through these two and through ConvertProtoAnyValue only, and each passes
    // the text through the credential stage before any bound (CredentialRedaction): attribute keys, span, event and
    // metric names, status messages, severity text, scope and schema text, trace state.
    private static string Kept(string value) => CredentialRedaction.Redact(value);

    private static string? KeptOrNull(string? value) =>
        string.IsNullOrEmpty(value) ? null : CredentialRedaction.Redact(value);

    #region OTLP Trace Conversion

    public static TraceIngestionBatch ConvertTraceRequest(ExportTraceServiceRequest request)
    {
        var spans = new List<SpanIngestionRecord>();
        long rejected = 0;
        var reasons = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var resourceSpan in request.ResourceSpans)
        {
            ResourceProjection resource;
            try { resource = ExtractResourceProjection(resourceSpan.Resource); }
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
                try { scopeAttributes = ConvertMetadataAttributes(scope.Scope?.Attributes ?? []); }
                catch (InvalidDataException error)
                {
                    Reject(scope.Spans.Count, error);
                    continue;
                }

                var producer = ProducerOfScope(scope.Scope?.Name);
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
                                ExtractAttributesFromProto(span.Attributes, serviceName, producer),
                                resource.Attributes, resource.EntityRefs,
                                scopeSchema ?? resourceSchema, scopeName, scopeVersion, producer, reasons) with
                        {
                            ScopeSchemaUrl = scopeSchema,
                            ScopeAttributes = scopeAttributes,
                            ScopeDroppedAttributesCount = scope.Scope?.DroppedAttributesCount ?? 0,
                            ResourceSchemaUrl = resourceSchema,
                            ResourceDroppedAttributesCount = resourceSpan.Resource?.DroppedAttributesCount ?? 0
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

    private static ResourceProjection ExtractResourceProjection(ProtoResource? resource)
    {
        var attrs = new NormalizedAttributes();
        if (resource is null) return new ResourceProjection(attrs, []);

        ValidateAttributeKeys(resource.Attributes);
        var projectedKeys = new Dictionary<string, ResourceAttributeProjectionKey>(StringComparer.Ordinal);

        foreach (var attr in resource.Attributes)
        {
            if (IsUnresolvedProfilingKey(attr)) continue;
            // Stored under the key it was sent under: ingest renames nothing and drops no key. The key is text the
            // sender chose, so it passes the credential stage like a value.
            var key = Kept(attr.Key);
            attrs[key] = ConvertAttribute(key, attr.Value, null);
            projectedKeys[attr.Key] = new ResourceAttributeProjectionKey(key);
        }

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

    private static NormalizedAttributes ExtractAttributesFromProto(
        RepeatedField<ProtoKeyValue> protoAttributes,
        string serviceName,
        string? producer)
    {
        var attributes = new NormalizedAttributes
        {
            [CollectorSemanticAttributeCatalog.ServiceName] = OtlpAttributeValue.FromString(serviceName)
        };

        CaptureRecordAttributes(attributes, protoAttributes, producer);
        return attributes;
    }

    // The attributes of one record (span, span event, span link, log record, metric point, exemplar) under the
    // instrumentation scope of producer, or of no producer.
    private static void CaptureRecordAttributes(
        NormalizedAttributes attributes,
        RepeatedField<ProtoKeyValue> protoAttributes,
        string? producer)
    {
        ValidateAttributeKeys(protoAttributes);
        // A container such as Claude Code's tool_parameters is stored as the JSON string it is; ingest lifts nothing
        // out of it.
        foreach (var attr in protoAttributes)
        {
            if (string.IsNullOrEmpty(attr.Key)) continue;
            var key = Kept(attr.Key);
            attributes[key] = ConvertAttribute(key, attr.Value, producer);
        }
    }

    private static NormalizedAttributes ConvertRecordAttributes(
        RepeatedField<ProtoKeyValue> protoAttributes,
        string? producer)
    {
        var attributes = new NormalizedAttributes();
        CaptureRecordAttributes(attributes, protoAttributes, producer);
        return attributes;
    }

    private static void ValidateAttributeKeys(RepeatedField<ProtoKeyValue> attributes)
    {
        var values = new Dictionary<string, ProtoAnyValue?>(StringComparer.Ordinal);
        foreach (var attribute in attributes)
        {
            if (IsUnresolvedProfilingKey(attribute)) continue;
            if (string.IsNullOrWhiteSpace(attribute.Key))
                throw new InvalidDataException("OTLP attributes require nonempty, unique keys.");
            if (values.TryAdd(attribute.Key, attribute.Value)) continue;

            // The SDK exporter flattens ILogger state and scopes, which can repeat the same
            // typed value (for example TraceId). Coalesce identical entries, never distinct values.
            // https://github.com/open-telemetry/opentelemetry-specification/blob/v1.61.0/specification/common/README.md#mapstring-anyvalue
            if (!Equals(values[attribute.Key], attribute.Value))
                throw new InvalidDataException("OTLP attributes require unique keys; duplicate values conflict.");
            NamiCollectorMetrics.AttributeCoalesced(attribute.Key);
        }
    }

    // A profiling string-table key has no meaning in these signals. OTLP requires its
    // presence to be non-fatal; do not mistake it for a malformed ordinary empty key.
    // https://github.com/open-telemetry/opentelemetry-proto/blob/v1.11.1/opentelemetry/proto/common/v1/common.proto
    private static bool IsUnresolvedProfilingKey(ProtoKeyValue attribute) =>
        attribute.Key.Length is 0 && attribute.KeyStrindex is not 0;

    private static NormalizedAttributes ConvertMetadataAttributes(
        RepeatedField<ProtoKeyValue> attributes)
    {
        ValidateAttributeKeys(attributes);
        var result = new NormalizedAttributes();
        foreach (var attribute in attributes)
        {
            if (IsUnresolvedProfilingKey(attribute)) continue;
            var key = Kept(attribute.Key);
            result[key] = ConvertAttribute(key, attribute.Value, null);
        }

        return result;
    }

    private static List<SpanEventIngest> BuildSpanEvents(ProtoSpan span, string? producer)
    {
        if (span.Events.Count is 0) return [];

        return span.Events.Select(e => new SpanEventIngest(Kept(e.Name ?? ""), e.TimeUnixNano,
            ConvertRecordAttributes(e.Attributes, producer), e.DroppedAttributesCount)).ToList();
    }

    private static List<SpanLinkIngest> BuildSpanLinks(ProtoSpan span, string? producer)
    {
        if (span.Links.Count is 0) return [];

        return span.Links.Select(l => new SpanLinkIngest(
                RequireId(l.TraceId, 16, "link.trace_id"),
                RequireId(l.SpanId, 8, "link.span_id"),
                ConvertRecordAttributes(l.Attributes, producer), KeptOrNull(l.TraceState), l.Flags,
                l.DroppedAttributesCount)).ToList();
    }

    // OTLP sets no bound on how deeply arrays and key-value lists nest. Protobuf's recursion limit admits 31 key-value
    // lists in a span attribute, more than the stored JSON and the tools' results carry: at 29 traces_get could not
    // render the trace, at 31 the store refused the whole export with a retryable 503. Past this bound the value
    // refuses its own record through partial success, and the export's other records are kept.
    private const int MaxValueNesting = 16;

    private static OtlpAttributeValue ConvertProtoAnyValue(ProtoAnyValue? value, string? attributeKey = null,
        string? producer = null, int nesting = 0)
    {
        // All keyed carriers, including nested maps and metric metadata, the items of arrays and log bodies converge
        // here, and every string among them passes the credential stage before original data is journaled. No key
        // is exempt. Exception text passes it inside its own limits (PRD R6); a value of another type under such a
        // key is no text and stays as it was sent.
        if (attributeKey is not null && value?.ValueCase is ProtoAnyValue.ValueOneofCase.StringValue
            && CollectorSemanticAttributeCatalog.ExceptionTextKeys.TryGetValue(attributeKey, out var canonicalKey))
            return OtlpAttributeValue.FromString(CredentialRedaction.ExceptionText(canonicalKey, value));
        if (value is null) return OtlpAttributeValue.Empty;

        return value.ValueCase switch
        {
            ProtoAnyValue.ValueOneofCase.StringValue =>
                OtlpAttributeValue.FromString(CredentialRedaction.RedactValue(attributeKey, value.StringValue)),
            ProtoAnyValue.ValueOneofCase.IntValue => OtlpAttributeValue.FromInt(value.IntValue),
            ProtoAnyValue.ValueOneofCase.DoubleValue => OtlpAttributeValue.FromDouble(value.DoubleValue),
            ProtoAnyValue.ValueOneofCase.BoolValue => OtlpAttributeValue.FromBool(value.BoolValue),
            ProtoAnyValue.ValueOneofCase.BytesValue => OtlpAttributeValue.FromBytes(CopyBytes(value.BytesValue)),
            ProtoAnyValue.ValueOneofCase.ArrayValue => ConvertProtoArrayValue(value.ArrayValue, producer, nesting + 1),
            ProtoAnyValue.ValueOneofCase.KvlistValue =>
                ConvertProtoKeyValueList(value.KvlistValue, producer, nesting + 1),
            ProtoAnyValue.ValueOneofCase.None => OtlpAttributeValue.Empty,
            // Alpha, profiles-only (opentelemetry-proto 1.10+): an index into a string table no other
            // signal carries. The proto asks non-profiling receivers to treat it as absent, which is what
            // it was while the field was unknown.
            ProtoAnyValue.ValueOneofCase.StringValueStrindex => OtlpAttributeValue.Empty,
            _ => throw new InvalidDataException("OTLP AnyValue contains an unknown value kind.")
        };
    }

    private static OtlpAttributeValue ConvertProtoArrayValue(ProtoArrayValue value, string? producer, int nesting)
    {
        RequireNesting(nesting);
        return OtlpAttributeValue.FromArray(
            value.Values.Select(item => ConvertProtoAnyValue(item, null, producer, nesting)).ToList());
    }

    private static OtlpAttributeValue ConvertProtoKeyValueList(ProtoKeyValueList value, string? producer, int nesting)
    {
        RequireNesting(nesting);
        ValidateAttributeKeys(value.Values);
        var items = new NormalizedAttributes();
        foreach (var item in value.Values)
        {
            if (IsUnresolvedProfilingKey(item)) continue;
            var key = Kept(item.Key);
            items[key] = ConvertAttribute(key, item.Value, producer, nesting);
        }

        return OtlpAttributeValue.FromKeyValueList(items);
    }

    private static void RequireNesting(int nesting)
    {
        if (nesting > MaxValueNesting)
        {
            throw new InvalidDataException(
                $"OTLP attribute values nest arrays and key-value lists at most {MaxValueNesting} deep.");
        }
    }

    private static SpanIngestionRecord CreateSpanRecordFromProto(
        ProtoSpan span,
        string? projectIdHint,
        string serviceName,
        NormalizedAttributes attributes,
        NormalizedAttributes resourceAttributes,
        IReadOnlyList<ResourceEntityRefIngestionRecord> resourceEntityRefs,
        string? schemaUrl,
        string? scopeName,
        string? scopeVersion,
        string? producer,
        ISet<string> warnings) =>
        new()
        {
            ProjectIdHint = projectIdHint,
            SpanId = RequireId(span.SpanId, 8, "span_id"),
            TraceId = RequireId(span.TraceId, 16, "trace_id"),
            ParentSpanId = RequireIdOrAbsent(span.ParentSpanId, 8, "parent_span_id"),
            TraceState = KeptOrNull(span.TraceState),
            Flags = span.Flags,
            DroppedAttributesCount = span.DroppedAttributesCount,
            DroppedEventsCount = span.DroppedEventsCount,
            DroppedLinksCount = span.DroppedLinksCount,
            Name = Kept(span.Name ?? "unknown"),
            Kind = DefinedOrZero((int)span.Kind, (int)ProtoSpan.Types.SpanKind.Consumer, SpanKindWarning, warnings),
            StartTimeUnixNano = span.StartTimeUnixNano,
            EndTimeUnixNano = span.EndTimeUnixNano,
            StatusCode = span.Status is not null
                ? DefinedOrZero((int)span.Status.Code, (int)ProtoStatusCode.Error, StatusCodeWarning, warnings)
                : null,
            StatusMessage = StatusMessageOf(span.Status, producer),
            ServiceName = serviceName,
            Attributes = attributes,
            ResourceAttributes = resourceAttributes,
            ResourceEntityRefs = resourceEntityRefs,
            SchemaUrl = schemaUrl,
            ScopeName = scopeName,
            ScopeVersion = scopeVersion,
            Events = BuildSpanEvents(span, producer),
            Links = BuildSpanLinks(span, producer)
        };

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
            try { resource = ExtractResourceProjection(resourceLogs.Resource); }
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
                try { scopeAttributes = ConvertMetadataAttributes(scope.Scope?.Attributes ?? []); }
                catch (InvalidDataException error)
                {
                    Reject(scope.LogRecords.Count, error);
                    ordinal += scope.LogRecords.Count;
                    continue;
                }

                var producer = ProducerOfScope(scope.Scope?.Name);
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
                                    observedTime, producer, reasons) with
                            {
                                UntimedRequestOrdinal =
                                    log is { TimeUnixNano: 0, ObservedTimeUnixNano: 0 } ? requestOrdinal : null,
                                ScopeName = scopeName,
                                ScopeVersion = scopeVersion,
                                ScopeSchemaUrl = scopeSchema,
                                ScopeAttributes = scopeAttributes,
                                ScopeDroppedAttributesCount = scope.Scope?.DroppedAttributesCount ?? 0,
                                ResourceSchemaUrl = resourceSchema,
                                ResourceDroppedAttributesCount = resourceLogs.Resource?.DroppedAttributesCount ?? 0
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
        string? producer,
        ISet<string> warnings)
    {
        var attributes = ConvertRecordAttributes(log.Attributes, producer);

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
            EventName = KeptOrNull(log.EventName),
            Flags = log.Flags,
            DroppedAttributesCount = log.DroppedAttributesCount,
            TimeOrigin =
                new LogTimeOrigin
                {
                    ProducerEventTime = log.TimeUnixNano is 0 ? null : log.TimeUnixNano,
                    ProducerObservedTime = log.ObservedTimeUnixNano is 0 ? null : log.ObservedTimeUnixNano,
                    CollectorReceivedTime = observedTime
                },
            SeverityNumber = severityNumber,
            SeverityText = Kept(log.SeverityText),
            Body = ConvertProtoAnyValue(log.Body),
            ServiceName = serviceName,
            Attributes = attributes,
            ResourceAttributes = resourceAttributes,
            ResourceEntityRefs = resourceEntityRefs
        };
    }

    #endregion
}
