using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Nami.ToolSmoke;

var installedMode = args is ["--installed", _, "--version", _];
var packageMode = args is [_];
if (!installedMode && !packageMode)
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  dotnet run --project eng/tools/NamiToolSmoke -- <package-directory>");
    Console.Error.WriteLine("  dotnet run --project eng/tools/NamiToolSmoke -- --installed <nami-path> --version <version>");
    return 2;
}

var rid = RuntimeInformation.RuntimeIdentifier;
var expectedVersion = installedMode ? args[3] : string.Empty;
var installedTool = installedMode ? Path.GetFullPath(args[1]) : null;
string? packageDirectory = null;
PackageInfo? implementation = null;
if (packageMode)
{
    packageDirectory = Path.GetFullPath(args[0]);
    if (!Directory.Exists(packageDirectory))
        throw new DirectoryNotFoundException($"Package directory '{packageDirectory}' does not exist.");

    var packages = Directory.GetFiles(packageDirectory, "*.nupkg", SearchOption.TopDirectoryOnly)
        .Select(PackageInfo.Read)
        .ToArray();
    var outer = packages.Single(static package => string.Equals(package.Id, "nami", StringComparison.OrdinalIgnoreCase));
    implementation = packages.Single(package =>
        string.Equals(package.Id, $"nami.{rid}", StringComparison.OrdinalIgnoreCase));
    expectedVersion = outer.Version;

    var outerSettingsPath = outer.RequireSingleEntry(
        "tools/net10.0/any/DotnetToolSettings.xml",
        "tools/any/any/DotnetToolSettings.xml");
    outer.RequireText(outerSettingsPath, $"RuntimeIdentifier=\"{rid}\"");
    implementation.RequireEntry($"tools/net10.0/{rid}/DotnetToolSettings.xml");
    implementation.RequireEntry($"tools/net10.0/{rid}/collector/nami.collector.dll");
    implementation.RequireEntry($"tools/net10.0/{rid}/collector/nami.collector.deps.json");
    implementation.RequireEntry($"tools/net10.0/{rid}/collector/nami.collector.runtimeconfig.json");
    implementation.RequireEntry($"tools/net10.0/{rid}/collector/{NativeDuckDbName()}");
}
else if (!File.Exists(installedTool))
{
    throw new FileNotFoundException("Installed nami command was not found.", installedTool);
}

var scratch = Path.Combine(Path.GetTempPath(), $"nami-tool-smoke-{Guid.NewGuid():N}");
var toolDirectory = Path.Combine(scratch, "tool");
var dataDirectory = Path.Combine(scratch, "data");
Directory.CreateDirectory(dataDirectory);

try
{
    var tool = installedTool;
    if (tool is null)
    {
        Progress($"installing nami {expectedVersion} from {packageDirectory}");
        var install = await RunAsync(
            "dotnet",
            [
                "tool", "install", "nami", "--tool-path", toolDirectory,
                "--version", expectedVersion, "--add-source", packageDirectory!,
                "--ignore-failed-sources", "--no-http-cache"
            ],
            scratch,
            TimeSpan.FromMinutes(2));
        install.RequireExitCode(0, "clean tool install");
        tool = ResolveInstalledTool(toolDirectory);
    }

    if (!File.Exists(tool)) throw new FileNotFoundException("Installed nami command was not created.", tool);

    Progress($"exercising {tool}: --version");
    var version = await RunAsync(tool, ["--version"], dataDirectory, TimeSpan.FromSeconds(20));
    version.RequireExitCode(0, "nami --version");
    if (!string.Equals(version.Stdout.Trim(), expectedVersion, StringComparison.Ordinal))
        throw new InvalidOperationException($"Installed nami reported '{version.Stdout.Trim()}', expected '{expectedVersion}'.");

    Progress("exercising: live 'nami up' end-to-end");
    await RunLiveAsync(tool, dataDirectory);

    var source = implementation is null ? tool : implementation.Path;
    Console.WriteLine($"nami {expectedVersion} installed-tool smoke passed for {rid} ({source}).");
    await CiTelemetry.FlushAsync(success: true, failure: null);
    return 0;
}
catch (Exception exception)
{
    await CiTelemetry.FlushAsync(success: false, failure: exception.Message);
    throw;
}
finally
{
    await DeleteDirectoryAsync(scratch, TimeSpan.FromSeconds(30));
}

