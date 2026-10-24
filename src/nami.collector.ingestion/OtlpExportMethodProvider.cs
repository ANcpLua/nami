using System;
using System.Diagnostics.CodeAnalysis;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.AspNetCore.Server.Model;

namespace Nami.Collector.Ingestion;

/// <summary>
///     Binds an OTLP export service's unary <c>Export</c> method from the generated service
///     descriptor, decoding the request the way <see cref="OtlpPayloadParser" /> decodes OTLP/HTTP
///     protobuf: strictly, and with U+FFFD for an invalid UTF-8 sequence (OTLP 1.11.1). The
///     Grpc.Tools service base binds the generated marshallers instead, and those reject such a
///     request before the service runs.
/// </summary>
internal sealed class OtlpExportMethodProvider<
    [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.NonPublicMethods)]
    TService,
    TRequest,
    TResponse>(
    ServiceDescriptor service,
    MessageParser<TRequest> parser,
    UnaryServerMethod<TService, TRequest, TResponse> export)
    : IServiceMethodProvider<TService>
    where TService : class
    where TRequest : class, IMessage<TRequest>
    where TResponse : class, IMessage<TResponse>, new()
{
    private const string ExportMethodName = "Export";

    public void OnServiceMethodDiscovery(ServiceMethodProviderContext<TService> context)
    {
        var method = service.FindMethodByName(ExportMethodName)
                     ?? throw new InvalidOperationException(
                         $"{service.FullName} declares no {ExportMethodName} method.");
        if (method.IsClientStreaming || method.IsServerStreaming)
            throw new InvalidOperationException($"{service.FullName}/{method.Name} is not unary.");

        var descriptor = method.InputType;
        context.AddUnaryMethod(
            new Method<TRequest, TResponse>(
                MethodType.Unary,
                service.FullName,
                method.Name,
                Marshallers.Create<TRequest>(
                    static (message, serialization) => Serialize(message, serialization),
                    deserialization =>
                        OtlpPayloadParser.ParseProtobuf(parser, descriptor,
                            deserialization.PayloadAsReadOnlySequence())),
                Marshallers.Create<TResponse>(
                    static (message, serialization) => Serialize(message, serialization),
                    static deserialization =>
                    {
                        var message = new TResponse();
                        message.MergeFrom(deserialization.PayloadAsReadOnlySequence());
                        return message;
                    })),
            [],
            export);
    }

    private static void Serialize(IMessage message, SerializationContext context)
    {
        context.SetPayloadLength(message.CalculateSize());
        message.WriteTo(context.GetBufferWriter());
        context.Complete();
    }
}
