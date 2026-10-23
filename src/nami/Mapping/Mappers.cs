using Nami.Api.Contracts.Annotations;
using Nami.Api.Contracts.Common;
using Nami.Api.Contracts.Common.Pagination;
using Nami.Api.Contracts.Domains.Observe.Session;
using Nami.Api.Contracts.OTel.Enums;
using Nami.Api.Contracts.OTel.Logs;
using Nami.Api.Contracts.OTel.Metrics;
using Nami.Api.Contracts.OTel.Traces;
using Nami.Api.Contracts.SavedQueries;
using Nami.Collector.Storage;
using ContractAttribute = Nami.Api.Contracts.Common.Attribute;
using ContractValue = Nami.Api.Contracts.Common.AttributeValue;
using Resource = Nami.Api.Contracts.OTel.Resource.Resource;
using TraceContract = Nami.Api.Contracts.OTel.Traces.Trace;
using ContractMetricKind = Nami.Api.Contracts.OTel.Metrics.MetricKind;
using ContractMetricTemporality = Nami.Api.Contracts.OTel.Metrics.MetricTemporality;
using ContractMetricAggregation = Nami.Api.Contracts.OTel.Metrics.MetricAggregation;
using StorageMetricAggregation = Nami.Collector.Storage.MetricAggregation;
using StorageMetricKind = Nami.Collector.Ingestion.Primitives.MetricKind;
using StorageMetricTemporality = Nami.Collector.Ingestion.Primitives.MetricTemporality;

namespace Nami.Collector.Mapping;

internal static class ContractJson
{
    public static IReadOnlyList<ContractAttribute>? ParseAttributes(string? json)
    {
        if (TryParseAttributes(json, out var attributes)) return attributes;
        throw new InvalidDataException("Stored attributes do not match the persisted attribute value format.");
    }

    private static bool TryParseAttributes(string? json, out IReadOnlyList<ContractAttribute>? attributes)
    {
        attributes = null;
        if (string.IsNullOrWhiteSpace(json))
            return true;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind is not JsonValueKind.Object)
                return false;

            var parsed = new List<ContractAttribute>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!keys.Add(property.Name)) return false;
                parsed.Add(new ContractAttribute
                {
                    Key = property.Name,
                    Value = StoredValueMapper.ToContract(property.Value.Deserialize(StorageJsonSerializerContext.Default.StoredAttributeValue))
                });
            }

            attributes = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Decode current JSON bodies while preserving text stored by older collectors.</summary>
    public static ContractValue? ParseLogBody(string? json)
    {
        if (json is null)
            return null;

        try
        {
            return StoredValueMapper.ToContract(JsonSerializer.Deserialize(json, StorageJsonSerializerContext.Default.StoredAttributeValue));
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Before attribute value storage, log bodies were persisted as raw text.
            // Keep those historical rows readable without rewriting their stored contents.
            return new ContractValue.StringValue(json);
        }
    }
}

internal static class ResourceMapping
{
    public static string ServiceNameOrUnknown(string? serviceName) =>
        string.IsNullOrWhiteSpace(serviceName) ? "unknown" : serviceName;

    public static IReadOnlyList<EntityRef>? ParseEntityRefs(
        string? json,
        IReadOnlyList<ContractAttribute>? resourceAttributes)
    {
        if (TryParseEntityRefs(json, resourceAttributes, out var entityRefs)) return entityRefs;
        throw new InvalidDataException("Stored Resource entity references are invalid.");
    }

    public static IReadOnlyList<EntityRef>? ParseEntityRefs(string? json, string? resourceJson) =>
        ParseEntityRefs(json, ContractJson.ParseAttributes(resourceJson));

