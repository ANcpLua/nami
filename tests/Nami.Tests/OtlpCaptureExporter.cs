using JetBrains.Annotations;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Nami.Tests;

/// <summary>
///     Adds an OpenTelemetry SDK OTLP exporter that sends gzip-compressed records one at a time to a test receiver: the
///     signal's <c>v1/{signal}</c> route over OTLP/HTTP protobuf, or the receiver address itself over gRPC.
/// </summary>
[PublicAPI]
public static class OtlpCaptureExporter
{
    /// <param name="builder">The SDK configuration receiving the exporter.</param>
    /// <param name="receiver">The receiver's HTTP endpoint, or its gRPC endpoint for <see cref="OtlpTransport.Grpc" />.</param>
    /// <param name="transport">The OTLP transport the exporter uses.</param>
    /// <param name="configure">Further exporter settings, applied last.</param>
    public static TracerProviderBuilder AddOtlpCapture(this TracerProviderBuilder builder, Uri receiver,
        OtlpTransport transport = OtlpTransport.HttpProtobuf,
        Action<OtlpExporterOptions>? configure = null)
    {
        return builder.AddOtlpExporter(options =>
        {
            options.Endpoint = Endpoint(receiver, "traces", transport);
            Capture(options, transport);
            configure?.Invoke(options);
        });
    }

    /// <param name="builder">The SDK configuration receiving the exporter.</param>
    /// <param name="receiver">The receiver's HTTP endpoint, or its gRPC endpoint for <see cref="OtlpTransport.Grpc" />.</param>
    /// <param name="transport">The OTLP transport the exporter uses.</param>
    public static MeterProviderBuilder AddOtlpCapture(this MeterProviderBuilder builder, Uri receiver,
        OtlpTransport transport = OtlpTransport.HttpProtobuf)
    {
        return builder.AddOtlpExporter(options =>
        {
            options.Endpoint = Endpoint(receiver, "metrics", transport);
            Capture(options, transport);
        });
    }

    /// <param name="logging">The SDK configuration receiving the exporter.</param>
    /// <param name="receiver">The receiver's HTTP endpoint, or its gRPC endpoint for <see cref="OtlpTransport.Grpc" />.</param>
    /// <param name="transport">The OTLP transport the exporter uses.</param>
    public static OpenTelemetryLoggerOptions AddOtlpCapture(this OpenTelemetryLoggerOptions logging, Uri receiver,
        OtlpTransport transport = OtlpTransport.HttpProtobuf)
    {
        return logging.AddOtlpExporter(options =>
        {
            options.Endpoint = Endpoint(receiver, "logs", transport);
            Capture(options, transport);
        });
    }

    private static Uri Endpoint(Uri receiver, string signal, OtlpTransport transport)
    {
        ArgumentNullException.ThrowIfNull(receiver);
        return transport == OtlpTransport.Grpc ? receiver : new Uri(receiver, "v1/" + signal);
    }

    private static void Capture(OtlpExporterOptions options, OtlpTransport transport)
    {
        options.Protocol = transport == OtlpTransport.Grpc ? OtlpExportProtocol.Grpc : OtlpExportProtocol.HttpProtobuf;
        options.Compression = OtlpExportCompression.GZip;
        options.ExportProcessorType = ExportProcessorType.Simple;
    }
}
