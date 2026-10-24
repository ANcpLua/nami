using System.Globalization;
using System.Text.Json.Nodes;
namespace Nami.Tests.Infrastructure;

internal static class OtlpData
{
    internal const string TraceId = "0123456789abcdef0123456789abcdef";
    internal const string SpanId = "0123456789abcdef";
    internal static JsonObject Attribute(string key, string value) => new() { ["key"] = key, ["value"] = new JsonObject { ["stringValue"] = value } };
    internal static JsonObject Resource(string? service, string project)
    {
        var attrs = new JsonArray(Attribute("nami.project.id", project));
        if (service is not null) attrs.Add((JsonNode)Attribute("service.name", service));
        return new() { ["attributes"] = attrs };
    }
    internal static string Traces(ulong start, int count = 1, int offset = 0, string project = "default", string? service = "checkout", JsonArray? attributes = null, bool error = false, bool exceptionEvent = false, string operation = "GET /orders")
    {
        var spans = new JsonArray();
        for (var i = 0; i < count; i++)
        {
            var span = new JsonObject
            {
                ["traceId"] = (offset + i + 1).ToString("x32", CultureInfo.InvariantCulture),
                ["spanId"] = (offset + i + 1).ToString("x16", CultureInfo.InvariantCulture),
                ["name"] = operation, ["kind"] = 2,
                ["startTimeUnixNano"] = start.ToString(CultureInfo.InvariantCulture),
                ["endTimeUnixNano"] = (start + 1000000UL + (ulong)i).ToString(CultureInfo.InvariantCulture),
                ["status"] = new JsonObject { ["code"] = error ? 2 : 0 },
                ["attributes"] = attributes?.DeepClone() ?? new JsonArray(Attribute("http.request.method", "GET"))
            };
            if (exceptionEvent) span["events"] = new JsonArray(new JsonObject
            {
                ["name"] = "exception", ["timeUnixNano"] = start.ToString(CultureInfo.InvariantCulture),
                ["attributes"] = new JsonArray(Attribute("exception.type", "OrderFailure"), Attribute("exception.message", "declined"), Attribute("exception.stacktrace", "SensitiveStackTrace"))
            });
            spans.Add((JsonNode)span);
        }
        return new JsonObject { ["resourceSpans"] = new JsonArray(new JsonObject { ["resource"] = Resource(service, project),
            ["scopeSpans"] = new JsonArray(new JsonObject { ["scope"] = new JsonObject { ["name"] = "nami-tests" }, ["spans"] = spans }) }) }.ToJsonString();
    }
    internal static string Logs(ulong time, string body = "order accepted", string project = "default", string? service = "checkout", bool exception = false, string? trace = null, string? span = null)
    {
        var log = new JsonObject { ["timeUnixNano"] = time.ToString(CultureInfo.InvariantCulture), ["severityNumber"] = exception ? 17 : 9,
            ["severityText"] = exception ? "ERROR" : "INFO", ["body"] = new JsonObject { ["stringValue"] = body } };
        if (trace is not null) log["traceId"] = trace;
        if (span is not null) log["spanId"] = span;
        if (exception) { log["eventName"] = "exception"; log["attributes"] = new JsonArray(Attribute("exception.type", "OrderFailure")); }
        return new JsonObject { ["resourceLogs"] = new JsonArray(new JsonObject { ["resource"] = Resource(service, project),
            ["scopeLogs"] = new JsonArray(new JsonObject { ["logRecords"] = new JsonArray(log) }) }) }.ToJsonString();
    }
    internal static string Metrics(ulong time, string project = "default", string? service = "checkout") => new JsonObject
    {
        ["resourceMetrics"] = new JsonArray(new JsonObject { ["resource"] = Resource(service, project),
            ["scopeMetrics"] = new JsonArray(new JsonObject { ["metrics"] = new JsonArray(new JsonObject
            { ["name"] = "http.server.active_requests", ["gauge"] = new JsonObject { ["dataPoints"] = new JsonArray(new JsonObject
                { ["timeUnixNano"] = time.ToString(CultureInfo.InvariantCulture), ["asInt"] = "1" }) } }) }) })
    }.ToJsonString();
}
