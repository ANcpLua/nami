using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Nami.Grpc;

/// <summary>Both transports acknowledge the same accepted records and rejected-item diagnostics.</summary>
internal static class OtlpExportAck
{
    // OTLP permits a warning with zero rejected items; a diagnostic must not disappear
    // just because there was no countable point in an unsupported descriptor.
    // https://opentelemetry.io/docs/specs/otlp/#partial-success
    public static ExportTraceServiceResponse Create(TraceIngestionBatch batch)
    {
        return batch.RejectedSpans == 0 && string.IsNullOrEmpty(batch.RejectionMessage)
            ? new ExportTraceServiceResponse()
            : new ExportTraceServiceResponse
            {
                PartialSuccess = new ExportTracePartialSuccess
                {
                    RejectedSpans = batch.RejectedSpans,
                    ErrorMessage = batch.RejectionMessage ??
                                   throw new InvalidDataException("Rejected spans require diagnostics.")
                }
            };
    }

    public static ExportLogsServiceResponse Create(LogIngestionBatch batch)
    {
        return batch.RejectedLogRecords == 0 && string.IsNullOrEmpty(batch.RejectionMessage)
            ? new ExportLogsServiceResponse()
            : new ExportLogsServiceResponse
            {
                PartialSuccess = new ExportLogsPartialSuccess
                {
                    RejectedLogRecords = batch.RejectedLogRecords,
                    ErrorMessage = batch.RejectionMessage ??
                                   throw new InvalidDataException("Rejected logs require diagnostics.")
                }
            };
    }

    public static ExportMetricsServiceResponse Create(MetricIngestionBatch batch)
    {
        return batch.RejectedDataPoints == 0 && string.IsNullOrEmpty(batch.RejectionMessage)
            ? new ExportMetricsServiceResponse()
            : new ExportMetricsServiceResponse
            {
                PartialSuccess = new ExportMetricsPartialSuccess
                {
                    RejectedDataPoints = batch.RejectedDataPoints,
                    ErrorMessage = batch.RejectionMessage ??
                                   throw new InvalidDataException("Rejected metrics require diagnostics.")
                }
            };
    }
}
