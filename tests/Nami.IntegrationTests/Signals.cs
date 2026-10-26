using Nami.Tests.Wire.OpenTelemetry.Proto.Collector.Logs.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Collector.Metrics.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Collector.Trace.V1;

namespace Nami.IntegrationTests;

/// <summary>OTLP signal names that select the receiver route and the request shape.</summary>
internal static class Signals
{
    public const string Traces = "traces";
    public const string Metrics = "metrics";
    public const string Logs = "logs";

    /// <summary>The rejected count and message of a signal's export response, or null without a partial success.</summary>
    public static (long Rejected, string Message)? PartialSuccess(string signal, byte[] response)
    {
        return signal switch
        {
            Traces => ExportTraceServiceResponse.Parser.ParseFrom(response).PartialSuccess is { } traces
                ? (traces.RejectedSpans, traces.ErrorMessage)
                : null,
            Logs => ExportLogsServiceResponse.Parser.ParseFrom(response).PartialSuccess is { } logs
                ? (logs.RejectedLogRecords, logs.ErrorMessage)
                : null,
            _ => ExportMetricsServiceResponse.Parser.ParseFrom(response).PartialSuccess is { } metrics
                ? (metrics.RejectedDataPoints, metrics.ErrorMessage)
                : null
        };
    }
}