    private static bool TryParseEntityRefs(
        string? json,
        IReadOnlyList<ContractAttribute>? resourceAttributes,
        out IReadOnlyList<EntityRef>? entityRefs)
    {
        entityRefs = null;
        if (string.IsNullOrWhiteSpace(json)) return true;

        List<ResourceEntityRefIngestionRecord>? stored;
        try
        {
            stored = JsonSerializer.Deserialize(
                json,
                StorageJsonSerializerContext.Default.ResourceEntityRefIngestionRecordList);
        }
        catch (JsonException)
        {
            return false;
        }

        if (stored is null) return false;
        var resourceKeys = resourceAttributes?.Select(static attribute => attribute.Key)
            .ToHashSet(StringComparer.Ordinal) ?? [];
        if (resourceAttributes is not null && resourceKeys.Count != resourceAttributes.Count) return false;

        var mapped = new List<EntityRef>(stored.Count);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        string? previousIdentity = null;
        foreach (var entityRef in stored)
        {
            if (entityRef is null ||
                entityRef.SchemaUrl is "" ||
                string.IsNullOrWhiteSpace(entityRef.Type) ||
                entityRef.IdKeys is not { Count: > 0 } idKeys ||
                entityRef.DescriptionKeys is not { } descriptionKeys ||
                !IsCanonicalKeyList(idKeys) ||
                !IsCanonicalKeyList(descriptionKeys) ||
                idKeys.Any(key => !resourceKeys.Contains(key)) ||
                descriptionKeys.Any(key => !resourceKeys.Contains(key)))
            {
                return false;
            }

            var identity = GetIdentity(entityRef.Type, idKeys);
            if (!identities.Add(identity) ||
                previousIdentity is not null && StringComparer.Ordinal.Compare(previousIdentity, identity) >= 0)
            {
                return false;
            }

            previousIdentity = identity;
            mapped.Add(new EntityRef
            {
                SchemaUrl = entityRef.SchemaUrl,
                Type = entityRef.Type,
                IdKeys = idKeys,
                DescriptionKeys = descriptionKeys.Count is 0 ? null : descriptionKeys
            });
        }

        entityRefs = mapped.Count is 0 ? null : mapped;
        return true;
    }

    private static bool IsCanonicalKeyList(IReadOnlyList<string> keys)
    {
        string? previous = null;
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key) ||
                previous is not null && StringComparer.Ordinal.Compare(previous, key) >= 0)
            {
                return false;
            }

            previous = key;
        }

        return true;
    }

    private static string GetIdentity(string type, IReadOnlyList<string> idKeys)
    {
        var builder = new StringBuilder();
        builder.AppendSegment(type);
        builder.Append(idKeys.Count.ToString(CultureInfo.InvariantCulture)).Append(':');
        foreach (var key in idKeys)
            builder.AppendSegment(key);
        return builder.ToString();
    }
}

internal static class SpanMapper
{
    public static List<Span> ToContracts(IEnumerable<SpanStorageRow> records) =>
    [
        .. records.Select(ToContractCore)
    ];

    public static TraceContract ToTrace(string traceId, IReadOnlyList<Span> spans)
    {
        var rootSpan = spans.FirstOrDefault(static s => s.ParentSpanId is null);
        var start = spans.Min(static s => s.StartTimeUnixNano);
        var end = spans.Max(static s => s.EndTimeUnixNano);
        var duration = end >= start ? end - start : 0UL;

        return new TraceContract
        {
            TraceId = traceId,
            Spans = spans,
            RootSpan = rootSpan,
            SpanCount = spans.Count,
            DurationNs = duration,
            StartTime = Api.Contracts.ContractJson.FormatTimestamp(NamiTimeConversions.NanosToDateTimeOffset(start)),
            EndTime = Api.Contracts.ContractJson.FormatTimestamp(NamiTimeConversions.NanosToDateTimeOffset(end)),
            Services = [.. spans.Select(static s => s.Resource.ServiceName).Distinct(StringComparer.Ordinal)],
            HasError = spans.Any(static s => s.Status.Code is SpanStatusCode.Error)
        };
    }

