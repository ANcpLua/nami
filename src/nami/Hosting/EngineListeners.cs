using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using Microsoft.Win32.SafeHandles;

namespace Nami.Hosting;

/// <summary>
///     The two OTLP listeners as Kestrel bound them. With a configured port of 0 the operating system chooses the
///     port, and Kestrel writes the bound endpoint back into these options when the host starts.
/// </summary>
internal sealed class CollectorListeners
{
    public ListenOptions? OtlpHttp { get; set; }
    public ListenOptions? Grpc { get; set; }

    /// <summary>The URL a client on this machine reaches the listener at; a wildcard address is reached over loopback.</summary>
    public static string? Url(ListenOptions? listener)
    {
        if (listener?.IPEndPoint is not { } endpoint) return null;
        var address = endpoint.Address;
        if (address.Equals(IPAddress.Any)) address = IPAddress.Loopback;
        else if (address.Equals(IPAddress.IPv6Any)) address = IPAddress.IPv6Loopback;
        return $"http://{new IPEndPoint(address, endpoint.Port)}";
    }
}

/// <summary>
///     Tells the supervising host where the engine listens. The host passes <c>--listeners-fd N</c> with a pipe it
///     opened for this one line; an engine started without the argument reports nothing. The line is written after
///     the listeners are bound and before the ready frame, so a host that has seen "ready" can read it to its end.
/// </summary>
internal static class EngineListenerReport
{
    internal const string Argument = "listeners-fd";

    public static void Write(WebApplication application)
    {
        if (!int.TryParse(application.Configuration[Argument], NumberStyles.None, CultureInfo.InvariantCulture,
                out var descriptor) || descriptor < 3)
            return;

        var listeners = application.Services.GetRequiredService<CollectorListeners>();
        var storage = application.Services.GetRequiredService<IOptions<CollectorStorageOptions>>().Value;
        using var handle = new SafeFileHandle(descriptor, true);
        using var pipe = new FileStream(handle, FileAccess.Write, 1, false);
        using (var writer = new Utf8JsonWriter(pipe))
        {
            writer.WriteStartObject();
            writer.WriteString("otlp_http", CollectorListeners.Url(listeners.OtlpHttp));
            writer.WriteString("otlp_grpc", CollectorListeners.Url(listeners.Grpc));
            writer.WriteString("data_path", Path.GetFullPath(storage.DataPath));
            writer.WriteEndObject();
        }

        pipe.WriteByte((byte)'\n');
    }
}