// Timestamped phase markers: the smoke has hung without any output before, so make it
// obvious from a cancelled run's log which phase never returned. Each marker also
// opens a ci_log phase span (inert unless NAMI_CI_OTLP_ENDPOINT is set).
static void Progress(string message)
{
    Console.WriteLine($"[{DateTimeOffset.UtcNow:HH:mm:ss}] {message}");
    CiTelemetry.Mark(message);
}

static string ResolveInstalledTool(string toolDirectory)
{
    // .NET 10 SDK tool-path installs shim Windows tools as nami.cmd (no .exe shim).
    string[] commandNames = OperatingSystem.IsWindows() ? ["nami.exe", "nami.cmd", "nami"] : ["nami", "nami.exe"];
    foreach (var commandName in commandNames)
    {
        var candidate = Path.Combine(toolDirectory, commandName);
        if (File.Exists(candidate)) return candidate;
    }

    var contents = Directory.Exists(toolDirectory)
        ? string.Join(", ", Directory.EnumerateFileSystemEntries(toolDirectory)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal))
        : "<directory missing>";
    throw new FileNotFoundException(
        $"The nami tool installation did not create a supported command shim in '{toolDirectory}'. " +
        $"Top-level contents: {contents}.");
}

static async Task DeleteDirectoryAsync(string path, TimeSpan timeout)
{
    var elapsed = Stopwatch.StartNew();
    while (true)
    {
        try
        {
            Directory.Delete(path, recursive: true);
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException && elapsed.Elapsed < timeout)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
    }
}

static async Task RunLiveAsync(string tool, string workingDirectory)
{
    var isolatedHome = Path.Combine(workingDirectory, "home");
    Directory.CreateDirectory(isolatedHome);
    using var process = Start(
        tool,
        ["up"],
        workingDirectory,
        environment: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOME"] = isolatedHome,
            ["USERPROFILE"] = isolatedHome
        });
    try
    {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(2);
        await WaitUntilAsync(async () =>
        {
            if (process.HasExited)
                throw new InvalidOperationException($"nami exited {process.ExitCode} before becoming ready.");
            if (!await IsHealthyAsync(client, "http://127.0.0.1:5100/health")) return false;
            if (!await IsHealthyAsync(client, "http://127.0.0.1:5200/health")) return false;
            return await AreRunnerResourcesReadyAsync(client);
        }, TimeSpan.FromSeconds(90), "nami collectors and runner API did not become ready");

        Progress("live: collectors and runner API ready");

        await VerifyIngestAndReadbackAsync(client);
        Progress("live: ingest and readback verified");
        await VerifyRunnerSelfTelemetryAsync(client);
        Progress("live: runner self-telemetry readable from the diagnostics collector, shutting down");
    }
    finally
    {
        await StopLiveAsync(process, workingDirectory);
    }

    if (!OperatingSystem.IsWindows() && process.ExitCode != 0)
        throw new InvalidOperationException($"nami exited {process.ExitCode} after Ctrl-C.");

    await WaitUntilAsync(
        () => Task.FromResult(ArePortsFree([5100, 5200, 4317, 4318, SmokeEndpoints.RunnerApiPort])),
        TimeSpan.FromSeconds(30),
        "nami left a collector or runner listener behind after shutdown");
    Progress("live: shutdown clean, all ports released");
}

static async Task StopLiveAsync(Process process, string workingDirectory)
{
    if (process.HasExited) return;

    if (OperatingSystem.IsWindows())
    {
        process.Kill(entireProcessTree: true);
    }
    else
    {
        var signal = await RunAsync("/bin/kill", ["-INT", process.Id.ToString(CultureInfo.InvariantCulture)],
            workingDirectory, TimeSpan.FromSeconds(10));
        signal.RequireExitCode(0, "send Ctrl-C to nami");
    }

    using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    try
    {
        await process.WaitForExitAsync(shutdown.Token);
    }
    catch (OperationCanceledException)
    {
        // The whole tree: a bare Kill() leaves nami's collector children running on their ports.
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        using var forced = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync(forced.Token);
    }
}

