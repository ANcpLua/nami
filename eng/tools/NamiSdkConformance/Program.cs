using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nami.Api.Contracts.Common.Pagination;
using Nami.Api.Contracts.OTel.Enums;
using Nami.Api.Contracts.OTel.Logs;
using ContractAttribute = Nami.Api.Contracts.Common.Attribute;
using ContractResource = Nami.Api.Contracts.OTel.Resource.Resource;
using ContractSpan = Nami.Api.Contracts.OTel.Traces.Span;
using ContractTrace = Nami.Api.Contracts.OTel.Traces.Trace;

return await SdkConformance.RunAsync(args).ConfigureAwait(false);

internal static class SdkConformance
{
    private const string TraceIdHeader = "X-Nami-Conformance-Trace-Id";
    private const string SpanIdHeader = "X-Nami-Conformance-Span-Id";
    private const string WorkCompletedEventName = "WorkCompleted";
    private static readonly TimeSpan s_startupTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan s_processExitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan s_readbackTimeout = TimeSpan.FromSeconds(30);

    private static readonly HttpClient s_http = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 5)
        {
            await Console.Error.WriteLineAsync(
                    "usage: NamiSdkConformance <app-executable> <app-base> <api-base> <otlp-base> <service-name>")
                .ConfigureAwait(false);
            return 2;
        }

        try
        {
            var executable = Path.GetFullPath(args[0]);
            var appBase = RequireAbsoluteHttpUri(args[1], "app base");
            var apiBase = RequireAbsoluteHttpUri(args[2], "API base");
            var otlpBase = RequireAbsoluteHttpUri(args[3], "OTLP base");
            var serviceName = args[4];
            if (string.IsNullOrWhiteSpace(serviceName))
                throw new ArgumentException("The conformance service name must not be blank.");
            if (!File.Exists(executable))
                throw new FileNotFoundException("Native AOT conformance executable not found.", executable);

            var evidence = await ExecuteAsync(executable, appBase, apiBase, otlpBase, serviceName)
                .ConfigureAwait(false);
            await Console.Out.WriteLineAsync(
                    $"[sdk-conformance] PASS trace={evidence.TraceId} " +
                    $"app-server-span={evidence.ServerSpanId} outbound-client-span={evidence.ClientSpanId} " +
                    $"correlated-log-span={evidence.LogSpanId} service={serviceName} " +
                    $"storage=collector-duckdb api=/api/v1/traces/{evidence.TraceId}")
                .ConfigureAwait(false);
            return 0;
        }
        catch (ArgumentException exception)
        {
            return await ReportFailureAsync(exception).ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            return await ReportFailureAsync(exception).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            return await ReportFailureAsync(exception).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            return await ReportFailureAsync(exception).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return await ReportFailureAsync(exception).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            return await ReportFailureAsync(exception).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            return await ReportFailureAsync(exception).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            return await ReportFailureAsync(exception).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException exception)
        {
            return await ReportFailureAsync(exception).ConfigureAwait(false);
        }
    }

    private static async Task<int> ReportFailureAsync(Exception exception)
    {
        await Console.Error.WriteLineAsync($"[sdk-conformance] FAIL: {exception.Message}")
            .ConfigureAwait(false);
        return 1;
    }

    private static async Task<ConformanceEvidence> ExecuteAsync(
        string executable,
        Uri appBase,
        Uri apiBase,
        Uri otlpBase,
        string serviceName)
    {
        var output = new ConcurrentQueue<string>();
        using var process = CreateProcess(executable, appBase, otlpBase, serviceName, output);
        if (!process.Start())
            throw new InvalidOperationException("Native AOT conformance app process did not start.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            var ids = await DriveAppAsync(process, appBase, output).ConfigureAwait(false);
            await WaitForSuccessfulExitAsync(process, output).ConfigureAwait(false);

            var client = new NamiApiContractClient(s_http, apiBase);
            var (trace, logs) = await WaitForReadbackAsync(client, ids.TraceId).ConfigureAwait(false);
            return AssertEvidence(trace, logs, ids, serviceName);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
        }
    }

    private static Process CreateProcess(
        string executable,
        Uri appBase,
        Uri otlpBase,
        string serviceName,
        ConcurrentQueue<string> output)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable)
                               ?? throw new InvalidOperationException("Conformance executable has no parent directory."),
        };

        foreach (var variable in startInfo.Environment.Keys
                     .Where(static key => key.StartsWith("OTEL_", StringComparison.Ordinal))
                     .ToArray())
        {
            startInfo.Environment.Remove(variable);
        }

        startInfo.Environment["ASPNETCORE_URLS"] = appBase.AbsoluteUri.TrimEnd('/');
        startInfo.Environment["OTEL_EXPORTER_OTLP_ENDPOINT"] = otlpBase.AbsoluteUri;
        startInfo.Environment["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf";
        startInfo.Environment["OTEL_SERVICE_NAME"] = serviceName;

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, eventArgs) => Capture("stdout", eventArgs.Data, output);
        process.ErrorDataReceived += (_, eventArgs) => Capture("stderr", eventArgs.Data, output);
        return process;
    }

    private static async Task<TraceAndSpanIds> DriveAppAsync(
        Process process,
        Uri appBase,
        ConcurrentQueue<string> output)
    {
        var endpoint = new Uri(appBase, "conformance");
        var elapsed = Stopwatch.StartNew();

        while (elapsed.Elapsed < s_startupTimeout)
        {
            if (process.HasExited)
                throw new InvalidOperationException(
                    $"Native AOT conformance app exited before accepting traffic.{FormatOutput(output)}");

            try
            {
                using var response = await s_http.GetAsync(endpoint).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        $"Conformance endpoint returned {(int)response.StatusCode}; " +
                        $"builder.AddNami() did not complete its inbound/outbound work. body={body}{FormatOutput(output)}");
                }

                if (!string.Equals(body, "nami-sdk-conformance-ok", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Conformance endpoint returned an unexpected body: {body}");

                var traceId = ReadIdHeader(response, TraceIdHeader, "Common.TraceId");
                var spanId = ReadIdHeader(response, SpanIdHeader, "Common.SpanId");
                return new TraceAndSpanIds(traceId, spanId);
            }
            catch (HttpRequestException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (!process.HasExited)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
            }
        }

        throw new TimeoutException(
            $"Native AOT conformance app did not listen at {endpoint} within {s_startupTimeout}.{FormatOutput(output)}");
    }

    private static async Task WaitForSuccessfulExitAsync(
        Process process,
        ConcurrentQueue<string> output)
    {
        using var timeout = new CancellationTokenSource(s_processExitTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(
                $"Native AOT conformance app did not stop and flush after one request.{FormatOutput(output)}");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Native AOT conformance app exited with code {process.ExitCode}.{FormatOutput(output)}");
        }
    }

    private static async Task<(ContractTrace Trace, CursorPageLogRecord Logs)> WaitForReadbackAsync(
        NamiApiContractClient client,
        string traceId)
    {
        var elapsed = Stopwatch.StartNew();
        ContractTrace? trace = null;

        while (elapsed.Elapsed < s_readbackTimeout)
        {
            trace ??= await client.TryGetTraceAsync(traceId).ConfigureAwait(false);
            var logs = await client.GetLogsAsync(traceId).ConfigureAwait(false);
            if (trace is not null && logs.Items.Any(log =>
                    string.Equals(log.EventName, WorkCompletedEventName, StringComparison.Ordinal)))
                return (trace, logs);

            await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
        }

        if (trace is null)
        {
            throw new InvalidOperationException(
                $"Missing trace {traceId} from /api/v1/traces/{traceId}; " +
                "expected builder.AddNami() to export the inbound server and outbound client spans.");
        }

        throw new InvalidOperationException(
            $"Missing {WorkCompletedEventName} log record correlated to trace {traceId} " +
            $"from /api/v1/logs?trace_id={traceId}.");
    }

    private static ConformanceEvidence AssertEvidence(
        ContractTrace trace,
        CursorPageLogRecord logs,
        TraceAndSpanIds ids,
        string serviceName)
    {
        if (!string.Equals(trace.TraceId, ids.TraceId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Product API returned trace {trace.TraceId}, expected {ids.TraceId}.");

        var server = trace.Spans.SingleOrDefault(span =>
            string.Equals(span.SpanId, ids.ServerSpanId, StringComparison.Ordinal));
        if (server is null)
            throw new InvalidOperationException($"Missing inbound server span {ids.ServerSpanId} in trace {ids.TraceId}.");
        if (server.Kind is not SpanKind.Server)
            throw new InvalidOperationException($"Inbound span {server.SpanId} has kind {server.Kind}, expected Server.");
        RequireAttribute(server, "http.request.method", "GET");
        RequireAttribute(server, "http.route", "/conformance");
        RequireService(server, serviceName);

        var clients = trace.Spans.Where(span =>
                span.Kind is SpanKind.Client &&
                string.Equals(span.ParentSpanId, server.SpanId, StringComparison.Ordinal))
            .ToArray();
        if (clients.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected one outbound client span parented by inbound span {server.SpanId}, got {clients.Length}.");
        }

        var client = clients[0];
        RequireAttribute(client, "http.request.method", "GET");
        RequireService(client, serviceName);

        if (!trace.Services.Contains(serviceName, StringComparer.Ordinal))
            throw new InvalidOperationException($"Trace services omit configured OTEL_SERVICE_NAME={serviceName}.");

        var correlatedLog = logs.Items.FirstOrDefault(log =>
            string.Equals(log.TraceId, trace.TraceId, StringComparison.Ordinal) &&
            string.Equals(log.EventName, WorkCompletedEventName, StringComparison.Ordinal) &&
            !string.IsNullOrEmpty(log.SpanId) &&
            string.Equals(log.Resource.ServiceName, serviceName, StringComparison.Ordinal));
        if (correlatedLog is null)
        {
            throw new InvalidOperationException(
                $"Missing {WorkCompletedEventName} log record correlated to trace {trace.TraceId} " +
                $"and service {serviceName}.");
        }

        if (!trace.Spans.Any(span => string.Equals(span.SpanId, correlatedLog.SpanId, StringComparison.Ordinal)))
            throw new InvalidOperationException(
                $"Correlated log span {correlatedLog.SpanId} is absent from trace {trace.TraceId}.");

        return new ConformanceEvidence(trace.TraceId, server.SpanId, client.SpanId, correlatedLog.SpanId!);
    }

    private static void RequireAttribute(ContractSpan span, string key, string expected)
    {
        if (!HasAttribute(span, key, expected))
            throw new InvalidOperationException($"Span {span.SpanId} is missing {key}={expected}.");
    }

    private static bool HasAttribute(ContractSpan span, string key, string expected)
    {
        var attribute = span.Attributes?.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));
        return attribute?.Value is Nami.Api.Contracts.Common.AttributeValue.StringValue { Value: var value } &&
               string.Equals(value, expected, StringComparison.Ordinal);
    }

    private static void RequireService(ContractSpan span, string serviceName)
    {
        if (!string.Equals(span.Resource.ServiceName, serviceName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Span {span.SpanId} resource service.name={span.Resource.ServiceName}, expected {serviceName}.");
        }
    }

    private static string ReadIdHeader(HttpResponseMessage response, string header, string contract)
    {
        if (!response.Headers.TryGetValues(header, out var values))
        {
            throw new InvalidOperationException(
                $"Conformance response is missing {header}; expected builder.AddNami() to create the inbound span.");
        }

        var value = values.Single();
        if (Nami.Api.Contracts.ContractJson.Validate(contract, System.Text.Json.Nodes.JsonValue.Create(value)) is not null)
            throw new InvalidOperationException($"Conformance response {header} is not a valid lowercase OTLP id: {value}");
        return value;
    }

    private static Uri RequireAbsoluteHttpUri(string value, string label)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"The {label} must be an absolute HTTP URI: {value}");
        }

        return uri;
    }

    private static void Capture(string stream, string? line, ConcurrentQueue<string> output)
    {
        if (line is not null)
            output.Enqueue($"{stream}: {line}");
    }

    private static string FormatOutput(IEnumerable<string> output)
    {
        var lines = output.ToArray();
        return lines.Length == 0 ? "" : Environment.NewLine + string.Join(Environment.NewLine, lines);
    }

    private sealed record TraceAndSpanIds(string TraceId, string ServerSpanId);

    private sealed record ConformanceEvidence(
        string TraceId,
        string ServerSpanId,
        string ClientSpanId,
        string LogSpanId);
}

