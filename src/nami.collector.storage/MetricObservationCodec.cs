using MetricKind = Nami.Collector.Ingestion.Primitives.MetricKind;

namespace Nami.Collector.Storage;

internal sealed record HistoricalMetricProjection(
    double? Value,
    ulong? HistogramCount,
    double? HistogramSum,
    double? HistogramMin,
    double? HistogramMax,
    string? BucketBoundsJson,
    string? BucketCountsJson);

internal static class MetricObservationCodec
{
    internal static MetricObservationRow FromIngestion(MetricPointIngestionRecord point, string projectId, string seriesId)
    {
        var stored = StoredRecordMapper.ToStored(point.Data);
        var data = JsonSerializer.Serialize(stored, StorageJsonSerializerContext.Default.StoredMetricPointData);
        var exemplars = JsonSerializer.Serialize(StoredRecordMapper.ToStored(point.Exemplars),
            StorageJsonSerializerContext.Default.StoredExemplars);
        var metadata = JsonSerializer.Serialize(StoredRecordMapper.ToStored(point.InstrumentMetadata),
            StorageJsonSerializerContext.Default.StoredAttributes);
        var time = point.TimeUnixNano.ToString(CultureInfo.InvariantCulture);
        var start = point.StartTimeUnixNano?.ToString(CultureInfo.InvariantCulture);
        var measuredData = (point.Flags & 1) != 0 ? "no-recorded-value" : stored switch
        {
            StoredNumberPointData number => "number:" + NumericIdentity(number.Value),
            StoredHistogramPointData or StoredExponentialHistogramPointData or StoredSummaryPointData => data,
            _ => throw new InvalidDataException("Unknown canonical metric data shape.")
        };
        return new MetricObservationRow
        {
            ProjectId = projectId, SeriesId = seriesId,
            TimeUnixNano = point.TimeUnixNano, StartTimeUnixNano = point.StartTimeUnixNano,
            Flags = point.Flags, DataJson = data, ExemplarsJson = exemplars, InstrumentMetadataJson = metadata,
            ResourceDroppedAttributesCount = point.ResourceDroppedAttributesCount,
            ScopeDroppedAttributesCount = point.ScopeDroppedAttributesCount,
            ObservationId = Fingerprint("mo_", time, start, point.Flags.ToString(CultureInfo.InvariantCulture), data, exemplars, metadata,
                point.ResourceDroppedAttributesCount.ToString(CultureInfo.InvariantCulture),
                point.ScopeDroppedAttributesCount.ToString(CultureInfo.InvariantCulture)),
            MeasurementId = Fingerprint("mm_", time, point.Kind == MetricKind.Gauge ? null : start,
                (point.Flags & 1).ToString(CultureInfo.InvariantCulture), measuredData)
        };
    }

    private static string NumericIdentity(StoredNumberValue? value) => value switch
    {
        StoredIntValue integer => integer.Value.ToString(CultureInfo.InvariantCulture),
        StoredDoubleValue number when double.IsInteger(number.Value) &&
                                      number.Value is >= long.MinValue and < 9_223_372_036_854_775_808d =>
            ((long)number.Value).ToString(CultureInfo.InvariantCulture),
        StoredDoubleValue number => number.Value.ToString("R", CultureInfo.InvariantCulture),
        null => "absent",
        _ => throw new InvalidDataException("Unknown OTLP metric numeric representation.")
    };

    internal static MetricObservationRow FromHistorical(MetricPointRow point, IReadOnlyList<string> traceIds)
    {
        var projection = new HistoricalMetricProjection(point.Value, point.HistogramCount,
            point.HistogramSum, point.HistogramMin, point.HistogramMax,
            point.BucketBoundsJson, point.BucketCountsJson);
        var json = JsonSerializer.Serialize(projection, StorageJsonSerializerContext.Default.HistoricalMetricProjection);
        var traceJson = JsonSerializer.Serialize(traceIds, StorageJsonSerializerContext.Default.IReadOnlyListString);
        var time = point.TimeUnixNano.ToString(CultureInfo.InvariantCulture);
        var start = point.StartTimeUnixNano?.ToString(CultureInfo.InvariantCulture);
        return new MetricObservationRow
        {
            ProjectId = point.ProjectId, SeriesId = point.SeriesId,
            TimeUnixNano = point.TimeUnixNano, StartTimeUnixNano = point.StartTimeUnixNano,
            HistoricalProjectionJson = json, HistoricalExemplarTraceIdsJson = traceJson,
            MeasurementId = Fingerprint("mm_", "historical", time, start, json),
            ObservationId = Fingerprint("mo_", "historical", time, start, json, traceJson)
        };
    }

    private static string Fingerprint(string prefix, params string?[] parts)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var part in parts) writer.WriteStringValue(part);
            writer.WriteEndArray();
        }
        return prefix + Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }
}