static async Task VerifyIngestAndReadbackAsync(HttpClient client)
{
    const string TraceId = "0af7651916cd43dd8448eb211c80319c";
    const string SpanId = "b7ad6b7169203331";
    string[] capturedKeys = ["http.request.header.authorization", "http.request.header.x-token-count", "rpc.response.metadata.trace-bin"];
    string[] capturedValues = ["first", "first", "", "AQID"];
    var start = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000;
    var payload = JsonSerializer.Serialize(new
    {
        resourceSpans = new[]
        {
            new
            {
                resource = new
                {
                    attributes = new[]
                    {
                        new { key = "service.name", value = new { stringValue = "nami-tool-smoke" } }
                    }
                },
                scopeSpans = new[]
                {
                    new
                    {
                        scope = new { name = "nami-tool-smoke" },
                        spans = new[]
                        {
                            new
                            {
                                // OTLP/JSON deliberately uses hex ids (unlike generic protojson,
                                // which uses base64 for bytes). This exercises the collector's
                                // standards-compatible JSON normalization path.
                                traceId = TraceId,
                                spanId = SpanId,
                                name = "installed-tool-span",
                                kind = 1,
                                startTimeUnixNano = start.ToString(CultureInfo.InvariantCulture),
                                endTimeUnixNano = (start + 1_000_000).ToString(CultureInfo.InvariantCulture),
                                status = new { code = 1 },
                                attributes = capturedKeys.Select(key => new
                                {
                                    key,
                                    value = new
                                    {
                                        arrayValue = new
                                        {
                                            values = capturedValues.Select(value => new { stringValue = value }).ToArray()
                                        }
                                    }
                                }).ToArray()
                            }
                        }
                    }
                }
            }
        }
    });
    using var ingestContent = new StringContent(payload, Encoding.UTF8, "application/json");
    using var ingest = await client.PostAsync("http://127.0.0.1:4318/v1/traces", ingestContent);
    if (!ingest.IsSuccessStatusCode)
    {
        var error = await ingest.Content.ReadAsStringAsync();
        throw new InvalidOperationException(
            $"Installed collector rejected OTLP trace ingest with {(int)ingest.StatusCode}: {error}");
    }

    await WaitUntilAsync(async () =>
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:5100/api/v1/traces?limit=100");
        request.Headers.Add("X-Nami-Project", "default");
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode) return false;
        var body = await response.Content.ReadAsStringAsync();
        return body.Contains(TraceId, StringComparison.Ordinal) && body.Contains("nami-tool-smoke", StringComparison.Ordinal);
    }, TimeSpan.FromSeconds(20), "OTLP trace was not readable from the product API");

    using var spanRequest = new HttpRequestMessage(HttpMethod.Get,
        $"http://127.0.0.1:5100/api/v1/traces/{TraceId}/spans");
    spanRequest.Headers.Add("X-Nami-Project", "default");
    using var spanResponse = await client.SendAsync(spanRequest);
    spanResponse.EnsureSuccessStatusCode();
    using var spans = JsonDocument.Parse(await spanResponse.Content.ReadAsStringAsync());
    var span = spans.RootElement.GetProperty("items").EnumerateArray()
        .Single(item => item.GetProperty("span_id").GetString() == SpanId);
    var attributes = span.GetProperty("attributes").EnumerateArray()
        .ToDictionary(static item => item.GetProperty("key").GetString()!, static item => item.GetProperty("value"));
    foreach (var key in capturedKeys)
    {
        if (!attributes.TryGetValue(key, out var values) || values.ValueKind != JsonValueKind.Array ||
            !capturedValues.SequenceEqual(values.EnumerateArray().Select(static item => item.GetString())))
            throw new InvalidOperationException($"Installed collector did not preserve captured array '{key}'.");
    }
}

