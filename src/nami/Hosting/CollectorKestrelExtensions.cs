using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Nami.Hosting;

internal static class CollectorKestrelExtensions
{
    public static void ConfigureNamiCollectorKestrel(this IServiceCollection services)
    {
        services.AddOptions<KestrelServerOptions>()
            .Configure<IOptions<CollectorPortOptions>>(static (options, configured) =>
            {
                var ports = configured.Value;
                var address = ports.ParseBindAddress();
                if (ports.OtlpHttp > 0)
                    options.Listen(address, ports.OtlpHttp, static lo => lo.Protocols = HttpProtocols.Http1);

                if (ports.Grpc > 0)
                    options.Listen(address, ports.Grpc, static lo => lo.Protocols = HttpProtocols.Http2);

                // A process-local ingest listener remains reachable for self-telemetry when the
                // public listener is bound to one particular NIC rather than a wildcard address.
                if (ports.OtlpHttp > 0 && !IPAddress.IsLoopback(address) && !address.Equals(IPAddress.Any) &&
                    !address.Equals(IPAddress.IPv6Any))
                {
                    var loopback = address.AddressFamily == AddressFamily.InterNetworkV6
                        ? IPAddress.IPv6Loopback
                        : IPAddress.Loopback;
                    options.Listen(loopback, ports.OtlpHttp,
                        static lo => lo.Protocols = HttpProtocols.Http1);
                }
            });

    }
}