internal sealed class NamiApiContractClient(HttpClient http, Uri apiBase)
{
    internal async Task<ContractTrace?> TryGetTraceAsync(string traceId)
    {
        var path = $"api/v1/traces/{Uri.EscapeDataString(traceId)}";
        using var response = await http.GetAsync(new Uri(apiBase, path)).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound)
            return null;
        return await ReadContractAsync<ContractTrace>(response, path).ConfigureAwait(false);
    }

    internal async Task<CursorPageLogRecord> GetLogsAsync(string traceId)
    {
        var path = $"api/v1/logs?trace_id={Uri.EscapeDataString(traceId)}&limit=100";
        using var response = await http.GetAsync(new Uri(apiBase, path)).ConfigureAwait(false);
        return await ReadContractAsync<CursorPageLogRecord>(response, path)
            .ConfigureAwait(false);
    }

    private static async Task<T> ReadContractAsync<T>(
        HttpResponseMessage response,
        string path)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Product API {path} returned {(int)response.StatusCode}: {error}");
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Product API {path} returned content type {mediaType ?? "<missing>"}.");

        return Nami.Api.Contracts.ContractJson.Deserialize<T>(await response.Content.ReadAsStringAsync().ConfigureAwait(false))
               ?? throw new InvalidOperationException($"Product API {path} returned an empty contract body.");
    }
}
