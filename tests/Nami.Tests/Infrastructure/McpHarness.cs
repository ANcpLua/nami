using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Nami.Tests.Infrastructure;

internal sealed class McpHarness : IAsyncDisposable
{
    private readonly Process? _process;
    private readonly Task<string>? _stderr;
    private readonly Task<string>? _stdout;
    private HttpClientTransport? _transport;
    internal McpClient Client { get; private set; } = null!;
    internal HttpClient Http { get; } = new();
    internal int HttpPort { get; }
    internal int OtlpPort { get; }
    internal int GrpcPort { get; }
    internal string DirectoryPath { get; }
    internal string DatabasePath => Path.Combine(DirectoryPath, "nami.duckdb");
    internal string StandardError => _stderr?.IsCompletedSuccessfully == true ? _stderr.Result : "";

    private McpHarness(string directory, int httpPort, int otlpPort, int grpcPort,
        Process? process = null, Task<string>? stderr = null, Task<string>? stdout = null)
        => (DirectoryPath, HttpPort, OtlpPort, GrpcPort, _process, _stderr, _stdout) =
           (directory, httpPort, otlpPort, grpcPort, process, stderr, stdout);

    internal static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    internal static async Task<McpHarness> StartAsync(string binary, bool stdio, string directory,
        CancellationToken cancellationToken, string? keys = null, string? readKey = null)
    {
        Directory.CreateDirectory(directory);
        var httpPort = FreePort(); var otlpPort = FreePort(); var grpcPort = FreePort();
        while (otlpPort == httpPort) otlpPort = FreePort();
        while (grpcPort == httpPort || grpcPort == otlpPort) grpcPort = FreePort();
        var env = new Dictionary<string, string?>
        {
            ["NAMI_PORT"] = httpPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["NAMI_OTLP_PORT"] = otlpPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["NAMI_GRPC_PORT"] = grpcPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["NAMI_DATA_PATH"] = Path.Combine(directory, "nami.duckdb"),
            ["NAMI_OTLP_AUTH_MODE"] = keys is null ? "Unsecured" : "ApiKey",
            ["NAMI_OTLP_PROJECT_KEYS"] = keys,
            ["DOTNET_ENVIRONMENT"] = "Development"
        };
        var managed = binary.EndsWith(".dll", StringComparison.Ordinal);
        var command = managed ? "dotnet" : binary;
        var args = managed ? new List<string> { binary } : [];
        if (stdio) args.Add("--stdio");
        var options = new McpClientOptions { ProtocolVersion = "2026-07-28", InitializationTimeout = TimeSpan.FromSeconds(30) };
        if (stdio)
        {
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Command = command, Arguments = args, WorkingDirectory = directory,
                EnvironmentVariables = env, ShutdownTimeout = TimeSpan.FromSeconds(5)
            });
            var fixture = new McpHarness(directory, httpPort, otlpPort, grpcPort);
            try { fixture.Client = await McpClient.CreateAsync(transport, options, cancellationToken: cancellationToken).ConfigureAwait(false);
                await fixture.WaitForHealthAsync(cancellationToken).ConfigureAwait(false); return fixture; }
            catch { await fixture.DisposeAsync().ConfigureAwait(false); throw; }
        }
        var start = new ProcessStartInfo(command) { UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, WorkingDirectory = directory };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        foreach (var pair in env) start.Environment[pair.Key] = pair.Value;
        var process = Process.Start(start) ?? throw new InvalidOperationException("Nami did not start.");
        var host = new McpHarness(directory, httpPort, otlpPort, grpcPort, process,
            process.StandardError.ReadToEndAsync(CancellationToken.None), process.StandardOutput.ReadToEndAsync(CancellationToken.None));
        try
        {
            await host.WaitForHealthAsync(cancellationToken).ConfigureAwait(false);
            var headers = readKey is null ? null : new Dictionary<string, string> { ["x-otlp-api-key"] = readKey };
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri($"http://127.0.0.1:{httpPort}/mcp"), AdditionalHeaders = headers
            });
            host._transport = transport;
            host.Client = await McpClient.CreateAsync(transport, options, cancellationToken: cancellationToken).ConfigureAwait(false);
            return host;
        }
        catch { await host.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private async Task WaitForHealthAsync(CancellationToken cancellationToken)
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < until)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_process?.HasExited == true) throw new InvalidOperationException("Nami exited: " + await _stderr!.ConfigureAwait(false));
            try { using var response = await Http.GetAsync($"http://127.0.0.1:{HttpPort}/health", cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) return; }
            catch (HttpRequestException) { }
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException("Nami health endpoint did not become ready.");
    }

    internal ValueTask<CallToolResult> CallAsync(string name, string json, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(json);
        return Client.CallToolAsync(new CallToolRequestParams
        {
            Name = name, Arguments = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())
        }, cancellationToken);
    }

    internal async Task<HttpResponseMessage> SendJsonAsync(string signal, string json, CancellationToken cancellationToken, string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{OtlpPort}/v1/{signal}")
            { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (key is not null) request.Headers.Add("x-otlp-api-key", key);
        return await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Client is not null) await Client.DisposeAsync().ConfigureAwait(false);
        if (_transport is not null) await _transport.DisposeAsync().ConfigureAwait(false);
        if (_process is not null)
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            if (_stderr is not null) await _stderr.ConfigureAwait(false);
            if (_stdout is not null) await _stdout.ConfigureAwait(false);
            _process.Dispose();
        }
        Http.Dispose();
    }
}
