namespace Nami.Collector.Storage;

/// <summary>
///     One row per distinct resource, scope, metric shape and attribute set — the series index.
///     It answers "which metrics exist" and "which series carry attribute X" without touching
///     the point table, which is the pair of questions an agent asks before it asks for values.
/// </summary>
internal sealed partial record MetricSeriesRow
{
    [JsonIgnore] internal IReadOnlyList<AttributeRename> AttributeRenames { get; init; } = [];
}

internal readonly record struct MetricRetentionResult(int Points, int Series);
