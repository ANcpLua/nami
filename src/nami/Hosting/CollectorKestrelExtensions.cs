using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace Nami.Hosting;

internal static class CollectorKestrelExtensions
{
    private const long GrpcMessagePrefixBytes = 5;

    public static void ConfigureNamiCollectorKestrel(this IServiceCollection services)
    {
        services.AddSingleton<CollectorListeners>();
        services.AddOptions<KestrelServerOptions>()
            .Configure<IOptions<CollectorPortOptions>, IOptions<IngestLimits>, CollectorListeners>(static (options,
                configured, limits, listeners) =>
            {
                // Kestrel's own request-body bound (30 MB unless set) stays as a backstop, but never below
                // nami's: a request nami accepts must reach it. A gRPC message has a five-byte prefix.
                var largestRequest = limits.Value.MaxRequestWireBytes + GrpcMessagePrefixBytes;
                if (options.Limits.MaxRequestBodySize < largestRequest)
                    options.Limits.MaxRequestBodySize = largestRequest;

                var ports = configured.Value;
                var address = ports.ParseBindAddress();
                // Port 0 lets the operating system choose; CollectorListeners then holds the bound endpoints.
                options.Listen(address, ports.OtlpHttp, lo =>
                {
                    lo.Protocols = HttpProtocols.Http1;
                    listeners.OtlpHttp = lo;
                });
                options.Listen(address, ports.Grpc, lo =>
                {
                    lo.Protocols = HttpProtocols.Http2;
                    listeners.Grpc = lo;
                });

                // A process-local ingest listener remains reachable for self-telemetry when the
                // public listener is bound to one particular NIC rather than a wildcard address. With an
                // operating-system-chosen port there is no self-telemetry export, so no second listener either.
                if (ports.OtlpHttp <= 0 || IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) ||
                    address.Equals(IPAddress.IPv6Any)) return;

                var loopback = address.AddressFamily == AddressFamily.InterNetworkV6
                    ? IPAddress.IPv6Loopback
                    : IPAddress.Loopback;
                options.Listen(loopback, ports.OtlpHttp,
                    static lo => lo.Protocols = HttpProtocols.Http1);
            });
    }
}
