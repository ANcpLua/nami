using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Model;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Nami.Grpc;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Nami.Hosting;

internal static class CollectorCoreExtensions
{
    public static void AddNamiCollectorCore(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.AddOptions<CollectorPortOptions>()
            .Bind(config)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CollectorPortOptions>, CollectorPortOptionsValidator>();
        services.AddSingleton<IValidateOptions<CollectorPortOptions>, CollectorPortOptionsRules>();
        services.AddOptions<CollectorStorageOptions>()
            .Bind(config)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CollectorStorageOptions>, CollectorStorageOptionsValidator>();
        // The size bounds of ingest: one instance for the request readers, the bounded parse and the converter.
        services.AddOptions<IngestLimits>()
            .Bind(config)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<IngestLimits>, IngestLimitsValidator>();
        services.TryAddSingleton(TimeProvider.System);

        services.AddGrpc(static options =>
        {
            options.ResponseCompressionLevel = CompressionLevel.Optimal;
            options.ResponseCompressionAlgorithm = "gzip";
            // The gRPC half of the API-key boundary; the HTTP half is CollectorApiKeyMiddleware.
            options.Interceptors.Add<OtlpApiKeyInterceptor>();
        });
        services.AddSingleton<OtlpApiKeyInterceptor>();
        // One request bound for both transports (engine audit A15: gRPC kept the library's 4 MiB beside HTTP's
        // 16 MiB). gRPC has a single number for a message, on the wire and decompressed, so it gets the smaller of
        // the two: nothing over either bound is accepted. A longer message is refused with RESOURCE_EXHAUSTED when
        // its length prefix arrives, and a compressed one as soon as it has inflated one byte past the bound.
        services.AddOptions<GrpcServiceOptions>()
            .Configure<IOptions<IngestLimits>>(static (grpc, limits) => grpc.MaxReceiveMessageSize =
                Math.Min(limits.Value.MaxRequestWireBytes, limits.Value.MaxRequestDecompressedBytes));

        // The three OTLP export services bind through OtlpExportMethodProvider, whose request
        // marshaller decodes an invalid UTF-8 sequence as U+FFFD (OTLP 1.11.1). AddGrpc's binder
        // stays registered, because every other gRPC service in the process gets its methods from
        // it; for these three it finds OtlpExportService's empty bind method and adds nothing.
        services.AddSingleton<IServiceMethodProvider<TraceServiceImpl>>(
            new OtlpExportMethodProvider<TraceServiceImpl, ExportTraceServiceRequest, ExportTraceServiceResponse>(
                TraceServiceReflection.Descriptor.Services[0],
                ExportTraceServiceRequest.Parser,
                static (service, request, context) => service.Export(request, context)));
        services.AddSingleton<IServiceMethodProvider<LogsServiceImpl>>(
            new OtlpExportMethodProvider<LogsServiceImpl, ExportLogsServiceRequest, ExportLogsServiceResponse>(
                LogsServiceReflection.Descriptor.Services[0],
                ExportLogsServiceRequest.Parser,
                static (service, request, context) => service.Export(request, context)));
        services.AddSingleton<IServiceMethodProvider<MetricsServiceImpl>>(
            new OtlpExportMethodProvider<MetricsServiceImpl, ExportMetricsServiceRequest, ExportMetricsServiceResponse>(
                MetricsServiceReflection.Descriptor.Services[0],
                ExportMetricsServiceRequest.Parser,
                static (service, request, context) => service.Export(request, context)));
    }
}