    private static SpanKind MapSpanKind(byte kind) =>
        kind switch
        {
            1 => SpanKind.Internal,
            2 => SpanKind.Server,
            3 => SpanKind.Client,
            4 => SpanKind.Producer,
            5 => SpanKind.Consumer,
            _ => SpanKind.Unspecified
        };

    private static SpanStatusCode MapStatus(byte statusCode) =>
        statusCode switch
        {
            1 => SpanStatusCode.Ok,
            2 => SpanStatusCode.Error,
            _ => SpanStatusCode.Unset
        };

    private static IReadOnlyList<SpanEvent> MapEvents(string? eventsJson)
    {
        var stored = SpanChildStorage.DeserializeEvents(eventsJson);
        if (stored is null || stored.Count is 0) return [];

        var result = new List<SpanEvent>(stored.Count);
        foreach (var e in stored)
        {
            result.Add(new SpanEvent
            {
                Name = e.Name,
                TimeUnixNano = e.TimeUnixNano,
                Attributes = ContractJson.ParseAttributes(e.AttributesJson),
                DroppedAttributesCount = e.DroppedAttributesCount
            });
        }

        return result;
    }

    private static IReadOnlyList<SpanLink> MapLinks(string? linksJson)
    {
        var stored = SpanChildStorage.DeserializeLinks(linksJson);
        if (stored is null || stored.Count is 0) return [];

        var result = new List<SpanLink>(stored.Count);
        foreach (var l in stored)
        {
            result.Add(new SpanLink
            {
                TraceId = l.TraceId,
                SpanId = l.SpanId,
                Attributes = ContractJson.ParseAttributes(l.AttributesJson),
                TraceState = l.TraceState,
                Flags = l.Flags,
                DroppedAttributesCount = l.DroppedAttributesCount
            });
        }

        return result;
    }

    private static Span ToContractCore(SpanStorageRow row)
    {
        var attributes = ContractJson.ParseAttributes(row.AttributesJson);
        var resourceAttributes = ContractJson.ParseAttributes(row.ResourceJson);

        return new Span
        {
            TraceId = row.TraceId,
            SpanId = row.SpanId,
            ParentSpanId = row.ParentSpanId,
            Name = row.Name,
            Kind = MapSpanKind(row.Kind),
            StartTimeUnixNano = row.StartTimeUnixNano,
            EndTimeUnixNano = row.EndTimeUnixNano,
            TraceState = row.TraceState,
            Flags = row.Flags,
            DroppedAttributesCount = row.DroppedAttributesCount,
            DroppedEventsCount = row.DroppedEventsCount,
            DroppedLinksCount = row.DroppedLinksCount,
            Attributes = attributes,
            Events = MapEvents(row.EventsJson),
            Links = MapLinks(row.LinksJson),
            Status = new SpanStatus
            {
                Code = MapStatus(row.StatusCode),
                Message = row.StatusMessage
            },
            Resource = new Resource
            {
                ServiceName = ResourceMapping.ServiceNameOrUnknown(row.ServiceName),
                Attributes = resourceAttributes,
                EntityRefs = ResourceMapping.ParseEntityRefs(row.ResourceEntityRefsJson, resourceAttributes),
                SchemaUrl = row.ResourceSchemaUrl,
                DroppedAttributesCount = row.ResourceDroppedAttributesCount
            },
            InstrumentationScope = row.ScopeName is null && row.ScopeVersion is null &&
                                   row.ScopeSchemaUrl is null && row.ScopeAttributesJson is null &&
                                   row.ScopeDroppedAttributesCount is null or 0
                ? null
                : new InstrumentationScope
                {
                    ScopeName = row.ScopeName,
                    ScopeVersion = row.ScopeVersion,
                    SchemaUrl = row.ScopeSchemaUrl,
                    ScopeAttributes = ContractJson.ParseAttributes(row.ScopeAttributesJson),
                    DroppedAttributesCount = row.ScopeDroppedAttributesCount
                }
        };
    }
}