// The runner process exports its own telemetry (AddNami: the health-probe HttpClient spans, logs,
// meters) to the diagnostics collector and nowhere else. The product collector's trace list must
// not know the runner's service; the diagnostics collector's must.
static async Task VerifyRunnerSelfTelemetryAsync(HttpClient client)
{
    const string RunnerService = "nami-runner";
    await WaitUntilAsync(async () =>
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"http://127.0.0.1:5200/api/v1/traces?limit=100&service_name={RunnerService}");
        request.Headers.Add("X-Nami-Project", "default");
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode) return false;
        var body = await response.Content.ReadAsStringAsync();
        return body.Contains(RunnerService, StringComparison.Ordinal);
    }, TimeSpan.FromSeconds(45), "the runner's own spans were not readable from the diagnostics collector");

    using var productRequest = new HttpRequestMessage(HttpMethod.Get,
        $"http://127.0.0.1:5100/api/v1/traces?limit=100&service_name={RunnerService}");
    productRequest.Headers.Add("X-Nami-Project", "default");
    using var productResponse = await client.SendAsync(productRequest);
    var productBody = await productResponse.Content.ReadAsStringAsync();
    if (productBody.Contains(RunnerService, StringComparison.Ordinal))
        throw new InvalidOperationException("The product collector received the runner's own telemetry; it belongs to the diagnostics collector only.");
}

static async Task<bool> IsHealthyAsync(HttpClient client, string uri)
{
    try
    {
        using var response = await client.GetAsync(uri);
        return response.IsSuccessStatusCode;
    }
    catch (HttpRequestException)
    {
        return false;
    }
    catch (TaskCanceledException)
    {
        return false;
    }
}

static async Task<bool> AreRunnerResourcesReadyAsync(HttpClient client)
{
    try
    {
        using var response = await client.GetAsync($"http://127.0.0.1:{SmokeEndpoints.RunnerApiPort}/runner/resources");
        if (!response.IsSuccessStatusCode) return false;
        await using var body = await response.Content.ReadAsStreamAsync();
        using var resources = await JsonDocument.ParseAsync(body);
        var ready = resources.RootElement.EnumerateArray()
            .Where(static resource =>
                resource.TryGetProperty("lifecycle", out var lifecycle) &&
                string.Equals(lifecycle.GetString(), "ready", StringComparison.OrdinalIgnoreCase))
            .Select(static resource => resource.GetProperty("name").GetString())
            .ToHashSet(StringComparer.Ordinal);
        return ready.SetEquals(["collector", "diagnostics"]);
    }
    catch (HttpRequestException)
    {
        return false;
    }
    catch (TaskCanceledException)
    {
        return false;
    }
}

static bool ArePortsFree(IEnumerable<int> ports)
{
    var listeners = new List<TcpListener>();
    try
    {
        foreach (var port in ports)
        {
            TcpListener? listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listeners.Add(listener);
                listener = null;
            }
            finally
            {
                listener?.Dispose();
            }
        }

        return true;
    }
    catch (SocketException)
    {
        return false;
    }
    finally
    {
        foreach (var listener in listeners) listener.Stop();
    }
}

static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout, string failure)
{
    var elapsed = Stopwatch.StartNew();
    while (elapsed.Elapsed < timeout)
    {
        if (await condition()) return;
        await Task.Delay(TimeSpan.FromMilliseconds(250));
    }

    throw new TimeoutException(failure);
}

static Process Start(
    string fileName,
    IReadOnlyList<string> arguments,
    string workingDirectory,
    ConcurrentQueue<string>? stdout = null,
    ConcurrentQueue<string>? stderr = null,
    IReadOnlyDictionary<string, string>? environment = null)
{
    // CreateProcess cannot exec a .cmd shim directly (and .NET refuses since the BatBadBut
    // mitigation), so route shims through cmd.exe. Arguments here are fixed tokens and
    // space-free paths; cmd metacharacter quoting is deliberately not handled.
    var isCmdShim = OperatingSystem.IsWindows() &&
                    (fileName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                     fileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase));
    var startInfo = new ProcessStartInfo(isCmdShim
        ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe"
        : fileName)
    {
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = stdout is not null,
        RedirectStandardError = stderr is not null,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    if (isCmdShim)
    {
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(fileName);
    }

    foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
    if (environment is not null)
    {
        foreach (var (name, value) in environment)
            startInfo.Environment[name] = value;
    }

    var process = new Process { StartInfo = startInfo };
    if (stdout is not null) process.OutputDataReceived += (_, eventArgs) =>
    {
        if (eventArgs.Data is { } line) stdout.Enqueue(line);
    };
    if (stderr is not null) process.ErrorDataReceived += (_, eventArgs) =>
    {
        if (eventArgs.Data is { } line) stderr.Enqueue(line);
    };
    if (!process.Start()) throw new InvalidOperationException($"Could not start '{fileName}'.");
    if (stdout is not null) process.BeginOutputReadLine();
    if (stderr is not null) process.BeginErrorReadLine();
    return process;
}

