using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace Nami.Collector.Hosting;

internal static class CollectorKestrelExtensions
{
    public static IServiceCollection ConfigureNamiCollectorKestrel(this IServiceCollection services)
    {
        services.AddOptions<KestrelServerOptions>()
            .Configure<IOptions<CollectorPortOptions>>(static (options, configured) =>
            {
                var ports = configured.Value;
                var address = ports.ParseBindAddress();
                options.Listen(address, ports.Http, static lo => lo.Protocols = HttpProtocols.Http1AndHttp2);

                if (ports.OtlpHttp > 0 && ports.OtlpHttp != ports.Http)
                    options.Listen(address, ports.OtlpHttp, static lo => lo.Protocols = HttpProtocols.Http1AndHttp2);

                if (ports.Grpc > 0)
                    options.Listen(address, ports.Grpc, static lo => lo.Protocols = HttpProtocols.Http2);
            });

        return services;
    }
}
