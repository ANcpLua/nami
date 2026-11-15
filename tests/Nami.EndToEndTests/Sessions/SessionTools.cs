using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Nami.Tests.Infrastructure;

namespace Nami.EndToEndTests.Sessions;

/// <summary>Runs nami on a capture and calls its tools the way an agent does: OTLP/HTTP protobuf in, MCP out.</summary>
internal static class SessionTools
{
    /// <summary>
    ///     The capture is from 15 Nov 2026; the default retention of 30 days would delete it ten seconds after start
    ///     once it is older. Retention is not what these tests prove.
    /// </summary>
    private static readonly Dictionary<string, string?> s_retentionOff = new() { ["NAMI_RETENTION_DAYS"] = "0" };

    private static readonly string[] s_sessionTools = [SessionsContract.SearchTool, SessionsContract.GetTool];

    /// <summary>Starts nami in <paramref name="directory" /> and posts every body in order; each must be accepted.</summary>
    internal static async Task<McpHarness> IngestAsync(string directory, IReadOnlyList<OtlpBody> bodies,
        CancellationToken cancellationToken)
    {
        var host = await StartAsync(directory, cancellationToken);
        try
        {
            foreach (var body in bodies)
            {
                using var content = new ByteArrayContent(body.Payload);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
                using var response = await host.Http.PostAsync(
                    $"http://127.0.0.1:{host.OtlpPort}/v1/{body.Signal}", content, cancellationToken);
                if (response.StatusCode != HttpStatusCode.OK)
                    Assert.Fail($"nami refused {body.Name} ({body.Signal}, session {body.Session}) with " +
                                $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
            }

            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    /// <summary>Starts nami on an existing data directory without posting anything.</summary>
    internal static Task<McpHarness> StartAsync(string directory, CancellationToken cancellationToken) =>
        McpHarness.StartAsync(TestData.HostBinary, false, directory, cancellationToken, environment: s_retentionOff);

    /// <summary>Fails with the tool list when M2's two tools are not offered.</summary>
    internal static async Task RequireSessionToolsAsync(McpHarness host, CancellationToken cancellationToken)
    {
        var tools = await host.Client.ListToolsAsync(cancellationToken: cancellationToken);
        var names = tools.Select(static tool => tool.Name).ToArray();
        var missing = s_sessionTools.Where(name => !names.Contains(name, StringComparer.Ordinal)).ToArray();
        if (missing.Length > 0)
            Assert.Fail($"M2 has not delivered {string.Join(" and ", missing)}: tools/list offers {string.Join(", ", names)}.");
    }

    /// <summary>Calls a tool that must answer; a refusal fails the test with the refusal.</summary>
    internal static async Task<JsonElement> CallAsync(McpHarness host, string tool, JsonObject arguments,
        CancellationToken cancellationToken)
    {
        var result = await host.CallAsync(tool, arguments.ToJsonString(), cancellationToken);
        var content = Structured(result, tool);
        if (result.IsError == true)
            Assert.Fail($"{tool} refused {arguments.ToJsonString()}: {content.GetRawText()}");
        return content;
    }

    /// <summary>Calls a tool that must refuse, and returns the typed refusal.</summary>
    internal static async Task<JsonElement> RefusalAsync(McpHarness host, string tool, JsonObject arguments,
        CancellationToken cancellationToken)
    {
        var result = await host.CallAsync(tool, arguments.ToJsonString(), cancellationToken);
        var content = Structured(result, tool);
        if (result.IsError != true)
            Assert.Fail($"{tool} answered {arguments.ToJsonString()} instead of refusing: {content.GetRawText()}");
        return content;
    }

    internal static JsonElement Structured(CallToolResult result, string tool)
    {
        return (object?)result.StructuredContent is JsonElement json
            ? json
            : throw new InvalidOperationException($"{tool} returned no structured content.");
    }

    internal static JsonObject Search(string since, int maxBytes = 262144) =>
        new() { ["since"] = since, ["max_bytes"] = maxBytes };

    internal static JsonObject Get(string sessionId, int maxBytes = 262144) =>
        new() { [SessionsContract.SessionId] = sessionId, ["max_bytes"] = maxBytes };

    /// <summary>The structured result's UTF-8 size, which <c>max_bytes</c> bounds.</summary>
    internal static int Bytes(JsonElement content) => Encoding.UTF8.GetByteCount(content.GetRawText());

    /// <summary>The one session of a sessions_search result with this id, failing with the ids it did return.</summary>
    internal static JsonElement SessionById(JsonElement result, string id)
    {
        var sessions = result.GetProperty(SessionsContract.SessionsMember).EnumerateArray().ToArray();
        var matches = sessions.Where(s => Text(s, SessionsContract.SessionId) == id).ToArray();
        if (matches.Length != 1)
            Assert.Fail($"Expected one session {id}; sessions_search returned {matches.Length} of " +
                        $"[{string.Join(", ", sessions.Select(static s => Text(s, SessionsContract.SessionId)))}].");
        return matches[0];
    }

    internal static string? Text(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    /// <summary>A member that must be present, failing with the members that are.</summary>
    internal static JsonElement Member(JsonElement value, string name, string where)
    {
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var member)) return member;
        Assert.Fail($"{where} has no member {name}: {Shorten(value.GetRawText())}");
        return default;
    }

    internal static string Shorten(string text) => text.Length <= 2000 ? text : text[..2000] + "...";
}
