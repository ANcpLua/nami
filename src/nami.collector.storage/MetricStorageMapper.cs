using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nami.Collector.Ingestion.Primitives;

namespace Nami.Collector.Storage;

/// <summary>
///     Folds the flat OTLP data points the converter produces into the two-table storage shape:
///     one <see cref="MetricSeriesRow" /> per distinct stream and canonical observations
///     for every nonidentical producer record.
/// </summary>
internal static class MetricStorageMapper
{
    public static MetricWriteSet ToStorageRows(MetricIngestionBatch batch, string? authenticatedProjectId = null)
    {
        var series = new Dictionary<(string ProjectId, string SeriesId), MetricSeriesRow>();
        var observations =
            new Dictionary<(string ProjectId, string SeriesId, string ObservationId), MetricObservationRow>();

        foreach (var record in batch.Points)
        {
            var projectId = ProjectScope.ForIngest(record.ProjectIdHint, authenticatedProjectId);
            var attributesJson = PersistedAttributePolicy.SerializeAttributes(record.Attributes);
            var resourceJson = PersistedAttributePolicy.SerializeAttributes(record.ResourceAttributes);
            var resourceEntityRefsJson = StorageJsonSerializerContext.SerializeResourceEntityRefs(record.ResourceEntityRefs);
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
                AttributeRenames = NormalizedAttributes
                    .Collect([record.Attributes, record.ResourceAttributes, record.ScopeAttributes])
                    .Concat(record.InstrumentMetadata is NormalizedContractAttributes metadata ? metadata.Renames : [])
                    .Concat(record.Exemplars is NormalizedMetricExemplars exemplars ? exemplars.Renames : [])
                    .Distinct().ToArray(),
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
                    AttributeRenames = existing.AttributeRenames.Concat(row.AttributeRenames).Distinct().ToArray(),
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
    ///     Resource, scope and name identify the catalog instrument. Point attributes and
    ///     conflicting identifying properties distinguish streams within that instrument.
    ///     https://github.com/open-telemetry/opentelemetry-specification/blob/v1.61.0/specification/metrics/data-model.md#opentelemetry-protocol-data-model
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
        builder.AppendSegment(metricName, true);
        builder.AppendSegment(serviceName, true);
        builder.AppendSegment(resourceJson, true);
        builder.AppendSegment(scopeName, true);
        builder.AppendSegment(scopeVersion, true);
        builder.AppendSegment(scopeSchemaUrl, true);
        builder.AppendSegment(scopeAttributesJson, true);
        builder.AppendSegment(resourceSchemaUrl, true);
        if (resourceIdentity is not null) builder.AppendSegment(resourceIdentity, true);
        if (scopeIdentity is not null) builder.AppendSegment(scopeIdentity, true);
        return "mi_" +
               Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())).AsSpan(0, 16));
    }

    internal static string InstrumentId(MetricSeriesRow row) => ComputeInstrumentId(
        row.MetricName, row.ServiceName, row.ResourceJson, row.ScopeName, row.ScopeVersion,
        row.ScopeSchemaUrl, row.ScopeAttributesJson, row.ResourceSchemaUrl);

    internal static string SeriesId(MetricSeriesRow row) => ComputeSeriesId(
        row.InstrumentId ?? InstrumentId(row), row.AttributesJson,
        (MetricKind)row.MetricKind, (MetricTemporality)row.Temporality, row.IsMonotonic != 0, row.Unit);

    private static string ComputeSeriesId(string instrumentId, string? attributesJson,
        MetricKind kind,
        MetricTemporality temporality,
        bool isMonotonic,
        string? unit, string? attributesIdentity = null)
    {
        var builder = new StringBuilder(512);
        builder.Append("metric_series\n");
        builder.AppendSegment(instrumentId, true);
        builder.AppendSegment(attributesJson, true);
        builder.AppendSegment(((byte)kind).ToString(CultureInfo.InvariantCulture), true);
        builder.AppendSegment(((byte)temporality).ToString(CultureInfo.InvariantCulture), true);
        builder.AppendSegment(isMonotonic ? "1" : "0", true);
        builder.AppendSegment(unit, true);
        if (attributesIdentity is not null) builder.AppendSegment(attributesIdentity, true);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return "ms_" + Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }

    // Description is non-identifying; retain the longer value on re-registration.
    // https://github.com/open-telemetry/opentelemetry-specification/blob/v1.61.0/specification/metrics/data-model.md#opentelemetry-protocol-data-model-producer-recommendations
    private static string? LongerDescription(string? left, string? right) =>
        (left?.Length ?? 0) > (right?.Length ?? 0) ? left :
        (left?.Length ?? 0) < (right?.Length ?? 0) ? right :
        StringComparer.Ordinal.Compare(left, right) >= 0 ? left : right;

    internal readonly record struct MetricWriteSet(
        IReadOnlyList<MetricSeriesRow> Series,
        IReadOnlyList<MetricObservationRow> Observations);
}
