using Microsoft.Extensions.Options;
using OpenTelemetry.Proto.Collector.Trace.V1;
using Nami.Collector.ApiKeys;
using Nami.Collector.Storage;

namespace Nami.Collector.Grpc;

internal sealed class TraceServiceImpl(INamiStore store, IOptions<OtlpApiKeyOptions>? options = null)
{
    public Task<ExportTraceServiceResponse> Export(
        ExportTraceServiceRequest request,
        ServerCallContext context) =>
        GrpcExport.ExecuteAsync(async () =>
        {
            var traceBatch = OtlpConverter.ConvertTraceRequest(request);
            var spans = IngestionStorageMapper.ToSpanStorageRows(
                traceBatch, AuthenticatedProject.ForGrpcIngest(context, options?.Value));

            if (spans.Count <= 0) return OtlpExportAck.Create(traceBatch);

            await store.EnqueueAsync(new SpanBatch(spans), context.CancellationToken).ConfigureAwait(false);

            return OtlpExportAck.Create(traceBatch);
        }, "trace");
}
