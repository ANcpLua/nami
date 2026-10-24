namespace Nami.Collector.Ingestion;

internal static class AttributeStabilityWire
{
    internal static string Name(string key) => AttributeStability.Of(key) switch
    {
        Stability.Stable => "stable",
        Stability.Development => "development",
        Stability.Deprecated => "deprecated",
        Stability.Alpha => "alpha",
        Stability.Beta => "beta",
        Stability.ReleaseCandidate => "release_candidate",
        _ => "unknown"
    };
}