internal static class LogMapper
{
    public static LogRecord ToContract(LogStorageRow record) =>
        new()
        {
            TimeUnixNano = record.TimeUnixNano is > 0 ? record.TimeUnixNano : null,
            ObservedTimeUnixNano = record.ObservedTimeUnixNano,
            SeverityNumber = MapSeverityNumber(record.SeverityNumber),
            SeverityText = record.SeverityText,
            Body = ContractJson.ParseLogBody(record.Body),
            Attributes = ContractJson.ParseAttributes(record.AttributesJson),
            TraceId = record.TraceId,
            SpanId = record.SpanId,
            EventName = record.EventName,
            Flags = record.Flags,
            DroppedAttributesCount = record.DroppedAttributesCount,
            InstrumentationScope = record.ScopeName is null && record.ScopeVersion is null &&
                                   record.ScopeSchemaUrl is null && record.ScopeAttributesJson is null &&
                                   record.ScopeDroppedAttributesCount is null or 0
                ? null
                : new InstrumentationScope
                {
                    ScopeName = record.ScopeName,
                    ScopeVersion = record.ScopeVersion,
                    SchemaUrl = record.ScopeSchemaUrl,
                    ScopeAttributes = ContractJson.ParseAttributes(record.ScopeAttributesJson),
                    DroppedAttributesCount = record.ScopeDroppedAttributesCount
                },
            Resource = new Resource
            {
                ServiceName = ResourceMapping.ServiceNameOrUnknown(record.ServiceName),
                Attributes = ContractJson.ParseAttributes(record.ResourceJson),
                EntityRefs = ResourceMapping.ParseEntityRefs(record.ResourceEntityRefsJson, record.ResourceJson),
                SchemaUrl = record.ResourceSchemaUrl,
                DroppedAttributesCount = record.ResourceDroppedAttributesCount
            }
        };

    public static IReadOnlyList<LogRecord> ToContracts(IReadOnlyList<LogStorageRow> records) =>
        [.. records.Select(static record => ToContract(record))];

    private static SeverityNumber MapSeverityNumber(byte severityNumber) =>
        Enum.IsDefined(typeof(SeverityNumber), (int)severityNumber)
            ? (SeverityNumber)severityNumber
            : SeverityNumber.Unspecified;

}

internal static class SessionMapper
{
    public static SessionEntity ToContract(SessionQueryRow summary, ulong activeCutoffUnixNano)
    {
        var startTime = AsUtcOffset(summary.StartTime);
        var lastActivity = AsUtcOffset(summary.LastActivity);
        var isActive = summary.LastActivityUnixNano >= activeCutoffUnixNano;

        return new SessionEntity
        {
            SessionId = summary.SessionId,
            StartTime = Api.Contracts.ContractJson.FormatTimestamp(startTime),
            EndTime = isActive ? null : Api.Contracts.ContractJson.FormatTimestamp(lastActivity),
            DurationMs = summary.DurationMs,
            SpanCount = ToInt32(summary.SpanCount),
            TraceCount = ToInt32(summary.TraceCount),
            ErrorCount = ToInt32(summary.ErrorCount),
            Services = [.. summary.Services],
            State = isActive ? SessionState.Active : SessionState.Ended,
            GenaiUsage = new SessionGenAiUsage
            {
                RequestCount = ToInt32(summary.GenAiRequestCount),
                TotalInputTokens = summary.InputTokens,
                TotalOutputTokens = summary.OutputTokens,
                ModelsUsed = [.. summary.Models],
                ProvidersUsed = [.. summary.Providers]
            }
        };
    }

