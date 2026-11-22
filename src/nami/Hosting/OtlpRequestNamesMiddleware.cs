using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Nami.ApiKeys;
using Nami.Collector.Ingestion.Configuration;
using HttpContext = Microsoft.AspNetCore.Http.HttpContext;
using RequestDelegate = Microsoft.AspNetCore.Http.RequestDelegate;

namespace Nami.Hosting;

/// <summary>
///     DNS-rebinding protection in front of the OTLP/HTTP endpoints, with the rules of /mcp
///     (src/nami.mcp/src/http-security.ts). A page whose name was rebound to this machine posts an export like a
///     local exporter does, but under its own name: 403 unless the Host is one of the listener's names and the
///     Origin, if there is one, is an origin of this machine, the public one, or one the operator allows for CORS.
///     <para>
///         The Host is judged only where no key is asked (Unsecured mode). With keys the key admits an export
///         under whatever name a producer reaches nami by, another container's or a platform's private one, and a
///         page holds no key. The Origin is judged in both modes: an exporter sends none. No Host is excepted
///         here: the health probe a platform sends under its own name asks /health on the MCP port.
///     </para>
///     <para>
///         OTLP/gRPC is left out. Its listener speaks HTTP/2 without TLS, which no browser does, and a gRPC path
///         on the HTTP/1 listener is refused by the gRPC server before any export is read.
///     </para>
/// </summary>
internal sealed class OtlpRequestNamesMiddleware
{
    /// <summary>The names this machine has for itself, as a Host header and a URL carry them.</summary>
    private static readonly string[] s_loopbackNames = ["localhost", "127.0.0.1", "[::1]"];

    private readonly HashSet<string> _allowedOrigins;
    private readonly bool _judgesHost;
    private readonly HashSet<string> _hosts = new(s_loopbackNames, StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loopback = new(s_loopbackNames, StringComparer.OrdinalIgnoreCase);
    private readonly RequestDelegate _next;
    private readonly Uri? _publicOrigin;

    public OtlpRequestNamesMiddleware(RequestDelegate next, IConfiguration configuration,
        IOptions<CollectorPortOptions> ports, IOptions<OtlpApiKeyOptions> keys, IOptions<OtlpCorsOptions> cors)
    {
        _next = next;
        _judgesHost = !keys.Value.IsApiKeyMode;
        _allowedOrigins = cors.Value.GetOrigins().ToHashSet(StringComparer.OrdinalIgnoreCase);
        // The address the listener is bound to is a name of it, unless it means every interface: a wildcard is
        // where a listener binds, never a name a client reached it by.
        var bound = ports.Value.ParseBindAddress();
        if (!IsWildcard(bound)) _hosts.Add(HostName(bound));
        _publicOrigin = PublicOrigin(configuration);
        if (_publicOrigin is not null) _hosts.Add(_publicOrigin.Host);
    }

    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (!OtlpConstants.IsOtlpPath(request.Path.Value ?? "")) return _next(context);
        // Host is compared by name, without its port: a proxy or a published container port may use any.
        if (_judgesHost && !_hosts.Contains(request.Host.Host)) return RefuseAsync(context, "Invalid Host.");
        return IsOriginAllowed(request.Headers.Origin) ? _next(context) : RefuseAsync(context, "Invalid Origin.");
    }

    private bool IsOriginAllowed(StringValues header)
    {
        // No Origin: not a browser's cross-origin request. Other clients do not send one.
        if (header.Count is 0 || (header.Count is 1 && string.IsNullOrEmpty(header[0]))) return true;
        if (header.Count is not 1) return false;
        var value = header[0]!;
        if (_allowedOrigins.Contains("*") || _allowedOrigins.Contains(value)) return true;
        // "null" and anything else that is no HTTP(S) URL is refused.
        if (!Uri.TryCreate(value, UriKind.Absolute, out var origin) ||
            (origin.Scheme != Uri.UriSchemeHttp && origin.Scheme != Uri.UriSchemeHttps))
            return false;
        // A page of this machine has no fixed port, so every HTTP(S) origin on a loopback name passes. Any other
        // origin has to be the public one in scheme, host and port.
        if (_loopback.Contains(origin.Host)) return true;
        return _publicOrigin is not null && Uri.Compare(origin, _publicOrigin, UriComponents.SchemeAndServer,
            UriFormat.UriEscaped, StringComparison.OrdinalIgnoreCase) == 0;
    }

    /// <summary>
    ///     The origin of NAMI_PUBLIC_URL, or of the public domain a platform names, as the MCP host reads them
    ///     (src/nami.mcp/src/main.ts), which also refuses to start on a value that is no origin. A wildcard address
    ///     is not one.
    /// </summary>
    private static Uri? PublicOrigin(IConfiguration configuration)
    {
        var configured = configuration[CollectorConfigurationKeys.PublicUrl];
        if (string.IsNullOrEmpty(configured))
        {
            var domain = configuration[CollectorConfigurationKeys.RailwayPublicDomain];
            if (string.IsNullOrEmpty(domain)) return null;
            configured = "https://" + domain;
        }

        if (!Uri.TryCreate(configured, UriKind.Absolute, out var origin) ||
            (origin.Scheme != Uri.UriSchemeHttp && origin.Scheme != Uri.UriSchemeHttps))
            return null;
        return IPAddress.TryParse(origin.DnsSafeHost, out var address) && IsWildcard(address) ? null : origin;
    }

    /// <summary>True for every interface: IPv4, IPv6 and IPv4 mapped into IPv6.</summary>
    private static bool IsWildcard(IPAddress address)
    {
        var plain = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        return plain.Equals(IPAddress.Any) || plain.Equals(IPAddress.IPv6Any);
    }

    private static string HostName(IPAddress address)
    {
        return address.AddressFamily == AddressFamily.InterNetworkV6 ? "[" + address + "]" : address.ToString();
    }

    /// <summary>403 with an OTLP Status in the encoding of the request; the message repeats nothing the sender wrote.</summary>
    private static Task RefuseAsync(HttpContext context, string message)
    {
        var encoding = OtlpPayloadParser.TryGetEncoding(context.Request.ContentType, out var requested)
            ? requested
            : OtlpPayloadEncoding.Json;
        return OtlpHttpResult.Failure(StatusCodes.Status403Forbidden, encoding, message).ExecuteAsync(context);
    }
}
