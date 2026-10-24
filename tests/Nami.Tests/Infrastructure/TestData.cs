using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nami.Collector.Ingestion;
using Nami.Collector.Storage;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Nami.Tests.Infrastructure;

internal static class TestData
{
    internal static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-10-24T12:00:00Z", CultureInfo.InvariantCulture);

    internal static string HostBinary => Path.Combine(ProcessRunner.RepositoryRoot, "artifacts", "publish",
        OperatingSystem.IsWindows() ? "nami.exe" : "nami");

    internal static ExportTraceServiceRequest ParseTraces(string json)
    {
        return ExportTraceServiceRequest.Parser.ParseJson(ProtobufJson(json));
    }

    internal static string ProtobufJson(string json)
    {
        var node = JsonNode.Parse(json)!;

        void ConvertIds(JsonNode? value)
        {
            if (value is JsonObject obj)
            {
                foreach (var pair in obj.ToArray())
                {
                    if (pair.Key is "traceId" or "spanId" or "parentSpanId" && pair.Value is JsonValue)
                        obj[pair.Key] = Convert.ToBase64String(Convert.FromHexString(pair.Value.GetValue<string>()));
                    else ConvertIds(pair.Value);
                }
            }
            else if (value is JsonArray array)
                foreach (var item in array)
                    ConvertIds(item);
        }

        ConvertIds(node);
        return node.ToJsonString();
    }

    internal static ulong Nanos(DateTimeOffset value)
    {
        return checked((ulong)((value.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) * 100));
    }

    internal static async Task SeedTraces(DuckDbStore store, string json, CancellationToken token)
    {
        var batch = OtlpConverter.ConvertTraceRequest(ParseTraces(json));
        await store.EnqueueAsync(new SpanBatch(IngestionStorageMapper.ToSpanStorageRows(batch)), token);
    }

    internal static async Task SeedLogs(DuckDbStore store, string json, TimeProvider clock, CancellationToken token)
    {
        var batch = OtlpConverter.ConvertLogs(ExportLogsServiceRequest.Parser.ParseJson(ProtobufJson(json)), clock);
        await store.InsertLogsAsync(IngestionStorageMapper.ToLogStorageRows(batch), token);
    }

    internal static async Task SeedMetrics(DuckDbStore store, string json, CancellationToken token)
    {
        var batch = OtlpConverter.ConvertMetrics(ExportMetricsServiceRequest.Parser.ParseJson(json));
        var rows = MetricStorageMapper.ToStorageRows(batch);
        await store.InsertMetricsAsync(rows.Series, rows.Observations, token);
    }
}