    public static CursorPageSessionEntity ToPage(
        IReadOnlyList<SessionQueryRow> summaries,
        ulong activeCutoffUnixNano,
        bool hasMore,
        string? nextCursor) =>
        new()
        {
            Items = [.. summaries.Select(summary => ToContract(summary, activeCutoffUnixNano))],
            HasMore = hasMore,
            NextCursor = nextCursor
        };

    private static int ToInt32(long value) =>
        value switch
        {
            > int.MaxValue => int.MaxValue,
            _ => (int)value
        };

    private static DateTimeOffset AsUtcOffset(DateTime timestamp) =>
        new(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc));
}

internal static class MetricMapper
{
    public static MetricDescriptor ToContract(MetricCatalogEntry entry) =>
        new()
        {
            Name = entry.MetricName,
            InstrumentId = entry.InstrumentId,
            HasConflict = entry.Variants.Count > 1,
            Resource = new Resource
            {
                ServiceName = ResourceMapping.ServiceNameOrUnknown(entry.ServiceName),
                Attributes = ContractJson.ParseAttributes(entry.ResourceJson),
                EntityRefs = ResourceMapping.ParseEntityRefs(entry.ResourceEntityRefsJson, entry.ResourceJson),
                SchemaUrl = entry.ResourceSchemaUrl,
                DroppedAttributesCount = entry.ResourceDroppedAttributesCount
            },
            InstrumentationScope = entry.ScopeName is null && entry.ScopeVersion is null &&
                                   entry.ScopeSchemaUrl is null && entry.ScopeAttributesJson is null &&
                                   entry.ScopeDroppedAttributesCount is null or 0 ? null : new InstrumentationScope
            {
                ScopeName = entry.ScopeName, ScopeVersion = entry.ScopeVersion, SchemaUrl = entry.ScopeSchemaUrl,
                ScopeAttributes = ContractJson.ParseAttributes(entry.ScopeAttributesJson),
                DroppedAttributesCount = entry.ScopeDroppedAttributesCount
            },
            Kind = ToKind(entry.MetricKind),
            Temporality = ToTemporality(entry.Temporality),
            Monotonic = entry.IsMonotonic is not 0,
            Unit = entry.Unit,
            Description = entry.Description,
            Services = entry.Variants.SelectMany(static variant => variant.Services)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            Conflicts = entry.Variants.Count <= 1 ? null : entry.Variants.Select(static variant => new MetricDescriptorConflict
            {
                Kind = ToKind(variant.Kind),
                Temporality = ToTemporality(variant.Temporality),
                Monotonic = variant.IsMonotonic is not 0,
                Unit = variant.Unit,
                Services = variant.Services
            }).ToArray(),
            SupportedAggregations = SupportedAggregations(entry.Variants.Count is 0
                ? [entry.MetricKind] : entry.Variants.Select(static variant => variant.Kind)),
            SeriesCount = entry.SeriesCount,
            LastSeen = Api.Contracts.ContractJson.FormatTimestamp(NamiTimeConversions.NanosToDateTimeOffset(entry.LastSeenUnixNano))
        };

    public static IReadOnlyList<MetricDescriptor> ToContracts(IReadOnlyList<MetricCatalogEntry> entries) =>
        [.. entries.Select(static entry => ToContract(entry))];

    internal static IReadOnlyList<ContractMetricAggregation> SupportedAggregations(IEnumerable<byte> kinds)
    {
        var present = kinds.ToArray();
        if (present.Contains((byte)StorageMetricKind.Summary)) return [];
        return present.All(static kind => kind is (byte)StorageMetricKind.Histogram or (byte)StorageMetricKind.ExponentialHistogram)
            ? [ContractMetricAggregation.Avg, ContractMetricAggregation.Min, ContractMetricAggregation.Max,
                ContractMetricAggregation.Sum, ContractMetricAggregation.Count, ContractMetricAggregation.Last,
                ContractMetricAggregation.P50, ContractMetricAggregation.P90, ContractMetricAggregation.P95, ContractMetricAggregation.P99]
            : [ContractMetricAggregation.Avg, ContractMetricAggregation.Min, ContractMetricAggregation.Max,
                ContractMetricAggregation.Sum, ContractMetricAggregation.Count, ContractMetricAggregation.Last];
    }

