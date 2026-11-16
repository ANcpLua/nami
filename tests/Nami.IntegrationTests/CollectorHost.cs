using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nami.Collector.Ingestion;
using Nami.Collector.Ingestion.Configuration;
using Nami.Collector.Storage;
using Nami.Hosting;
using Nami.Tests.Infrastructure;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Nami.IntegrationTests;

/// <summary>
///     The collector as src/nami/Program.cs composes it, started inside the test process on loopback: both call
///     <see cref="CollectorComposition.BuildNamiCollector" />, so the registrations, API-key middleware, gRPC
///     interceptor, OTLP endpoints, Kestrel listeners and DuckDB store are the same. Left out
///     (<see cref="CollectorParts.IngestOnly" />) are the tool service, which only the engine protocol loop on stdin
///     calls, and the self-telemetry exporters, which would add the engine's own rows.
/// </summary>
internal sealed class CollectorHost : IAsyncDisposable
{
    internal const string JsonTransport = "json";
    internal const string ProtobufTransport = "protobuf";
    internal const string GrpcTransport = "grpc";

    private readonly WebApplication _application;
    private readonly GrpcChannel _channel;
    private readonly HttpClient _http = new();
    private readonly Uri _otlpHttp;

    private CollectorHost(WebApplication application, int httpPort, int grpcPort)
    {
        _application = application;
        _otlpHttp = new Uri($"http://127.0.0.1:{httpPort}/");
        _channel = GrpcChannel.ForAddress($"http://127.0.0.1:{grpcPort}");
    }

    /// <summary>The store the endpoints write to; a read through it waits for every accepted export to be committed.</summary>
    internal INamiStore Store => _application.Services.GetRequiredService<INamiStore>();

    public async ValueTask DisposeAsync()
    {
        _channel.Dispose();
        _http.Dispose();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await _application.StopAndDisposeAsync(stop.Token).ConfigureAwait(false);
    }

    /// <summary>
    ///     Starts a collector that stores in <paramref name="dataPath" />. With <paramref name="projectKeys" /> it runs
    ///     in ApiKey mode, otherwise unsecured. With <paramref name="clock" /> the store and the endpoints read that
    ///     clock, so that every receipt time is the same in each host a test starts. <paramref name="settings" /> are
    ///     further engine settings, such as the bounds of <see cref="IngestLimits" />.
    /// </summary>
    internal static async Task<CollectorHost> StartAsync(string dataPath, CancellationToken cancellationToken,
        string? projectKeys = null, TimeProvider? clock = null, IReadOnlyDictionary<string, string?>? settings = null)
    {
        for (var attempt = 1;; attempt++)
        {
            var httpPort = FreePort();
            var grpcPort = FreePort();
            while (grpcPort == httpPort) grpcPort = FreePort();
            var application = Build(dataPath, httpPort, grpcPort, projectKeys, clock, settings);
            try
            {
                await application.StartAsync(cancellationToken).ConfigureAwait(false);
                return new CollectorHost(application, httpPort, grpcPort);
            }
            catch (IOException) when (attempt < 5)
            {
                // Another process bound the port between the probe and Kestrel's bind; take two new ports.
                await application.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                await application.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    private static WebApplication Build(string dataPath, int httpPort, int grpcPort, string? projectKeys,
        TimeProvider? clock, IReadOnlyDictionary<string, string?>? settings)
    {
        var builder = LoopbackHost.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [CollectorConfigurationKeys.OtlpPort] = httpPort.ToString(CultureInfo.InvariantCulture),
            [CollectorConfigurationKeys.GrpcPort] = grpcPort.ToString(CultureInfo.InvariantCulture),
            [CollectorConfigurationKeys.DataPath] = dataPath,
            [CollectorConfigurationKeys.AuthMode] = projectKeys is null ? "Unsecured" : "ApiKey",
            [CollectorConfigurationKeys.ProjectKeys] = projectKeys
        });
        if (settings is not null) builder.Configuration.AddInMemoryCollection(settings);
        // The core registration keeps a clock that is already registered, and the storage registration builds its
        // store on the registered clock.
        if (clock is not null) builder.Services.AddSingleton(clock);
        return builder.BuildNamiCollector(CollectorParts.IngestOnly);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>Posts an OTLP/HTTP JSON export of <paramref name="signal" />, with the API key when one is given.</summary>
    internal async Task<HttpResponseMessage> PostJsonAsync(string signal, string json,
        CancellationToken cancellationToken, string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_otlpHttp, "v1/" + signal));
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        if (key is not null) request.Headers.Add(OtlpConstants.ApiKeyHeaderName, key);
        return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Posts an OTLP/HTTP protobuf export of <paramref name="signal" />, with the API key when one is given.</summary>
    internal async Task<HttpResponseMessage> PostProtobufAsync(string signal, byte[] payload,
        CancellationToken cancellationToken, string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_otlpHttp, "v1/" + signal));
        request.Content = OtlpHttp.Content(payload);
        if (key is not null) request.Headers.Add(OtlpConstants.ApiKeyHeaderName, key);
        return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The slots of the ingest gate no request holds at this moment.</summary>
    internal int FreeIngestSlots => _application.Services.GetRequiredService<OtlpIngestGate>().FreeSlots;

    /// <summary>The OTLP/HTTP listener, for a request a test builds itself.</summary>
    internal Uri HttpEndpoint => _otlpHttp;

    /// <summary>Calls the gRPC Export method of <paramref name="signal" /> with the given call metadata.</summary>
    internal async Task<byte[]> ExportGrpcAsync(string signal, byte[] payload, CancellationToken cancellationToken,
        Metadata? headers = null)
    {
        using var call = OtlpGrpc.ExportAsync(_channel, signal, payload,
            new CallOptions(headers, cancellationToken: cancellationToken));
        return await call.ResponseAsync.ConfigureAwait(false);
    }

    /// <summary>
    ///     The protobuf encoding of an OTLP JSON export. Google.Protobuf converts it here, so the bytes do not come
    ///     from the collector's own JSON reader.
    /// </summary>
    internal static byte[] Encode(string signal, string json)
    {
        var message = TestData.ProtobufJson(json);
        return signal switch
        {
            Signals.Traces => ExportTraceServiceRequest.Parser.ParseJson(message).ToByteArray(),
            Signals.Logs => ExportLogsServiceRequest.Parser.ParseJson(message).ToByteArray(),
            _ => ExportMetricsServiceRequest.Parser.ParseJson(message).ToByteArray()
        };
    }

    /// <summary>
    ///     Sends the OTLP JSON export over <paramref name="transport" /> (HTTP JSON, HTTP protobuf or gRPC) and fails
    ///     unless the collector accepts it.
    /// </summary>
    internal async Task SendAsync(string transport, string signal, string json, CancellationToken cancellationToken)
    {
        switch (transport)
        {
            case JsonTransport:
                {
                    using var response = await PostJsonAsync(signal, json, cancellationToken).ConfigureAwait(false);
                    Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, transport + " " + signal);
                }
                break;
            case ProtobufTransport:
                {
                    using var response = await PostProtobufAsync(signal, Encode(signal, json), cancellationToken)
                        .ConfigureAwait(false);
                    Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, transport + " " + signal);
                }
                break;
            case GrpcTransport:
                _ = await ExportGrpcAsync(signal, Encode(signal, json), cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(transport), transport, "Unknown OTLP transport.");
        }
    }
}
