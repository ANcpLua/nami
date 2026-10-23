using System.Text.Json.Nodes;
using Nami.Api.Contracts.Mcp.Tools;
using Nami.Collector.Storage;

namespace Nami.Collector.Analysis;

internal sealed partial class AnalysisEngine
{
    // The run's own window and page override the saved filter; nothing else of the request does.
    private static readonly string[] s_runOverrides = ["lookback_minutes", "start_time", "end_time", "limit", "cursor"];

    // The input a saved query's run answers: its saved filter, named by its id, under the run's own window and page.
    internal static JsonObject SavedQueryInput(SavedQueryRow row, JsonObject request)
    {
        var input = JsonNode.Parse(row.FilterJson)!.AsObject();
        input["id"] = row.SavedQueryId;
        foreach (var name in s_runOverrides) if (request[name] is { } value) input[name] = value.DeepClone();
        return input;
    }

    internal static AnalysisInputException NoSavedQuery(string id) =>
        new("id", $"Saved query {id} was not found in this project; run_saved_query without id lists the saved queries.", "saved_query.not_found") { Missing = id };

    // Without an id, the project's saved queries; with one, that query answered over current telemetry.
    private JsonObject RunSavedQuery(JsonObject input)
    {
        if (J.String(input, "id") is not { } id)
            return Page(input, "saved_queries", snapshot.SavedQueries.Where(row => J.String(input, "signal") is not { } signal || row.Signal == signal)
                .OrderBy(row => row.Name, StringComparer.Ordinal).ThenBy(row => row.SavedQueryId, StringComparer.Ordinal).Select(SavedQueryJson), snapshot.SavedQueries.Count);
        var row = snapshot.SavedQueries.FirstOrDefault(row => row.SavedQueryId == id) ?? throw NoSavedQuery(id);
        var filter = SavedQueryInput(row, input);
        _window = AnalysisWindow.Resolve(filter, now);
        if (J.String(filter, "cursor") is { } cursor && handles.Read(cursor, project, "cursor")["window"] is JsonObject window) _window = AnalysisWindow.Resolve(window, now);
        var output = J.O(("saved_query", SavedQueryJson(row)));
        if (row.Signal == "metrics")
        {
            // A run's limit is the contract's 1 to 1000; a metric answer holds at most query_metric's series.
            if (J.Number(filter, "limit") > QueryMetricInput.LimitMaximum) filter["limit"] = QueryMetricInput.LimitMaximum;
            var metric = QueryMetric(filter);
            output["coverage"] = metric["coverage"]?.DeepClone();
            metric.Remove("coverage");
            output["metric"] = metric;
            return output;
        }
        var page = row.Signal == "traces" ? Traces(filter) : Logs(filter);
        // The run covers what its saved filter examined and matched, not the bare id request.
        foreach (var field in new[] { row.Signal, "has_more", "next_cursor", "coverage" }) output[field] = page[field]?.DeepClone();
        return output;
    }

    private static JsonObject SavedQueryJson(SavedQueryRow row) =>
        JsonSerializer.SerializeToNode(SavedQueryMapper.ToContract(row), global::Nami.Api.Contracts.ContractTypes.TypeInfo<global::Nami.Api.Contracts.SavedQueries.SavedQuery>())!.AsObject();
}
