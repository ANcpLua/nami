using Nami.Collector.Ingestion;
using Nami.Collector.Storage;
using Nami.Tests.Infrastructure;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;

namespace Nami.IntegrationTests;

internal static class StorageTestData
{
    internal static async Task SeedTraces(DuckDbStore store, string json, CancellationToken token)
    {
        var batch = OtlpConverter.ConvertTraceRequest(TestData.ParseTraces(json));
        await store.EnqueueAsync(new SpanBatch(IngestionStorageMapper.ToSpanStorageRows(batch)), token);
    }

    internal static async Task SeedLogs(DuckDbStore store, string json, TimeProvider clock, CancellationToken token)
    {
        var batch = OtlpConverter.ConvertLogs(ExportLogsServiceRequest.Parser.ParseJson(TestData.ProtobufJson(json)), clock);
        await store.InsertLogsAsync(IngestionStorageMapper.ToLogStorageRows(batch), token);
    }

    internal static async Task SeedMetrics(DuckDbStore store, string json, CancellationToken token)
    {
        var batch = OtlpConverter.ConvertMetrics(ExportMetricsServiceRequest.Parser.ParseJson(json));
        var rows = MetricStorageMapper.ToStorageRows(batch);
        await store.InsertMetricsAsync(rows.Series, rows.Observations, token);
    }
}