static async Task<ProcessResult> RunAsync(
    string fileName,
    IReadOnlyList<string> arguments,
    string workingDirectory,
    TimeSpan timeout)
{
    var stdout = new ConcurrentQueue<string>();
    var stderr = new ConcurrentQueue<string>();
    using var process = Start(fileName, arguments, workingDirectory, stdout, stderr);
    using var cancellation = new CancellationTokenSource(timeout);
    try
    {
        await process.WaitForExitAsync(cancellation.Token);
    }
    catch (OperationCanceledException)
    {
        process.Kill(entireProcessTree: true);
        throw new TimeoutException($"'{fileName} {string.Join(' ', arguments)}' did not exit within {timeout}.");
    }

    return new ProcessResult(process.ExitCode, string.Join(Environment.NewLine, stdout),
        string.Join(Environment.NewLine, stderr));
}

static string NativeDuckDbName()
{
    if (OperatingSystem.IsWindows()) return "duckdb.dll";
    if (OperatingSystem.IsMacOS()) return "libduckdb.dylib";
    return "libduckdb.so";
}

internal sealed record PackageInfo(string Path, string Id, string Version, HashSet<string> Entries)
{
    internal static PackageInfo Read(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var nuspec = archive.Entries.Single(static entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        using var stream = nuspec.Open();
        var document = XDocument.Load(stream);
        var metadata = document.Root?.Elements().Single(static element => element.Name.LocalName == "metadata")
                       ?? throw new InvalidDataException($"Package '{path}' has no nuspec metadata.");
        var id = metadata.Elements().Single(static element => element.Name.LocalName == "id").Value;
        var version = metadata.Elements().Single(static element => element.Name.LocalName == "version").Value;
        return new PackageInfo(path, id, version,
            archive.Entries.Select(static entry => entry.FullName).ToHashSet(StringComparer.Ordinal));
    }

    internal void RequireEntry(string path)
    {
        if (!Entries.Contains(path))
            throw new InvalidDataException($"Package '{Path}' is missing required entry '{path}'.");
    }

    internal string RequireSingleEntry(params string[] candidates)
    {
        var matches = Entries.Intersect(candidates, StringComparer.Ordinal).ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidDataException(
                $"Package '{Path}' contains {matches.Length} supported tool settings entries, expected exactly one.");
    }

    internal void RequireText(string path, string expected)
    {
        using var archive = ZipFile.OpenRead(Path);
        var entry = archive.GetEntry(path) ?? throw new InvalidDataException($"Package '{Path}' is missing '{path}'.");
        using var reader = new StreamReader(entry.Open());
        var text = reader.ReadToEnd();
        if (!text.Contains(expected, StringComparison.Ordinal))
            throw new InvalidDataException($"Package entry '{path}' does not contain '{expected}'.");
    }
}

internal static class SmokeEndpoints
{
    internal const int RunnerApiPort = 18889;
}

internal sealed record ProcessResult(int ExitCode, string Stdout, string Stderr)
{
    internal void RequireExitCode(int expected, string operation)
    {
        if (ExitCode != expected)
            throw new InvalidOperationException($"{operation} exited {ExitCode}, expected {expected}.{Environment.NewLine}{this}");
    }

    public override string ToString() =>
        $"stdout:{Environment.NewLine}{Stdout}{Environment.NewLine}stderr:{Environment.NewLine}{Stderr}";
}
