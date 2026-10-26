using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Nami.Tests.Infrastructure;

/// <summary>
///     Starts the TypeScript MCP server under Bun and connects through stdio or HTTP using the .NET MCP client SDK.
///     The client SDK is a test dependency used to exercise MCP initialization, tool discovery, and tool calls
///     against the production TypeScript server, verifying interoperability between the two SDKs.
/// </summary>
internal sealed class McpHarness : IAsyncDisposable
{
    private McpClient? _client;
    private OwnedProcess? _process;
    private Task<string>? _stderr;
    private Task<string>? _stdout;
    private HttpClientTransport? _transport;

    private McpHarness(int httpPort, int otlpPort, int grpcPort)
    {
        (HttpPort, OtlpPort, GrpcPort) = (httpPort, otlpPort, grpcPort);
    }

    internal McpClient Client => _client ?? throw new InvalidOperationException("MCP startup has not completed.");
    internal HttpClient Http { get; } = new();
    internal int HttpPort { get; }
    internal int OtlpPort { get; }
    internal int GrpcPort { get; }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync().ConfigureAwait(false);
        if (_transport is not null) await _transport.DisposeAsync().ConfigureAwait(false);
        if (_process is not null)
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.Close();
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await _process.WaitForExitAsync(shutdown.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
                {
                    await _process.KillAsync(CancellationToken.None).ConfigureAwait(false);
                    await _process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }

            await _process.KillAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (_stderr is not null) await _stderr.ConfigureAwait(false);
                if (_stdout is not null) await _stdout.ConfigureAwait(false);
            }
            finally
            {
                await _process.DisposeAsync().ConfigureAwait(false);
            }
        }

        Http.Dispose();
    }

    private static int FreePort()
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
        var httpPort = FreePort();
        var otlpPort = FreePort();
        var grpcPort = FreePort();
        while (otlpPort == httpPort) otlpPort = FreePort();
        while (grpcPort == httpPort || grpcPort == otlpPort) grpcPort = FreePort();
        var env = new Dictionary<string, string?>
        {
            ["NAMI_PORT"] = httpPort.ToString(CultureInfo.InvariantCulture),
            ["NAMI_OTLP_PORT"] = otlpPort.ToString(CultureInfo.InvariantCulture),
            ["NAMI_GRPC_PORT"] = grpcPort.ToString(CultureInfo.InvariantCulture),
            ["NAMI_DATA_PATH"] = Path.Combine(directory, "nami.duckdb"),
            ["NAMI_OTLP_AUTH_MODE"] = keys is null ? "Unsecured" : "ApiKey",
            ["NAMI_OTLP_PROJECT_KEYS"] = keys,
            ["DOTNET_ENVIRONMENT"] = "Development"
        };
        const string Command = "bun";
        var args = new List<string>
        {
            Path.Combine(ProcessRunner.RepositoryRoot, "artifacts", "publish", "mcp", "main.js"),
            "--engine", binary
        };
        if (stdio) args.Add("--stdio");
        var options = new McpClientOptions
            { ProtocolVersion = "2026-07-28", InitializationTimeout = TimeSpan.FromSeconds(30) };
        if (stdio)
        {
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Command = Command, Arguments = args, WorkingDirectory = directory,
                EnvironmentVariables = env, ShutdownTimeout = TimeSpan.FromSeconds(5)
            });
            var fixture = new McpHarness(httpPort, otlpPort, grpcPort);
            try
            {
                fixture._client = await McpClient.CreateAsync(transport, options, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                // MCP startup follows the engine-ready handshake; stdio has no HTTP health listener.
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        var start = new ProcessStartInfo(Command)
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, WorkingDirectory = directory
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        foreach (var pair in env) start.Environment[pair.Key] = pair.Value;
        var host = new McpHarness(httpPort, otlpPort, grpcPort);
        try
        {
            host._process = await OwnedProcess.StartAsync(start, cancellationToken).ConfigureAwait(false);
            host._stderr = host._process.StandardError.ReadToEndAsync(CancellationToken.None);
            host._stdout = host._process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            await host.WaitForHealthAsync(cancellationToken).ConfigureAwait(false);
            var headers = readKey is null ? null : new Dictionary<string, string> { ["x-otlp-api-key"] = readKey };
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri($"http://127.0.0.1:{httpPort}/mcp"), AdditionalHeaders = headers
            });
            host._transport = transport;
            host._client = await McpClient.CreateAsync(transport, options, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return host;
        }
        catch
        {
            await host.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task WaitForHealthAsync(CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30), TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        Exception? lastRequestError = null;
        try
        {
            while (true)
            {
                linked.Token.ThrowIfCancellationRequested();
                if (_process?.HasExited == true)
                    throw new InvalidOperationException("Nami exited: " + await _stderr!.ConfigureAwait(false));
                try
                {
                    using var response = await Http.GetAsync($"http://127.0.0.1:{HttpPort}/health", linked.Token)
                        .ConfigureAwait(false);
                    if (response.IsSuccessStatusCode) return;
                }
                catch (HttpRequestException error)
                {
                    lastRequestError = error;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50), TimeProvider.System, linked.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException error) when (timeout.IsCancellationRequested &&
                                                       !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Nami health endpoint did not become ready.", lastRequestError ?? error);
        }
    }

    internal ValueTask<CallToolResult> CallAsync(string name, string json, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(json);
        return Client.CallToolAsync(new CallToolRequestParams
        {
            Name = name, Arguments = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())
        }, cancellationToken);
    }

    internal async Task<HttpResponseMessage> SendJsonAsync(string signal, string json,
        CancellationToken cancellationToken, string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{OtlpPort}/v1/{signal}");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        if (key is not null) request.Headers.Add("x-otlp-api-key", key);
        return await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