    public static MetricSeries ToContract(MetricSeriesRow row) =>
        new()
        {
            SeriesId = row.SeriesId,
            Name = row.MetricName,
            Kind = ToKind(row.MetricKind),
            Attributes = ContractJson.ParseAttributes(row.AttributesJson) ?? [],
            ServiceName = row.ServiceName,
            Unit = row.Unit,
            Resource = new Resource
            {
                ServiceName = ResourceMapping.ServiceNameOrUnknown(row.ServiceName),
                Attributes = ContractJson.ParseAttributes(row.ResourceJson),
                EntityRefs = ResourceMapping.ParseEntityRefs(row.ResourceEntityRefsJson, row.ResourceJson),
                SchemaUrl = row.ResourceSchemaUrl,
                DroppedAttributesCount = row.ResourceDroppedAttributesCount
            },
            InstrumentationScope = row.ScopeName is null && row.ScopeVersion is null &&
                                   row.ScopeSchemaUrl is null && row.ScopeAttributesJson is null &&
                                   row.ScopeDroppedAttributesCount is null or 0
                ? null
                : new InstrumentationScope
                {
                    ScopeName = row.ScopeName,
                    ScopeVersion = row.ScopeVersion,
                    SchemaUrl = row.ScopeSchemaUrl,
                    ScopeAttributes = ContractJson.ParseAttributes(row.ScopeAttributesJson),
                    DroppedAttributesCount = row.ScopeDroppedAttributesCount
                },
            FirstSeen = Api.Contracts.ContractJson.FormatTimestamp(NamiTimeConversions.NanosToDateTimeOffset(row.FirstSeenUnixNano)),
            LastSeen = Api.Contracts.ContractJson.FormatTimestamp(NamiTimeConversions.NanosToDateTimeOffset(row.LastSeenUnixNano))
        };

    public static IReadOnlyList<MetricSeries> ToContracts(IReadOnlyList<MetricSeriesRow> rows) =>
        [.. rows.Select(static row => ToContract(row))];

    public static MetricPoint ToContract(MetricObservationPageItem item, MetricSeriesRow series)
    {
        var row = item.Observation;
        var projection = MetricProjection.Project(row, item.HasConflict);
        return new MetricPoint
        {
            ObservationId = row.ObservationId, HasConflict = item.HasConflict,
            SeriesId = row.SeriesId, TimeUnixNano = row.TimeUnixNano,
            StartTimeUnixNano = row.StartTimeUnixNano, Flags = row.Flags,
            ResourceDroppedAttributesCount = row.ResourceDroppedAttributesCount,
            ScopeDroppedAttributesCount = row.ScopeDroppedAttributesCount,
            Attributes = ContractJson.ParseAttributes(series.AttributesJson) ?? [],
            Data = row.DataJson is null ? null : StoredValueMapper.ToContract(JsonSerializer.Deserialize(row.DataJson, StorageJsonSerializerContext.Default.StoredMetricPointData)),
            Exemplars = row.ExemplarsJson is null ? null : StoredValueMapper.ToContract(JsonSerializer.Deserialize(row.ExemplarsJson, StorageJsonSerializerContext.Default.StoredExemplars)),
            InstrumentMetadata = row.InstrumentMetadataJson is null ? null : StoredValueMapper.ToContract(JsonSerializer.Deserialize(row.InstrumentMetadataJson, StorageJsonSerializerContext.Default.StoredAttributes)),
            HistoricalExemplarTraceIds = row.HistoricalExemplarTraceIdsJson is null ? null : JsonSerializer.Deserialize(row.HistoricalExemplarTraceIdsJson, StorageJsonSerializerContext.Default.IReadOnlyListString),
            Projection = item.HasConflict ? null : new MetricPointProjection
            {
                Value = projection.Value, Count = projection.HistogramCount, Sum = projection.HistogramSum,
                Min = projection.HistogramMin, Max = projection.HistogramMax,
                ExplicitBounds = projection.BucketBoundsJson is null ? null : JsonSerializer.Deserialize(projection.BucketBoundsJson, StorageJsonSerializerContext.Default.Doubles),
                BucketCounts = projection.BucketCountsJson is null ? null : JsonSerializer.Deserialize(projection.BucketCountsJson, StorageJsonSerializerContext.Default.Counts)
            },
            ProjectionUnavailableReason = projection.ProjectionUnavailableReason
        };
    }

