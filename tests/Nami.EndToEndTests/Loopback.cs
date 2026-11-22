using System.Net;
using System.Net.Sockets;

namespace Nami.EndToEndTests;

internal static class Loopback
{
    /// <summary>A loopback port that was free a moment ago; the caller hands it to a process that binds it.</summary>
    internal static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
