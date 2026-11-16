using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Nami.Collector.Ingestion.Configuration;
using Nami.Hosting;

namespace Nami.IntegrationTests.ClaudeCode;

/// <summary>
///     nami's OTLP collector in this process, composed by the method the engine's <c>Program.cs</c> calls
///     (<see cref="CollectorComposition.BuildNamiCollector" />): core, storage and auth services, the Kestrel
///     listener, the middleware and the <c>/v1/{signal}</c> endpoints, on a loopback port and a fresh DuckDB file.
///     The engine pipe, the tools and the export of nami's own telemetry are left out
///     (<see cref="CollectorParts.IngestOnly" />); they play no part in ingest.
///     Retention is off (<c>NAMI_RETENTION_DAYS=0</c>), so a capture with fixed timestamps stays stored however
///     old it is.
/// </summary>
internal sealed class CollectorHost : IAsyncDisposable
{
    private WebApplication _application = null!;
    private bool _stopped;

    private CollectorHost(Uri endpoint, string dataPath)
    {
        (Endpoint, DataPath) = (endpoint, dataPath);
    }

    /// <summary>The OTLP/HTTP base address, ending in a slash.</summary>
    internal Uri Endpoint { get; }

    /// <summary>The DuckDB file the collector writes; closed and complete once <see cref="StopAsync" /> returned.</summary>
    internal string DataPath { get; }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    internal static async Task<CollectorHost> StartAsync(string directory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var port = FreePort();
        var host = new CollectorHost(new Uri($"http://127.0.0.1:{port}/"), Path.Combine(directory, "nami.duckdb"));
        var builder = LoopbackHost.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [CollectorConfigurationKeys.BindAddress] = IPAddress.Loopback.ToString(),
            [CollectorConfigurationKeys.OtlpPort] = port.ToString(CultureInfo.InvariantCulture),
            [CollectorConfigurationKeys.GrpcPort] = "0",
            [CollectorConfigurationKeys.DataPath] = host.DataPath,
            [CollectorConfigurationKeys.AuthMode] = "Unsecured",
            [CollectorConfigurationKeys.RetentionDays] = "0"
        });
        host._application = builder.BuildNamiCollector(CollectorParts.IngestOnly);
        return await host._application.StartOwnedAsync(() => host, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Stops the host and disposes its store, which waits for every queued write; the DuckDB file is then
    ///     closed and complete.
    /// </summary>
    internal async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        if (_stopped) return;
        _stopped = true;
        await _application.StopAndDisposeAsync(cancellationToken).ConfigureAwait(false);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
