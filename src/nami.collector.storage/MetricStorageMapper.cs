using Nami.Collector.Ingestion.Primitives;

namespace Nami.Collector.Storage;

/// <summary>
/// Folds the flat OTLP data points the converter produces into the two-table storage shape:
/// one <see cref="MetricSeriesRow" /> per distinct stream and canonical observations
/// for every nonidentical producer record.
/// </summary>
internal static class MetricStorageMapper
{
    internal readonly record struct MetricWriteSet(
        IReadOnlyList<MetricSeriesRow> Series,
        IReadOnlyList<MetricObservationRow> Observations);

    public static MetricWriteSet ToStorageRows(MetricIngestionBatch batch, string? authenticatedProjectId = null)
    {
        var series = new Dictionary<(string ProjectId, string SeriesId), MetricSeriesRow>();
        var observations = new Dictionary<(string ProjectId, string SeriesId, string ObservationId), MetricObservationRow>();

        foreach (var record in batch.Points)
        {
            var projectId = ProjectScope.ForIngest(record.ProjectIdHint, authenticatedProjectId);
            var attributesJson = PersistedAttributePolicy.SerializeAttributes(record.Attributes);
            var resourceJson = PersistedAttributePolicy.SerializeAttributes(record.ResourceAttributes);
            var resourceEntityRefsJson = StorageJson.SerializeResourceEntityRefs(record.ResourceEntityRefs);
            var scopeAttributesJson = PersistedAttributePolicy.SerializeScopeAttributes(record.ScopeAttributes);
            var instrumentId = ComputeInstrumentId(
                record.MetricName,
                record.ServiceName,
                resourceJson,
                record.ScopeName,
                record.ScopeVersion,
                record.ScopeSchemaUrl,
                scopeAttributesJson,
                record.ResourceSchemaUrl, record.ResourceIdentity, record.ScopeIdentity);
            var seriesId = ComputeSeriesId(instrumentId, attributesJson,
                record.Kind,
                record.Temporality,
                record.IsMonotonic,
                record.Unit, record.AttributesIdentity);

            var key = (projectId, seriesId);
            var row = new MetricSeriesRow
            {
                ProjectId = projectId,
                SeriesId = seriesId,
                InstrumentId = instrumentId,
                MetricName = record.MetricName,
                MetricKind = (byte)record.Kind,
                Temporality = (byte)record.Temporality,
                IsMonotonic = record.IsMonotonic ? (byte)1 : (byte)0,
                Unit = record.Unit,
                Description = record.Description,
                ServiceName = record.ServiceName,
                SchemaUrl = record.SchemaUrl,
                AttributesJson = attributesJson,
                ResourceJson = resourceJson,
                ResourceEntityRefsJson = resourceEntityRefsJson,
                ScopeName = record.ScopeName,
                ScopeVersion = record.ScopeVersion,
                ScopeSchemaUrl = record.ScopeSchemaUrl,
                ScopeAttributesJson = scopeAttributesJson,
                ScopeDroppedAttributesCount = record.ScopeDroppedAttributesCount,
                ResourceSchemaUrl = record.ResourceSchemaUrl,
                ResourceDroppedAttributesCount = record.ResourceDroppedAttributesCount,
                FirstSeenUnixNano = record.TimeUnixNano,
                LastSeenUnixNano = record.TimeUnixNano
            };

            // One export can carry many points for one stream. Collapse them here rather than
            // sending N conflicting inserts through the writer for the same primary key.
            series[key] = series.TryGetValue(key, out var existing)
                ? row with
                {
                    Description = LongerDescription(existing.Description, row.Description),
                    FirstSeenUnixNano = Math.Min(existing.FirstSeenUnixNano, record.TimeUnixNano),
                    LastSeenUnixNano = Math.Max(existing.LastSeenUnixNano, record.TimeUnixNano)
                }
                : row;

            var observation = MetricObservationCodec.FromIngestion(record, projectId, seriesId);
            observations[(projectId, seriesId, observation.ObservationId)] = observation;
        }

        return new MetricWriteSet([.. series.Values], [.. observations.Values]);
    }

