using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Nami.Api.Contracts;
using Nami.Collector.ApiKeys;
using Nami.Collector.Ingestion.Primitives;
using Nami.Collector.Telemetry;
using Nami.Tools;

namespace Nami.Collector.Mcp;

internal static class NamiMcpServer
{
    private static readonly (string Name, string Description)[] s_definitions =
    [
        ("changes_recent", "Resource revisions, versions, environments and pipeline runs that changed in the window."),
        ("errors_recent", "Errors grouped by type and operation, with counts and example trace IDs."),
        ("logs_search", "Log records containing the exact, case-sensitive query text."),
        ("sources_status", "Telemetry sources and the last receipt time or silence duration of each signal."),
        ("traces_get", "One trace, projected to the requested fields."),
        ("traces_slowest", "The slowest traces in the window, optionally filtered by operation.")
    ];
    private static readonly Tool[] s_tools = s_definitions.Select(static definition => new Tool
    {
        Name = definition.Name,
        Description = definition.Description,
        InputSchema = LoadSchema(definition.Name, "input"),
        OutputSchema = LoadSchema(definition.Name, "result"),
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = true, IdempotentHint = true, DestructiveHint = false, OpenWorldHint = false
        }
    }).ToArray();

    public static IServiceCollection AddNamiMcp(this IServiceCollection services, bool stdio)
    {
        var mcp = services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation { Name = "nami", Version = BuildVersion.ProductVersion };
            options.ProtocolVersion = "2026-07-28";
        }).WithListToolsHandler(static (_, _) => ValueTask.FromResult(new ListToolsResult
        {
            Tools = s_tools, TimeToLive = TimeSpan.FromHours(1), CacheScope = CacheScope.Public, ResultType = "complete"
        })).WithCallToolHandler(CallAsync);
        if (stdio) mcp.WithStdioServerTransport();
        else mcp.WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless);
        return services;
    }

    public static void MapNamiMcp(this WebApplication app) => app.MapMcp("/mcp");

    private static async ValueTask<CallToolResult> CallAsync(
        RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
    {
        var parameters = request.Params ?? throw new McpProtocolException("Missing tool parameters.", McpErrorCode.InvalidParams);
        if (!s_definitions.Any(d => d.Name == parameters.Name))
            throw new McpProtocolException("Unknown tool.", McpErrorCode.InvalidParams);
        var arguments = new JsonObject();
        if (parameters.Arguments is { } supplied)
            foreach (var pair in supplied) arguments[pair.Key] = JsonNode.Parse(pair.Value.GetRawText());
        var input = ContractJson.ToElement(arguments);
        var services = request.Services ?? throw new InvalidOperationException("MCP service scope is missing.");
        var http = services.GetRequiredService<IHttpContextAccessor>().HttpContext;
        var project = http is null ? ProjectIdentity.DefaultProjectId : AuthenticatedProject.ForHttpRead(http);
        var parent = MetaString(parameters.Meta, "traceparent");
        var state = MetaString(parameters.Meta, "tracestate");
        var previousActivity = Activity.Current;
        if (!ActivityContext.TryParse(parent, state, out var parentContext)) Activity.Current = null;
        var activity = NamiTelemetry.Source.StartActivity("tools/call " + parameters.Name, ActivityKind.Server, parentContext);
        activity?.SetTag("mcp.method.name", "tools/call");
        activity?.SetTag("gen_ai.tool.name", parameters.Name);
        var started = Stopwatch.GetTimestamp();
        try
        {
            var service = services.GetRequiredService<NamiToolService>();
            var result = await service.InvokeAsync(parameters.Name, input, project, cancellationToken).ConfigureAwait(false);
            if (result.IsError)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                if (result.Content.TryGetProperty("code", out var errorCode))
                    activity?.SetTag(Nami.SemanticConventions.ErrorAttributes.Type, errorCode.GetString());
            }
            var json = result.Content.GetRawText();
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = Headline(parameters.Name, result.Content, result.IsError) },
                    new TextContentBlock { Text = json }],
                StructuredContent = result.Content,
                IsError = result.IsError,
                ResultType = "complete",
                Meta = new JsonObject { ["contract_revision"] = ContractRevision.Value,
                    ["io.modelcontextprotocol/serverInfo"] = new JsonObject
                    { ["name"] = "nami", ["version"] = BuildVersion.ProductVersion } }
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag(Nami.SemanticConventions.ErrorAttributes.Type, exception.GetType().FullName);
            NamiTelemetry.RecordException(exception, services.GetRequiredService<ILoggerFactory>().CreateLogger("nami"));
            throw;
        }
        finally
        {
            NamiTelemetry.Duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds,
                new KeyValuePair<string, object?>("mcp.method.name", "tools/call"),
                new KeyValuePair<string, object?>("gen_ai.tool.name", parameters.Name));
            activity?.Dispose();
            Activity.Current = previousActivity;
        }
    }

    private static string? MetaString(JsonObject? meta, string key) =>
        meta?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string Headline(string tool, JsonElement result, bool error)
    {
        if (error) return result.TryGetProperty("message", out var message) ? message.GetString()! : "Tool failed.";
        var freshness = result.GetProperty("freshness");
        var complete = freshness.GetProperty("complete").GetBoolean();
        var returned = freshness.GetProperty("returned");
        var summaries = new List<string>();
        foreach (var member in result.EnumerateObject())
        {
            if (member.Name == "trace") summaries.Add(Describe(member.Value));
            else if (member.Value.ValueKind == JsonValueKind.Array)
                summaries.AddRange(member.Value.EnumerateArray().Take(3).Select(Describe));
        }
        var next = result.TryGetProperty("next_cursor", out var cursor) && cursor.ValueKind == JsonValueKind.String
            ? $"Repeat {tool} with next_cursor as cursor."
            : tool == "sources_status" ? "Next: errors_recent." : "Next: traces_get for a listed trace_id.";
        return $"{tool}: {returned} results; {(complete ? "complete window" : "incomplete window; ask again after ingestion catches up")}. " +
            string.Join("; ", summaries.Where(static s => s.Length > 0)) + " " + next;
    }

    private static string Describe(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object) return "";
        return string.Join(", ", row.EnumerateObject().Where(static p => p.Name is
                "service" or "trace_id" or "example_trace_id" or "error_type" or "operation" or "kind" or "count")
            .Take(4).Select(static p => p.Name + "=" + (p.Value.GetRawText() is { Length: > 96 } text ? text[..96] + "…" : p.Value.GetRawText())));
    }

    private static JsonElement LoadSchema(string name, string kind)
    {
        using var stream = typeof(NamiMcpServer).Assembly.GetManifestResourceStream($"Schemas.{name}.{kind}.json")
            ?? throw new InvalidOperationException($"Missing generated schema: {name}.{kind}.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }
}
