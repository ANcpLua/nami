using Microsoft.Extensions.Options;
using Nami.ApiKeys;
using Nami.Collector.Storage;
using OpenTelemetry.Proto.Collector.Logs.V1;

namespace Nami.Grpc;

internal sealed class LogsServiceImpl(
    INamiStore store,
    TimeProvider timeProvider,
    IOptions<OtlpApiKeyOptions>? options = null)
{
    public Task<ExportLogsServiceResponse> Export(
        ExportLogsServiceRequest request,
        ServerCallContext context)
    {
        return GrpcExport.ExecuteAsync(async () =>
        {
            var logBatch = OtlpConverter.ConvertLogs(request, timeProvider);
            var logs = IngestionStorageMapper.ToLogStorageRows(
                logBatch, AuthenticatedProject.ForGrpcIngest(context, options?.Value));

            if (logs.Count <= 0) return OtlpExportAck.Create(logBatch);

            await store.InsertLogsAsync(logs, context.CancellationToken).ConfigureAwait(false);
            return OtlpExportAck.Create(logBatch);
        }, "log");
    }
}
