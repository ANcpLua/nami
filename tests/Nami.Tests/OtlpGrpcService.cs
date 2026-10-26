using Google.Protobuf.Reflection;
using Grpc.AspNetCore.Server.Model;
using Grpc.Core;
using Nami.Tests.Wire.OpenTelemetry.Proto.Collector.Logs.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Collector.Metrics.V1;
using Nami.Tests.Wire.OpenTelemetry.Proto.Collector.Trace.V1;

namespace Nami.Tests;

internal sealed class OtlpGrpcService(OtlpReceiver receiver)
{
    internal Task<byte[]> ExportAsync(string signal, byte[] payload, ServerCallContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(receiver.ReceiveGrpc(signal, payload));
    }
}

internal sealed class OtlpGrpcMethodProvider : IServiceMethodProvider<OtlpGrpcService>
{
    // Keep the original uncompressed protobuf bytes. A generated request marshaller would
    // decode before capture, and serializing it again would not reproduce the received wire.
    private static readonly Marshaller<byte[]> s_payload = Marshallers.Create<byte[]>(
        static (bytes, context) => context.Complete(bytes),
        static context => context.PayloadAsNewBuffer());

    public void OnServiceMethodDiscovery(ServiceMethodProviderContext<OtlpGrpcService> context)
    {
        Add(context, "traces", TraceServiceReflection.Descriptor.Services[0]);
        Add(context, "logs", LogsServiceReflection.Descriptor.Services[0]);
        Add(context, "metrics", MetricsServiceReflection.Descriptor.Services[0]);
    }

    private static void Add(ServiceMethodProviderContext<OtlpGrpcService> context, string signal,
        ServiceDescriptor service)
    {
        var method = service.FindMethodByName("Export")
                     ?? throw new InvalidOperationException($"{service.FullName} declares no Export method.");
        context.AddUnaryMethod(
            new Method<byte[], byte[]>(MethodType.Unary, service.FullName, method.Name, s_payload, s_payload),
            [], (instance, bytes, call) => instance.ExportAsync(signal, bytes, call));
    }
}
