using Nami.Api.Contracts;
using Nami.Api.Contracts.Common;
using Nami.Api.Contracts.Tools;
using Nami.Collector.Storage;

namespace Nami.Tools;

internal static class TypedToolRunner
{
    internal static Result<TOutput, ToolError> Run<TInput, TOutput>(string name, ISnapshot snapshot, TInput input, ToolClock clock)
    {
        var result = ToolDispatcher.Run(name, snapshot, ContractJson.Serialize(input), clock);
        return result.IsError
            ? new(default, ContractJson.Deserialize<ToolError>(result.Content))
            : new(ContractJson.Deserialize<TOutput>(result.Content), null);
    }
}

internal static partial class SourcesStatusTool
{
    public static Result<SourcesStatusResult, ToolError> Run(ISnapshot snapshot, SourcesStatusInput input, ToolClock clock) =>
        TypedToolRunner.Run<SourcesStatusInput, SourcesStatusResult>("sources_status", snapshot, input, clock);
}

internal static partial class ErrorsRecentTool
{
    public static Result<ErrorsRecentResult, ToolError> Run(ISnapshot snapshot, ErrorsRecentInput input, ToolClock clock) =>
        TypedToolRunner.Run<ErrorsRecentInput, ErrorsRecentResult>("errors_recent", snapshot, input, clock);
}

internal static partial class TracesGetTool
{
    public static Result<TracesGetResult, ToolError> Run(ISnapshot snapshot, TracesGetInput input, ToolClock clock) =>
        TypedToolRunner.Run<TracesGetInput, TracesGetResult>("traces_get", snapshot, input, clock);
}

internal static partial class TracesSlowestTool
{
    public static Result<TracesSlowestResult, ToolError> Run(ISnapshot snapshot, TracesSlowestInput input, ToolClock clock) =>
        TypedToolRunner.Run<TracesSlowestInput, TracesSlowestResult>("traces_slowest", snapshot, input, clock);
}

internal static partial class LogsSearchTool
{
    public static Result<LogsSearchResult, ToolError> Run(ISnapshot snapshot, LogsSearchInput input, ToolClock clock) =>
        TypedToolRunner.Run<LogsSearchInput, LogsSearchResult>("logs_search", snapshot, input, clock);
}

internal static partial class ChangesRecentTool
{
    public static Result<ChangesRecentResult, ToolError> Run(ISnapshot snapshot, ChangesRecentInput input, ToolClock clock) =>
        TypedToolRunner.Run<ChangesRecentInput, ChangesRecentResult>("changes_recent", snapshot, input, clock);
}
