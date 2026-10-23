using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nami.ToolSmoke;

/// <summary>
/// ci_log dogfooding emitter: mirrors the smoke's phase markers as OTLP/JSON spans
/// so the MCP server's ci_log tool can answer "which leg hung on what" from nami's own
/// telemetry. Convention (must match ci_log in services/nami.mcp): resource service.name
/// "nami-ci-smoke", session.id = one smoke run, one span per phase carrying a
/// cicd.pipeline.task.name attribute, failures as span status error.
///
/// Entirely inert unless NAMI_CI_OTLP_ENDPOINT is set, and a failed export never
/// fails the smoke — telemetry about the smoke must not become a new way to break it.
/// </summary>
internal static class CiTelemetry
{
    private static readonly string? s_endpoint =
        NonEmpty(Environment.GetEnvironmentVariable("NAMI_CI_OTLP_ENDPOINT"));

    // The hosted collector guards /v1/traces with ApiKeyAuth (header x-otlp-api-key,
    // per the published OpenAPI); local collectors don't require it.
    private static readonly string? s_apiKey =
        NonEmpty(Environment.GetEnvironmentVariable("NAMI_CI_API_KEY"));

    private static readonly string s_runId =
        NonEmpty(Environment.GetEnvironmentVariable("NAMI_CI_RUN_ID")) ?? $"local-{Environment.MachineName}";

    private static readonly string s_leg =
        NonEmpty(Environment.GetEnvironmentVariable("NAMI_CI_LEG"))
        ?? System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;

    private static readonly string s_traceId = RandomHex(16);

    private static readonly JsonSerializerOptions s_exportOptions =
        new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private static readonly List<(string Name, long StartNano, long EndNano, bool Ok, string? Message)> s_phases = [];
    private static string? s_openPhase;
    private static long s_openPhaseStart;

    private static string? NonEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static long NowNano() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000;

    private static string RandomHex(int bytes) =>
        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(bytes));

    /// <summary>Closes the previous phase as ok and opens the next one.</summary>
    public static void Mark(string message)
    {
        if (s_endpoint is null) return;
        var now = NowNano();
        if (s_openPhase is not null) s_phases.Add((s_openPhase, s_openPhaseStart, now, true, null));
        s_openPhase = message;
        s_openPhaseStart = now;
    }

    /// <summary>Closes the open phase with the run's outcome and exports all spans.</summary>
    public static async Task FlushAsync(bool success, string? failure)
    {
        if (s_endpoint is null) return;
        var now = NowNano();
        if (s_openPhase is not null)
        {
            s_phases.Add((s_openPhase, s_openPhaseStart, now, success, success ? null : failure));
            s_openPhase = null;
        }
        if (s_phases.Count == 0) return;

        var spans = s_phases.Select(phase => new
        {
            traceId = s_traceId,
            spanId = RandomHex(8),
            name = phase.Name,
            kind = 1,
            startTimeUnixNano = phase.StartNano.ToString(CultureInfo.InvariantCulture),
            endTimeUnixNano = phase.EndNano.ToString(CultureInfo.InvariantCulture),
            attributes = new[]
            {
                new { key = "cicd.pipeline.task.name", value = new { stringValue = s_leg } },
                new { key = "session.id", value = new { stringValue = s_runId } }
            },
            status = phase.Ok
                ? new { code = 1, message = (string?)null }
                : new { code = 2, message = phase.Message }
        }).ToArray();

        // WhenWritingNull keeps ok-status spans free of "message": null — strict
        // OTLP/JSON parsers reject explicit nulls.
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
                            // The leg lives in service.name too: the collector does not yet
                            // persist span attributes on readback, and per-leg grouping must
                            // survive that (cicd.pipeline.task.name stays for when it does).
                            new { key = "service.name", value = new { stringValue = $"nami-ci-smoke-{s_leg}" } },
                            new { key = "session.id", value = new { stringValue = s_runId } }
                        }
                    },
                    scopeSpans = new[] { new { scope = new { name = "nami-ci-smoke" }, spans } }
                }
            }
        }, s_exportOptions);

        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            if (s_apiKey is not null) client.DefaultRequestHeaders.Add("x-otlp-api-key", s_apiKey);
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var target = $"{s_endpoint.TrimEnd('/')}/v1/traces";
            using var response = await client.PostAsync(target, content);
            Console.WriteLine(response.IsSuccessStatusCode
                ? $"[ci-telemetry] exported {spans.Length} phase span(s) for {s_runId}/{s_leg}"
                : $"[ci-telemetry] export rejected with {(int)response.StatusCode} (ignored)");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            Console.WriteLine($"[ci-telemetry] export failed: {exception.Message} (ignored)");
        }
    }
}
