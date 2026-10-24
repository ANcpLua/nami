using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

    private static void SetNormalizedAttribute(
        Dictionary<string, OtlpAttributeValue> attributes,
        string key,
        OtlpAttributeValue value,
        bool renamed)
    {
        if (renamed) attributes.TryAdd(key, value);
        else attributes[key] = value;
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;

    #region OTLP Trace Conversion

    public static TraceIngestionBatch ConvertTraceRequest(ExportTraceServiceRequest request)
    {
        var spans = new List<SpanIngestionRecord>();
        long rejected = 0;
        var reasons = new SortedSet<string>(StringComparer.Ordinal);

        void Reject(int count, InvalidDataException error)
        {
            rejected += count;
            reasons.Add(error.Message);
        }

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
            var resourceSchema = NullIfEmpty(resourceSpan.SchemaUrl);
            foreach (var scope in resourceSpan.ScopeSpans)
            {
                Dictionary<string, OtlpAttributeValue> scopeAttributes;
                try { scopeAttributes = ConvertMetadataAttributes(scope.Scope?.Attributes ?? []); }
                catch (InvalidDataException error)
                {
                    Reject(scope.Spans.Count, error);
                    continue;
                }

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
                                ExtractAttributesFromProto(span.Attributes, serviceName), resource.Attributes,
                                resource.EntityRefs,
                                NullIfEmpty(scope.SchemaUrl) ?? resourceSchema, NullIfEmpty(scope.Scope?.Name),
                                NullIfEmpty(scope.Scope?.Version), reasons) with
                            {
                                ScopeSchemaUrl = NullIfEmpty(scope.SchemaUrl),
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
            RejectedSpans = rejected, RejectionMessage = reasons.Count == 0 ? null : string.Join("; ", reasons)
        };
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
            if (attr is
                {
                    Key: CollectorSemanticAttributeCatalog.ServiceName,
                    Value.ValueCase: ProtoAnyValue.ValueOneofCase.StringValue
                })
            {
                if (!string.IsNullOrWhiteSpace(attr.Value.StringValue)) return attr.Value.StringValue;
                break;
            }
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

                return attr.Value.StringValue;
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
            var renamed = DeprecatedAttributeNormalizer.TryNormalize(attr.Key, out var key);
            NormalizedAttributes.RecordRename(attrs, attr.Key, key);
            if (!AttributeKeySets.IsPersistable(key))
            {
                NamiCollectorMetrics.AttributeDropped(key);
                continue;
            }

            var value = ConvertProtoAnyValue(attr.Value, key);
            SetNormalizedAttribute(attrs, key, value, renamed);

            projectedKeys[attr.Key] = new ResourceAttributeProjectionKey(key);
        }

        return new ResourceProjection(attrs, ExtractResourceEntityRefs(resource, projectedKeys, attrs));
    }

    private static IReadOnlyList<ResourceEntityRefIngestionRecord> ExtractResourceEntityRefs(
        ProtoResource resource,
        IReadOnlyDictionary<string, ResourceAttributeProjectionKey> projectedKeys,
        IReadOnlyDictionary<string, OtlpAttributeValue> projectedAttributes)
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
                NullIfEmpty(entityRef.SchemaUrl),
                entityRef.Type,
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
        IReadOnlyDictionary<string, OtlpAttributeValue> resourceAttributes)
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

    private sealed record ResourceProjection(
        Dictionary<string, OtlpAttributeValue> Attributes,
        IReadOnlyList<ResourceEntityRefIngestionRecord> EntityRefs);

    private readonly record struct ResourceAttributeProjectionKey(string PersistedKey);

    private static Dictionary<string, OtlpAttributeValue> ExtractAttributesFromProto(
        RepeatedField<ProtoKeyValue> protoAttributes,
        string serviceName)
    {
        var attributes = new NormalizedAttributes
        {
            [CollectorSemanticAttributeCatalog.ServiceName] = OtlpAttributeValue.FromString(serviceName)
        };

        CaptureSpanAttributes(attributes, protoAttributes);
        return attributes;
    }

    private static void CaptureSpanAttributes(
        Dictionary<string, OtlpAttributeValue> attributes,
        RepeatedField<ProtoKeyValue> protoAttributes)
    {
        ValidateAttributeKeys(protoAttributes);
        foreach (var attr in protoAttributes)
        {
            if (string.IsNullOrEmpty(attr.Key)) continue;
            var renamed = DeprecatedAttributeNormalizer.TryNormalize(attr.Key, out var key);
            NormalizedAttributes.RecordRename(attributes, attr.Key, key);
            if (!AttributeKeySets.ShouldCaptureSpanAttribute(key))
            {
                NamiCollectorMetrics.AttributeDropped(key);
                continue;
            }

            var value = ConvertProtoAnyValue(attr.Value, key);
            SetNormalizedAttribute(attributes, key, value, renamed);
        }
    }

    private static IReadOnlyDictionary<string, OtlpAttributeValue> ConvertSpanChildAttributes(
        RepeatedField<ProtoKeyValue> protoAttributes)
    {
        var attributes = new NormalizedAttributes();
        CaptureSpanAttributes(attributes, protoAttributes);
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
        attribute.Key.Length == 0 && attribute.KeyStrindex != 0;

    private static Dictionary<string, OtlpAttributeValue> ConvertMetadataAttributes(
        RepeatedField<ProtoKeyValue> attributes)
    {
        ValidateAttributeKeys(attributes);
        var result = new NormalizedAttributes();
        foreach (var attribute in attributes)
        {
            if (IsUnresolvedProfilingKey(attribute)) continue;
            var renamed = DeprecatedAttributeNormalizer.TryNormalize(attribute.Key, out var key);
            NormalizedAttributes.RecordRename(result, attribute.Key, key);
            if (!AttributeKeySets.IsPersistable(key))
            {
                NamiCollectorMetrics.AttributeDropped(key);
                continue;
            }

            SetNormalizedAttribute(result, key, ConvertProtoAnyValue(attribute.Value, key), renamed);
        }

        return result;
    }

    private static IReadOnlyList<SpanEventIngest> BuildSpanEvents(ProtoSpan span)
    {
        if (span.Events.Count is 0) return [];

        var events = new List<SpanEventIngest>(span.Events.Count);
        foreach (var e in span.Events)
            events.Add(new SpanEventIngest(e.Name ?? "", e.TimeUnixNano, ConvertSpanChildAttributes(e.Attributes),
                e.DroppedAttributesCount));

        return events;
    }

    private static IReadOnlyList<SpanLinkIngest> BuildSpanLinks(ProtoSpan span)
    {
        if (span.Links.Count is 0) return [];

        var links = new List<SpanLinkIngest>(span.Links.Count);
        foreach (var l in span.Links)
        {
            links.Add(new SpanLinkIngest(
                RequireId(l.TraceId, 16, "link.trace_id"),
                RequireId(l.SpanId, 8, "link.span_id"),
                ConvertSpanChildAttributes(l.Attributes), NullIfEmpty(l.TraceState), l.Flags,
                l.DroppedAttributesCount));
        }

        return links;
    }

    private static OtlpAttributeValue ConvertProtoAnyValue(ProtoAnyValue? value, string? attributeKey = null)
    {
        // All keyed carriers, including nested maps and metric metadata, converge here.
        // A key exemption never bypasses this value policy before original data is journaled.
        if (attributeKey is not null
            && CollectorSemanticAttributeCatalog.ExceptionTextKeys.TryGetValue(attributeKey, out var canonicalKey))
            return OtlpAttributeValue.FromString(ExceptionTextSanitizer.Sanitize(canonicalKey, value));
        if (value is null) return OtlpAttributeValue.Empty;

        return value.ValueCase switch
        {
            ProtoAnyValue.ValueOneofCase.StringValue => OtlpAttributeValue.FromString(value.StringValue),
            ProtoAnyValue.ValueOneofCase.IntValue => OtlpAttributeValue.FromInt(value.IntValue),
            ProtoAnyValue.ValueOneofCase.DoubleValue => OtlpAttributeValue.FromDouble(value.DoubleValue),
            ProtoAnyValue.ValueOneofCase.BoolValue => OtlpAttributeValue.FromBool(value.BoolValue),
            ProtoAnyValue.ValueOneofCase.BytesValue => OtlpAttributeValue.FromBytes(CopyBytes(value.BytesValue)),
            ProtoAnyValue.ValueOneofCase.ArrayValue => ConvertProtoArrayValue(value.ArrayValue),
            ProtoAnyValue.ValueOneofCase.KvlistValue => ConvertProtoKeyValueList(value.KvlistValue),
            ProtoAnyValue.ValueOneofCase.None => OtlpAttributeValue.Empty,
            // Alpha, profiles-only (opentelemetry-proto 1.10+): an index into a string table no other
            // signal carries. The proto asks non-profiling receivers to treat it as absent, which is what
            // it was while the field was unknown.
            ProtoAnyValue.ValueOneofCase.StringValueStrindex => OtlpAttributeValue.Empty,
            _ => throw new InvalidDataException("OTLP AnyValue contains an unknown value kind.")
        };
    }

    private static OtlpAttributeValue ConvertProtoArrayValue(ProtoArrayValue value)
    {
        var items = new List<OtlpAttributeValue>(value.Values.Count);
        foreach (var item in value.Values)
        {
            items.Add(ConvertProtoAnyValue(item));
        }

        return OtlpAttributeValue.FromArray(items);
    }

    private static OtlpAttributeValue ConvertProtoKeyValueList(ProtoKeyValueList value)
    {
        ValidateAttributeKeys(value.Values);
        var items = new NormalizedAttributes();
        foreach (var item in value.Values)
        {
            if (IsUnresolvedProfilingKey(item)) continue;
            if (!AttributeKeySets.IsPersistable(item.Key))
            {
                NamiCollectorMetrics.AttributeDropped(item.Key);
                continue;
            }

            items[item.Key] = ConvertProtoAnyValue(item.Value, item.Key);
        }

        return OtlpAttributeValue.FromKeyValueList(items);
    }

    private static SpanIngestionRecord CreateSpanRecordFromProto(
        ProtoSpan span,
        string? projectIdHint,
        string serviceName,
        Dictionary<string, OtlpAttributeValue> attributes,
        Dictionary<string, OtlpAttributeValue> resourceAttributes,
        IReadOnlyList<ResourceEntityRefIngestionRecord> resourceEntityRefs,
        string? schemaUrl,
        string? scopeName,
        string? scopeVersion,
        ISet<string> warnings) =>
        new()
        {
            ProjectIdHint = projectIdHint,
            SpanId = RequireId(span.SpanId, 8, "span_id"),
            TraceId = RequireId(span.TraceId, 16, "trace_id"),
            ParentSpanId = RequireIdOrAbsent(span.ParentSpanId, 8, "parent_span_id"),
            TraceState = NullIfEmpty(span.TraceState),
            Flags = span.Flags,
            DroppedAttributesCount = span.DroppedAttributesCount,
            DroppedEventsCount = span.DroppedEventsCount,
            DroppedLinksCount = span.DroppedLinksCount,
            Name = span.Name ?? "unknown",
            Kind = DefinedOrZero((int)span.Kind, (int)ProtoSpan.Types.SpanKind.Consumer, SpanKindWarning, warnings),
            StartTimeUnixNano = span.StartTimeUnixNano,
            EndTimeUnixNano = span.EndTimeUnixNano,
            StatusCode = span.Status is not null
                ? DefinedOrZero((int)span.Status.Code, (int)ProtoStatusCode.Error, StatusCodeWarning, warnings)
                : null,
            StatusMessage = string.IsNullOrEmpty(span.Status?.Message) ? null : span.Status.Message,
            ServiceName = serviceName,
            Attributes = attributes,
            ResourceAttributes = resourceAttributes,
            ResourceEntityRefs = resourceEntityRefs,
            SchemaUrl = schemaUrl,
            ScopeName = scopeName,
            ScopeVersion = scopeVersion,
            Events = BuildSpanEvents(span),
            Links = BuildSpanLinks(span)
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

        void Reject(int count, InvalidDataException error)
        {
            rejected += count;
            reasons.Add(error.Message);
        }

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
            foreach (var scope in resourceLogs.ScopeLogs)
            {
                Dictionary<string, OtlpAttributeValue> scopeAttributes;
                try { scopeAttributes = ConvertMetadataAttributes(scope.Scope?.Attributes ?? []); }
                catch (InvalidDataException error)
                {
                    Reject(scope.LogRecords.Count, error);
                    ordinal += scope.LogRecords.Count;
                    continue;
                }

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
                                    ScopeName = NullIfEmpty(scope.Scope?.Name),
                                    ScopeVersion = NullIfEmpty(scope.Scope?.Version),
                                    ScopeSchemaUrl = NullIfEmpty(scope.SchemaUrl),
                                    ScopeAttributes = scopeAttributes,
                                    ScopeDroppedAttributesCount = scope.Scope?.DroppedAttributesCount ?? 0,
                                    ResourceSchemaUrl = NullIfEmpty(resourceLogs.SchemaUrl),
                                    ResourceDroppedAttributesCount = resourceLogs.Resource?.DroppedAttributesCount ?? 0
                                });
                    }
                    catch (InvalidDataException error) { Reject(1, error); }
                }
            }
        }

        return new LogIngestionBatch(logs)
        {
            RejectedLogRecords = rejected, RejectionMessage = reasons.Count == 0 ? null : string.Join("; ", reasons)
        };
    }

    private static LogIngestionRecord CreateLogRecord(
        ProtoLogRecord log,
        string? projectIdHint,
        string serviceName,
        Dictionary<string, OtlpAttributeValue> resourceAttributes,
        IReadOnlyList<ResourceEntityRefIngestionRecord> resourceEntityRefs,
        ulong observedTime,
        ISet<string> warnings)
    {
        var attributes = ExtractLogAttributes(log.Attributes);

        var severityNumber = DefinedOrZero((int)log.SeverityNumber, (int)ProtoSeverityNumber.Fatal4,
            SeverityNumberWarning, warnings);

        return new LogIngestionRecord
        {
            ProjectIdHint = projectIdHint,
            TraceId = RequireIdOrAbsent(log.TraceId, 16, "trace_id"),
            SpanId = RequireIdOrAbsent(log.SpanId, 8, "span_id"),
            EventName = NullIfEmpty(log.EventName),
            Flags = log.Flags,
            DroppedAttributesCount = log.DroppedAttributesCount,
            TimeUnixNano = log.TimeUnixNano == 0 ? null : log.TimeUnixNano,
            ObservedTimeUnixNano = log.ObservedTimeUnixNano > 0 ? log.ObservedTimeUnixNano : observedTime,
            ObservedTimeProvided = log.ObservedTimeUnixNano > 0,
            ReceivedTimeUnixNano = observedTime,
            SeverityNumber = severityNumber,
            SeverityText = log.SeverityText,
            Body = ConvertProtoAnyValue(log.Body),
            ServiceName = serviceName,
            Attributes = attributes,
            ResourceAttributes = resourceAttributes,
            ResourceEntityRefs = resourceEntityRefs
        };
    }

    private static Dictionary<string, OtlpAttributeValue> ExtractLogAttributes(RepeatedField<ProtoKeyValue> attributes)
    {
        ValidateAttributeKeys(attributes);
        var dict = new NormalizedAttributes();
        foreach (var attr in attributes)
        {
            if (string.IsNullOrEmpty(attr.Key)) continue;
            var renamed = DeprecatedAttributeNormalizer.TryNormalize(attr.Key, out var key);
            NormalizedAttributes.RecordRename(dict, attr.Key, key);
            if (!AttributeKeySets.IsPersistable(key) &&
                !key.IsAny(AttributeKeySets.SessionCorrelation))
            {
                NamiCollectorMetrics.AttributeDropped(key);
                continue;
            }

            var value = ConvertProtoAnyValue(attr.Value, key);
            SetNormalizedAttribute(dict, key, value, renamed);
        }

        return dict;
    }

    #endregion
}
