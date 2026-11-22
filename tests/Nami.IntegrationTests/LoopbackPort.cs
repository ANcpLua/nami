using System.Net;
using System.Net.Sockets;

namespace Nami.IntegrationTests;

/// <summary>Loopback ports for collectors the tests start in this process.</summary>
internal static class LoopbackPort
{
    /// <summary>A port the operating system had free on loopback a moment ago; the listener that found it is closed.</summary>
    internal static int Free()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
