using System.Collections.Concurrent;
using Grpc.AspNetCore.Server;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Nami.Grpc;
using Nami.Hosting;

namespace Nami.UnitTests.Hosting;

/// <summary>
///     Which gRPC methods a host that carries the collector binds. The collector binds its three OTLP export
///     services itself; every other service in the process still depends on the binder that AddGrpc registers.
/// </summary>
[TestClass]
[TestCategory("Fast")]
public sealed class GrpcMethodBindingTests
{
    [TestMethod]
    public async Task Should_BindAServiceWithItsOwnBindMethod_When_ItIsMappedBesideTheCollector()
    {
        using var warnings = new WarningLog();
        await using var app = BuildCollectorHost(warnings);

        _ = app.MapGrpcService<ProbeService>();

        Assert.AreSequenceEqual(["/nami.tests.Probe/Ping"], BoundMethods(app, typeof(ProbeService)));
    }

    [TestMethod]
    public async Task Should_BindEachOtlpExportOnceAndFindItsBindMethod_When_TheCollectorServicesAreMapped()
    {
        using var warnings = new WarningLog();
        await using var app = BuildCollectorHost(warnings);

        _ = app.MapGrpcService<TraceServiceImpl>();
        _ = app.MapGrpcService<LogsServiceImpl>();
        _ = app.MapGrpcService<MetricsServiceImpl>();

        Assert.AreSequenceEqual(["/opentelemetry.proto.collector.trace.v1.TraceService/Export"],
            BoundMethods(app, typeof(TraceServiceImpl)));
        Assert.AreSequenceEqual(["/opentelemetry.proto.collector.logs.v1.LogsService/Export"],
            BoundMethods(app, typeof(LogsServiceImpl)));
        Assert.AreSequenceEqual(["/opentelemetry.proto.collector.metrics.v1.MetricsService/Export"],
            BoundMethods(app, typeof(MetricsServiceImpl)));
        Assert.AreSequenceEqual([], warnings.Entries.ToArray());
    }

    private static WebApplication BuildCollectorHost(WarningLog warnings)
    {
        var builder = WebApplication.CreateSlimBuilder();
        _ = builder.Logging.ClearProviders();
        _ = builder.Logging.AddProvider(warnings);
        // The binder reports a service without a bind method at Debug.
        _ = builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.Services.AddNamiCollectorCore(builder.Configuration);
        return builder.Build();
    }

    private static string[] BoundMethods(WebApplication app, Type service)
    {
        return
        [
            .. ((IEndpointRouteBuilder)app).DataSources
                .SelectMany(static source => source.Endpoints)
                .Select(static endpoint => endpoint.Metadata.GetMetadata<GrpcMethodMetadata>())
                .Where(metadata => metadata?.ServiceType == service)
                .Select(static metadata => metadata!.Method.FullName)
                .Order(StringComparer.Ordinal)
        ];
    }

    private sealed class WarningLog : ILoggerProvider
    {
        public ConcurrentQueue<string> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName)
        {
            return new Logger(categoryName, Entries);
        }

        public void Dispose()
        {
        }

        private sealed class Logger(string category, ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return logLevel >= LogLevel.Debug;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Warning || eventId.Name == "BindMethodNotFound")
                    entries.Enqueue($"{logLevel} {eventId.Name} {category}: {formatter(state, exception)}");
            }
        }
    }
}

/// <summary>A service shaped like Grpc.Tools output: an attributed base type whose bind method registers its methods.</summary>
[BindServiceMethod(typeof(ProbeBinding), nameof(ProbeBinding.BindService))]
internal abstract class ProbeServiceBase
{
    public virtual Task<byte[]> Ping(byte[] request, ServerCallContext context)
    {
        throw new RpcException(new Status(StatusCode.Unimplemented, ""));
    }
}

internal static class ProbeBinding
{
    private static readonly Marshaller<byte[]> s_bytes = Marshallers.Create<byte[]>(
        static (bytes, context) => context.Complete(bytes), static context => context.PayloadAsNewBuffer());

    private static readonly Method<byte[], byte[]> s_ping =
        new(MethodType.Unary, "nami.tests.Probe", "Ping", s_bytes, s_bytes);

    public static void BindService(ServiceBinderBase binder, ProbeServiceBase? service)
    {
        binder.AddMethod(s_ping, service is null ? null : new UnaryServerMethod<byte[], byte[]>(service.Ping));
    }
}

internal sealed class ProbeService : ProbeServiceBase
{
    public override Task<byte[]> Ping(byte[] request, ServerCallContext context)
    {
        return Task.FromResult(request);
    }
}
