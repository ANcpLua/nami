using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Model;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using Nami.Collector.Analysis;
using Nami.Collector.Grpc;

namespace Nami.Collector.Hosting;

internal static class CollectorCoreExtensions
{
    public static IServiceCollection AddNamiCollectorCore(
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
        services.AddOptions<AnalysisOptions>()
            .Bind(config)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AnalysisOptions>, AnalysisOptionsRules>();
        services.AddSingleton<CollectorStreamCapacity>();

        // Preserve a host-supplied clock for observation times and analysis windows.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<AnalysisSigningKey>();

        services.ConfigureHttpJsonOptions(static options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, global::Nami.Api.Contracts.ContractTypes.Resolver);
        });

        services.AddGrpc(static options =>
        {
            options.ResponseCompressionLevel = CompressionLevel.Optimal;
            options.ResponseCompressionAlgorithm = "gzip";
            // The gRPC half of the API-key boundary; the HTTP half is CollectorApiKeyMiddleware.
            options.Interceptors.Add<OtlpApiKeyInterceptor>();
        });
        services.AddSingleton<OtlpApiKeyInterceptor>();

        // The three OTLP export services bind through OtlpExportMethodProvider, whose request
        // marshaller decodes an invalid UTF-8 sequence as U+FFFD (OTLP 1.11.1). AddGrpc's binder
        // looks for a Grpc.Tools service base, which these services no longer have; it would only
        // warn at start-up, so it goes.
        services.RemoveAll(typeof(IServiceMethodProvider<>));
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

        // grpc-dotnet decompresses a request into an unbounded buffer before any interceptor runs, so every
        // decompression provider is wrapped to stop at the receive limit. PostConfigure sees the final list,
        // including the defaults gRPC adds and any provider registered after this call.
        services.PostConfigure<GrpcServiceOptions>(static options =>
        {
            var limit = options.MaxReceiveMessageSize ?? BoundedDecompressionProvider.DefaultMaxReceiveMessageSize;
            for (var i = 0; i < options.CompressionProviders.Count; i++)
            {
                if (options.CompressionProviders[i] is not BoundedDecompressionProvider)
                    options.CompressionProviders[i] = new BoundedDecompressionProvider(options.CompressionProviders[i], limit);
            }
        });

        return services;
    }
}
