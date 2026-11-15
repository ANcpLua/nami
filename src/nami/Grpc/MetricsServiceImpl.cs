using Microsoft.Extensions.Options;
using Nami.ApiKeys;
using Nami.Collector.Storage;
using OpenTelemetry.Proto.Collector.Metrics.V1;

namespace Nami.Grpc;

internal sealed class MetricsServiceImpl(INamiStore store, IOptions<OtlpApiKeyOptions>? options = null)
{
    public Task<ExportMetricsServiceResponse> Export(
        ExportMetricsServiceRequest request,
        ServerCallContext context)
    {
        return GrpcExport.ExecuteAsync(async () =>
        {
            var batch = OtlpConverter.ConvertMetrics(request);
            var write = MetricStorageMapper.ToStorageRows(
                batch, AuthenticatedProject.ForGrpcIngest(context, options?.Value));

            if (write.Observations.Count > 0)
            {
                await store
                    .InsertMetricsAsync(write.Series, write.Observations, context.CancellationToken)
                    .ConfigureAwait(false);
            }

            return OtlpExportAck.Create(batch);
        }, "metric");
    }
}