    /// <summary>
    /// Builds the whole query answer from the catalog entry that identified the metric and
    /// the streams the store produced. The endpoint hands over both and reads neither: the
    /// instrument's shape and unit are storage facts, and this is where they stop being one.
    /// </summary>
    public static MetricQueryResult ToQueryResult(
        MetricCatalogEntry descriptor,
        StorageMetricAggregation aggregation,
        long stepMs,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        IReadOnlyList<MetricQuerySeries> series,
        bool truncated) =>
        new()
        {
            Name = descriptor.MetricName,
            Kind = ToKind(descriptor.MetricKind),
            Unit = descriptor.Unit,
            Aggregation = ToAggregation(aggregation),
            StepMs = stepMs,
            StartTime = Api.Contracts.ContractJson.FormatTimestamp(startTime),
            EndTime = Api.Contracts.ContractJson.FormatTimestamp(endTime),
            Series = [.. series.Select(static stream => ToContract(stream))],
            Truncated = truncated,
            NumericRepresentation = "float64",
            Approximate = aggregation is StorageMetricAggregation.P50 or StorageMetricAggregation.P90 or StorageMetricAggregation.P95 or StorageMetricAggregation.P99
        };

    private static MetricSeriesResult ToContract(MetricQuerySeries stream) =>
        new()
        {
            Attributes = ContractJson.ParseAttributes(stream.AttributesJson) ?? [],
            Buckets =
            [
                .. stream.Points.Select(static point => new MetricBucket
                {
                    BucketStart = Api.Contracts.ContractJson.FormatTimestamp(NamiTimeConversions.NanosToDateTimeOffset(point.BucketStartUnixNano)),
                    Value = point.Value,
                    PointCount = (long)point.PointCount
                })
            ]
        };

    // The storage enums and the contract enums are two spellings of one vocabulary, kept
    // apart on purpose: storage bytes are a persisted format and the contract names are a
    // published one, and neither should be free to drag the other along when it changes.
    private static ContractMetricKind ToKind(byte metricKind) => metricKind switch
    {
        (byte)StorageMetricKind.Sum => ContractMetricKind.Sum,
        (byte)StorageMetricKind.Histogram => ContractMetricKind.Histogram,
        (byte)StorageMetricKind.ExponentialHistogram => ContractMetricKind.Exponential_histogram,
        (byte)StorageMetricKind.Summary => ContractMetricKind.Summary,
        (byte)StorageMetricKind.Gauge => ContractMetricKind.Gauge,
        _ => throw new InvalidDataException("Unknown persisted metric kind.")
    };

    private static ContractMetricTemporality ToTemporality(byte temporality) => temporality switch
    {
        (byte)StorageMetricTemporality.Delta => ContractMetricTemporality.Delta,
        (byte)StorageMetricTemporality.Cumulative => ContractMetricTemporality.Cumulative,
        _ => ContractMetricTemporality.Unspecified
    };

