using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Nami.Collector.Hosting;

// Second half of the two-phase self-export validation: the Nami.Run composition layer rejects
// self-referential wiring when the graph is built, and this guard re-validates the RESOLVED
// environment at collector startup — catching hand-set or leaked endpoints the composition
// layer never saw. Exporting to our own OTLP receiver would re-ingest every ingest (a feedback
// amplifier), so an endpoint that resolves to this machine on any of our own ports is fatal,
// not a warning. The check goes beyond the literal URL string: loopback aliases, the local
// host name, and DNS/interface resolution all count as "self".
internal static class CollectorSelfExportGuard
{
    private static readonly string[] s_loopbackHosts = ["localhost", "127.0.0.1", "::1", "[::1]", "0.0.0.0"];

    public static void ThrowIfSelfExporting(IConfiguration config, CollectorPortOptions ports)
    {
        var endpoint = config["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (string.IsNullOrWhiteSpace(endpoint)) return;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) return;

        // Port first: an endpoint on a foreign port can never be our receiver, whatever its host.
        if (uri.Port != ports.OtlpHttp && uri.Port != ports.Grpc && uri.Port != ports.Http) return;

        if (IsSelfHost(uri.Host))
        {
            throw new InvalidOperationException(
                $"OTEL_EXPORTER_OTLP_ENDPOINT ({endpoint}) points at this collector's own port " +
                $"(api {ports.Http}, otlp-http {ports.OtlpHttp}, grpc {ports.Grpc}). Exporting self-telemetry " +
                "into our own ingest pipeline would feedback-amplify; export to a separate diagnostics " +
                "collector instead (see Nami.Run's ExportToDedicatedCollector).");
        }
    }

    private static bool IsSelfHost(string host)
    {
        if (s_loopbackHosts.Contains(host, StringComparer.OrdinalIgnoreCase)) return true;

        // Canonical-host resolution, best effort: hostname aliases (myhost, myhost.local) and
        // LAN addresses of this machine's own interfaces also count as "self". Resolution
        // failures fall through — the guard must never block a legitimate remote endpoint
        // just because DNS is down.
        try
        {
            if (string.Equals(host, Dns.GetHostName(), StringComparison.OrdinalIgnoreCase)) return true;

            var resolved = IPAddress.TryParse(host.Trim('[', ']'), out var literal)
                ? [literal]
                : Dns.GetHostAddresses(host);

            if (resolved.Any(IPAddress.IsLoopback)) return true;

            var localAddresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(static nic => nic.OperationalStatus == OperationalStatus.Up)
                .SelectMany(static nic => nic.GetIPProperties().UnicastAddresses)
                .Select(static ua => ua.Address)
                .ToHashSet();

            return resolved.Any(localAddresses.Contains);
        }
        catch (SocketException)
        {
            return false;
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }
}
