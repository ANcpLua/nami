using Nami.Telemetry.SemanticConventions.Names;

namespace Nami.Collector.Telemetry;

internal static class NamiTelemetry
{
    // The registry's own name for this scope, so the Meter and the collector's ingest catalog
    // cannot disagree about what the collector calls itself. The collector starts no spans of its
    // own; its traces are the ones AddNami's instrumentation records.
    public const string ServiceName = NamiTelemetryNames.Scopes.NamiCollector;
}

internal static class NamiLatencyNames
{
    internal static class Checkpoints
    {
        public const string DbQuery = "collector.db_query";
        public const string SpanIngest = "collector.span_ingest";
        public const string SpanStore = "collector.span_store";
        public const string SessionQuery = "collector.session_query";
        public const string GenAiExtract = "collector.genai_extract";
    }

    internal static class Measures
    {
        public const string IngestionDuration = "collector.ingestion_duration";
        public const string QueryDuration = "collector.query_duration";
        public const string StorageDuration = "collector.storage_duration";
    }

    internal static class Tags
    {
        public const string SpanCount = "collector.span_count";
    }
}