    private static ContractMetricAggregation ToAggregation(StorageMetricAggregation aggregation) => aggregation switch
    {
        StorageMetricAggregation.Min => ContractMetricAggregation.Min,
        StorageMetricAggregation.Max => ContractMetricAggregation.Max,
        StorageMetricAggregation.Sum => ContractMetricAggregation.Sum,
        StorageMetricAggregation.Count => ContractMetricAggregation.Count,
        StorageMetricAggregation.Last => ContractMetricAggregation.Last,
        StorageMetricAggregation.P50 => ContractMetricAggregation.P50,
        StorageMetricAggregation.P90 => ContractMetricAggregation.P90,
        StorageMetricAggregation.P95 => ContractMetricAggregation.P95,
        StorageMetricAggregation.P99 => ContractMetricAggregation.P99,
        _ => ContractMetricAggregation.Avg
    };
}

internal static class AnnotationMapper
{
    public const string TraceKind = "trace";
    public const string SpanKind = "span";
    public const string SessionKind = "session";

    public static Annotation ToContract(AnnotationRow row, string? failureId = null, IReadOnlyList<string>? labels = null, AnnotationClassification? classification = null) =>
        new()
        {
            Id = row.AnnotationId,
            CreatedAt = Api.Contracts.ContractJson.FormatTimestamp(NamiTimeConversions.NanosToDateTimeOffset(row.CreatedAtUnixNano)),
            UpdatedAt = Api.Contracts.ContractJson.FormatTimestamp(NamiTimeConversions.NanosToDateTimeOffset(row.UpdatedAtUnixNano)),
            Etag = row.Etag,
            Target = row.TargetKind switch
            {
                TraceKind => new TraceAnnotationTarget { TraceId = row.TraceId! },
                SpanKind => new SpanAnnotationTarget { TraceId = row.TraceId!, SpanId = row.SpanId! },
                SessionKind => new SessionAnnotationTarget { SessionId = row.SessionId! },
                "failure" => new FailureAnnotationTarget { FailureId = failureId ?? throw new InvalidDataException("Failure annotation metadata is missing.") },
                _ => throw new InvalidDataException($"Stored annotation target kind '{row.TargetKind}' is unknown.")
            },
            Text = row.Text,
            Labels = labels,
            Classification = classification
        };
}

internal static class SavedQueryMapper
{
    public static SavedQuery ToContract(SavedQueryRow row) =>
        new()
        {
            Id = row.SavedQueryId,
            CreatedAt = Api.Contracts.ContractJson.FormatTimestamp(NamiTimeConversions.NanosToDateTimeOffset(row.CreatedAtUnixNano)),
            UpdatedAt = Api.Contracts.ContractJson.FormatTimestamp(NamiTimeConversions.NanosToDateTimeOffset(row.UpdatedAtUnixNano)),
            Etag = row.Etag,
            Name = row.Name,
            Description = row.Description,
            Filter = ReadFilter(row.FilterJson)
        };

    public static IReadOnlyList<SavedQuery> ToContracts(IReadOnlyList<SavedQueryRow> rows) =>
        [.. rows.Select(static row => ToContract(row))];

    private static SavedQueryFilter ReadFilter(string filterJson) =>
        JsonSerializer.Deserialize(filterJson, global::Nami.Api.Contracts.ContractTypes.TypeInfo<global::Nami.Api.Contracts.SavedQueries.SavedQueryFilter>())
        ?? throw new InvalidDataException("A stored saved-query filter is empty.");

    public static string WriteFilter(SavedQueryFilter filter) =>
        JsonSerializer.Serialize(filter, global::Nami.Api.Contracts.ContractTypes.TypeInfo<global::Nami.Api.Contracts.SavedQueries.SavedQueryFilter>());

    public static string SignalName(SavedQueryFilter filter) => filter switch
    {
        SavedTraceQueryFilter => "traces",
        SavedLogQueryFilter => "logs",
        SavedMetricQueryFilter => "metrics",
        _ => throw new ArgumentOutOfRangeException(nameof(filter), "Unknown saved-query filter.")
    };
}