    /// <summary>
    /// Resource, scope and name identify the catalog instrument. Point attributes and
    /// conflicting identifying properties distinguish streams within that instrument.
    /// https://github.com/open-telemetry/opentelemetry-specification/blob/v1.61.0/specification/metrics/data-model.md#opentelemetry-protocol-data-model
    /// </summary>
    internal static string ComputeInstrumentId(
        string metricName,
        string? serviceName,
        string? resourceJson,
        string? scopeName,
        string? scopeVersion,
        string? scopeSchemaUrl,
        string? scopeAttributesJson,
        string? resourceSchemaUrl, string? resourceIdentity = null, string? scopeIdentity = null)
    {
        var builder = new StringBuilder("metric_instrument\n");
        builder.AppendSegment(metricName, terminate: true);
        builder.AppendSegment(serviceName, terminate: true);
        builder.AppendSegment(resourceJson, terminate: true);
        builder.AppendSegment(scopeName, terminate: true);
        builder.AppendSegment(scopeVersion, terminate: true);
        builder.AppendSegment(scopeSchemaUrl, terminate: true);
        builder.AppendSegment(scopeAttributesJson, terminate: true);
        builder.AppendSegment(resourceSchemaUrl, terminate: true);
        if (resourceIdentity is not null) builder.AppendSegment(resourceIdentity, terminate: true);
        if (scopeIdentity is not null) builder.AppendSegment(scopeIdentity, terminate: true);
        return "mi_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())).AsSpan(0, 16));
    }

    internal static string InstrumentId(MetricSeriesRow row) => ComputeInstrumentId(
        row.MetricName, row.ServiceName, row.ResourceJson, row.ScopeName, row.ScopeVersion,
        row.ScopeSchemaUrl, row.ScopeAttributesJson, row.ResourceSchemaUrl);

    internal static string SeriesId(MetricSeriesRow row) => ComputeSeriesId(
        row.InstrumentId ?? InstrumentId(row), row.AttributesJson,
        (MetricKind)row.MetricKind, (MetricTemporality)row.Temporality, row.IsMonotonic != 0, row.Unit);

    internal static MetricCatalogEntry ToCatalogEntry(IReadOnlyCollection<MetricSeriesRow> rows)
    {
        var first = rows.OrderBy(static row => row.SeriesId, StringComparer.Ordinal).First();
        var variants = rows.GroupBy(static row => (row.MetricKind, row.Temporality, row.IsMonotonic, row.Unit))
            .OrderBy(static group => group.Key.MetricKind).ThenBy(static group => group.Key.Temporality)
            .ThenBy(static group => group.Key.IsMonotonic).ThenBy(static group => group.Key.Unit, StringComparer.Ordinal)
            .Select(static group => new MetricDescriptorVariant(group.Key.MetricKind, group.Key.Temporality,
                group.Key.IsMonotonic, group.Key.Unit,
                group.Select(static row => row.ServiceName).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()))
            .ToArray();
        var shape = variants[0];
        return new MetricCatalogEntry(first.MetricName, shape.Kind, shape.Temporality, shape.IsMonotonic, shape.Unit,
            rows.Select(static row => row.Description).Aggregate(LongerDescription), rows.Count,
            rows.Max(static row => row.LastSeenUnixNano))
        {
            InstrumentId = first.InstrumentId ?? InstrumentId(first), Variants = variants,
            ServiceName = first.ServiceName, ResourceJson = first.ResourceJson, ResourceEntityRefsJson = first.ResourceEntityRefsJson,
            ResourceSchemaUrl = first.ResourceSchemaUrl, ResourceDroppedAttributesCount = first.ResourceDroppedAttributesCount,
            ScopeName = first.ScopeName, ScopeVersion = first.ScopeVersion, ScopeSchemaUrl = first.ScopeSchemaUrl,
            ScopeAttributesJson = first.ScopeAttributesJson, ScopeDroppedAttributesCount = first.ScopeDroppedAttributesCount
        };
    }

    private static string ComputeSeriesId(string instrumentId, string? attributesJson,
        MetricKind kind,
        MetricTemporality temporality,
        bool isMonotonic,
        string? unit, string? attributesIdentity = null)
    {
        var builder = new StringBuilder(512);
        builder.Append("metric_series\n");
        builder.AppendSegment(instrumentId, terminate: true);
        builder.AppendSegment(attributesJson, terminate: true);
        builder.AppendSegment(((byte)kind).ToString(CultureInfo.InvariantCulture), terminate: true);
        builder.AppendSegment(((byte)temporality).ToString(CultureInfo.InvariantCulture), terminate: true);
        builder.AppendSegment(isMonotonic ? "1" : "0", terminate: true);
        builder.AppendSegment(unit, terminate: true);
        if (attributesIdentity is not null) builder.AppendSegment(attributesIdentity, terminate: true);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return "ms_" + Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }

    // Description is non-identifying; retain the longer value on re-registration.
    // https://github.com/open-telemetry/opentelemetry-specification/blob/v1.61.0/specification/metrics/data-model.md#opentelemetry-protocol-data-model-producer-recommendations
    private static string? LongerDescription(string? left, string? right) =>
        (left?.Length ?? 0) > (right?.Length ?? 0) ? left :
        (left?.Length ?? 0) < (right?.Length ?? 0) ? right :
        StringComparer.Ordinal.Compare(left, right) >= 0 ? left : right;
}
