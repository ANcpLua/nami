using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Mvc;
using Nami.Api.Contracts;
using ContractJson = Nami.Api.Contracts.ContractJson;
using Nami.Api.Contracts.Mcp.Tools;
using Nami.Collector.Analysis;
using Nami.Collector.Storage;

namespace Nami.Collector.Hosting;

internal static partial class CollectorEndpointExtensions
{
    private static Task<IResult> AnalyzeReadEndpointAsync(HttpContext context, INamiStore store, TimeProvider time, CancellationToken ct)
    {
        var endpoint = context.GetEndpoint() ?? throw new InvalidOperationException("An analysis route requires an endpoint.");
        var operation = endpoint.Metadata.GetRequiredMetadata<IEndpointNameMetadata>().EndpointName;
        return AnalyzeAsync(operation, context, store, time, ct);
    }

    private static AnalysisHandles CreateAnalysisHandles(HttpContext context, string project) =>
        context.RequestServices.GetRequiredService<AnalysisSigningKey>().ForProject(project);
    private static async Task<IResult> AnalyzeAsync(string operation, HttpContext context, INamiStore store, TimeProvider time, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(55));
        try
        {
            var node = await JsonNode.ParseAsync(context.Request.Body, cancellationToken: deadline.Token).ConfigureAwait(false);
            if (node is not JsonObject input) return ResourceHttp.InvalidBody("The request body must be a JSON object.");
            ValidateAnalysisInput(operation, input);
            var project = ResolveProjectScope(context);
            var now = time.GetUtcNow(); var handles = CreateAnalysisHandles(context, project);
            if (operation == "query_metric" && J.Bool(input, "raw"))
                return AnalysisResult(operation, await RawMetricPointsAsync(input, store, project, handles, now, deadline.Token));
            var readInput = input;
            if (operation == "run_saved_query" && J.String(input, "id") is { } savedId)
            {
                if (await store.GetSavedQueryAsync(project, savedId, deadline.Token).ConfigureAwait(false) is not { } saved)
                    return AnalysisError(AnalysisEngine.NoSavedQuery(savedId));
                readInput = AnalysisEngine.SavedQueryInput(saved, input);
            }
            if (operation == "get_failure_context" && J.String(input, "failure_id") is { } failureId)
            {
                // A failure is explained in the trace of its latest occurrence. Its error spans name that trace, which is
                // then read like a trace_id request, so the answer never waits on the project's whole history.
                var errorsQuery = AnalysisReadPlan.Resolve(operation, input, now, handles, project) with { ErrorsOnly = true, Tables = AnalysisTables.Spans };
                var errors = await store.ReadAnalysisSnapshotAsync(project, errorsQuery, deadline.Token).ConfigureAwait(false);
                if (new AnalysisEngine(errors, project, handles, now, deadline.Token).LatestOccurrenceTrace(failureId) is not { } trace)
                    return errors.ScanComplete ? AnalysisError(AnalysisEngine.NoFailure(failureId)) : ContractErrorResults.ServiceUnavailable("analysis.scan_budget_exceeded");
                readInput = input.DeepClone().AsObject();
                readInput["trace_id"] = trace;
            }
            var query = AnalysisReadPlan.Resolve(operation, readInput, now, handles, project);
            var snapshot = await store.ReadAnalysisSnapshotAsync(project, query, deadline.Token).ConfigureAwait(false);
            var result = new AnalysisEngine(snapshot, project, handles, now, deadline.Token).Run(operation, input);
            // The generated contract validator is applied below before every result leaves this boundary.
            try { return AnalysisResult(operation, result); }
            catch (JsonException invalid)
            {
                LogInvalidAnalysisResponse(context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Nami.Collector.Analysis"), operation, invalid);
                return ContractErrorResults.InternalServerError("analysis.response_invalid");
            }
        }
        catch (MetricQueryConflictException conflict) { return ContractErrorResults.Conflict("metric", conflict.Message); }
        catch (AnalysisInputException invalid) { return AnalysisError(invalid); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return ContractErrorResults.AnalysisDeadlineExceeded(); }
        catch (JsonException) { return ResourceHttp.InvalidBody("The request body is not valid JSON."); }
    }

    // A *.not_found code answers 404 naming the missing resource; any other rejected argument 400 with its
    // field, the values it accepts and a hint.
    internal static IResult AnalysisError(AnalysisInputException invalid) => invalid.Code.EndsWith(".not_found", StringComparison.Ordinal)
        ? ContractErrorResults.NotFound(invalid.Code[..^".not_found".Length], invalid.Missing, invalid.Description)
        : ContractErrorResults.Validation(invalid.Field, invalid.Description, invalid.Code, invalid.RejectedValue, invalid.Allowed, invalid.Hint);

    private static (JsonTypeInfo Input, JsonTypeInfo Output) AnalysisContract(string operation)
    {
        var (input, output) = ContractTypes.Analysis(operation);
        return (ContractTypes.TypeInfo(input), ContractTypes.TypeInfo(output));
    }

    private static void ValidateAnalysisInput(string operation, JsonObject input)
    {
        var contract = AnalysisContract(operation);
        CheckContract(input, contract.Input);
    }

    private static void CheckContract(JsonObject body, JsonTypeInfo type)
    {
        if (ContractJson.Validate(ContractTypes.NameOf(type.Type), body) is not { } issue) return;
        var code = issue.Keyword switch
        {
            "minimum" or "maximum" or "exclusiveMinimum" or "exclusiveMaximum" => "out_of_range",
            "minLength" or "maxLength" or "minItems" or "maxItems" => "length",
            "enum" or "const" => "invalid",
            "additionalProperties" => "unknown",
            _ => issue.Keyword
        };
        throw new AnalysisInputException(issue.Field, issue.Message, System.Text.RegularExpressions.Regex.Replace(issue.Field, @"\[\d+\]", "[]") + "." + code)
        {
            Allowed = issue.Allowed,
            RejectedValue = issue.RejectedValue,
            Hint = issue.Hint ?? $"Correct {issue.Field}."
        };
    }

    private static async Task<(T? Value, IResult? Error)> ReadResourceBodyAsync<T>(HttpRequest request, JsonTypeInfo<T> type,
        CancellationToken ct) where T : class
    {
        try
        {
            if (await JsonNode.ParseAsync(request.Body, cancellationToken: ct).ConfigureAwait(false) is not JsonObject body)
                return (null, ResourceHttp.InvalidBody("The request body must be a JSON object."));
            CheckContract(body, type);
            return (body.Deserialize(type), null);
        }
        catch (AnalysisInputException invalid) { return (null, AnalysisError(invalid)); }
        catch (JsonException) { return (null, ResourceHttp.InvalidBody("The request body is not valid JSON.")); }
    }

    private static async Task<IResult?> RefuseBodyAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength is null or 0 && !request.Headers.TransferEncoding.Any(static encoding => encoding is { Length: > 0 })) return null;
        try
        {
            var body = await JsonNode.ParseAsync(request.Body, cancellationToken: ct).ConfigureAwait(false);
            if (body is JsonObject members)
                return members.FirstOrDefault() is { Key: { } name }
                    ? AnalysisError(new AnalysisInputException(name, "is not accepted here", name + ".unknown")) : null;
            return ResourceHttp.InvalidBody("This request takes no body.");
        }
        catch (JsonException) { return ResourceHttp.InvalidBody("This request takes no body."); }
    }

    // A string or an array outside the length the contract bounds its member to is refused naming that length: a string
    // counts its code points, as JSON Schema does, and an array its items. This is the one place a length or an item
    // count of a request body applies.
    private static void CheckLength(JsonNode? sent, string field, string path, (int? Minimum, int? Maximum) bounds)
    {
        static string Count(int count, string unit) => $"{count} {unit}{(count == 1 ? "" : "s")}";
        var (length, unit) = sent switch
        {
            JsonArray items => (items.Count, "item"),
            JsonValue value when value.GetValueKind() == JsonValueKind.String => (value.GetValue<string>().EnumerateRunes().Count(), "character"),
            _ => (-1, "")
        };
        if (length < 0 || length >= (bounds.Minimum ?? 0) && length <= (bounds.Maximum ?? int.MaxValue)) return;
        var range = (bounds.Minimum, bounds.Maximum) switch
        {
            ({ } low, { } high) when low == high => $"exactly {Count(high, unit)}",
            ({ } low, { } high) => $"{low} to {Count(high, unit)}",
            ({ } low, null) => $"at least {Count(low, unit)}",
            _ => $"at most {Count(bounds.Maximum!.Value, unit)}"
        };
        throw new AnalysisInputException(field, unit == "item" ? $"Use {field} with {range}." : $"Use {field} of {range}.", path + ".length") { Hint = $"Correct {field}." };
    }

    // A string that does not match the pattern the contract gives its member is refused naming the pattern, in the words of
    // the MCP's input schema; a malformed id is refused here rather than answered as an id not found.
    private static void CheckPattern(JsonNode? sent, string field, string path, string pattern)
    {
        if (sent is JsonValue value && value.GetValueKind() == JsonValueKind.String && !ContractPattern.IsMatch(pattern, value.GetValue<string>()))
            throw new AnalysisInputException(field, $"must match pattern \"{pattern}\"", path + ".pattern") { Hint = $"Correct {field}." };
    }

    private static IResult AnalysisResult(string operation, JsonObject result)
    {
        var name = ContractTypes.NameOf(AnalysisContract(operation).Output.Type);
        var serialized = ContractJson.ToElement(result);
        var roundTrip = ContractJson.Serialize(name, serialized);
        return Results.Json(roundTrip, global::Nami.Api.Contracts.ContractTypes.TypeInfo<global::System.Text.Json.JsonElement>());
    }


    private static async Task<IResult> UpdateFailureStatusAsync(HttpContext context, [FromRoute(Name = "failure_id")] string failureId, INamiStore store, TimeProvider time, CancellationToken ct)
    {
        try
        {
            if (ResourceHttp.ReadIfMatch(context.Request, out var condition) is { } error) return error;
            if (await JsonNode.ParseAsync(context.Request.Body, cancellationToken: ct).ConfigureAwait(false) as JsonObject is not { } body) return ResourceHttp.InvalidBody("The request body must be a JSON object.");
            CheckContract(body, global::Nami.Api.Contracts.ContractTypes.TypeInfo<global::Nami.Api.Contracts.UpdateFailureStatusRequest>());
            var status = J.String(body, "status");
            if (body["ignore_until"] is not null && status != "ignored") return ContractErrorResults.Validation("ignore_until", "ignore_until is valid only with status=ignored.", "ignore_until.invalid");
            var ignoreUntil = J.Date(body, "ignore_until");
            body = J.O(("status", status), ("assignee", J.String(body, "assignee")), ("ignore_until", ignoreUntil?.ToString("O")));
            var project = ResolveProjectScope(context); var snapshot = await store.ReadAnalysisSnapshotAsync(project, new AnalysisReadQuery { ErrorsOnly = true, Tables = AnalysisTables.Spans }, ct).ConfigureAwait(false);
            var engine = new AnalysisEngine(snapshot, project, CreateAnalysisHandles(context, project), time.GetUtcNow());
            if (!engine.OwnsFailure(failureId)) return snapshot.ScanComplete ? ContractErrorResults.NotFound("failure", failureId) : ContractErrorResults.ServiceUnavailable("analysis.scan_budget_exceeded");
            var existing = snapshot.State.FirstOrDefault(s => s.StateKey == failureId); var current = existing?.Etag ?? '"' + failureId + '"';
            if (!condition.Matches(current)) return ContractErrorResults.PreconditionFailed(current);
            body["updated_at"] = time.GetUtcNow().ToString("O");
            var row = new AnalysisStateRow { ProjectId = project, StateKey = failureId, ValueJson = body.ToJsonString(), Etag = ResourceHttp.NewEtag(), UpdatedAtUnixNano = ResourceHttp.NowUnixNano(time) };
            var outcome = await store.WriteAnalysisStateAsync(row, existing?.Etag, ct).ConfigureAwait(false);
            if (outcome.Status == ResourceWriteStatus.PreconditionFailed) return ContractErrorResults.PreconditionFailed(outcome.CurrentEtag);
            var updated = snapshot with { State = snapshot.State.Where(s => s.StateKey != failureId).Append(row).ToArray() };
            var failure = new AnalysisEngine(updated, project, CreateAnalysisHandles(context, project), time.GetUtcNow()).FailureSummary(failureId);
            context.Response.Headers.ETag = row.Etag;
            try { return AnalysisResult("update_failure_status", J.O(("failure", failure))); }
            catch (JsonException invalid)
            {
                LogInvalidAnalysisResponse(context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Nami.Collector.Analysis"), "update_failure_status", invalid);
                return ContractErrorResults.InternalServerError("analysis.response_invalid");
            }
        }
        catch (MetricQueryConflictException conflict) { return ContractErrorResults.Conflict("metric", conflict.Message); }
        catch (AnalysisInputException invalid) { return AnalysisError(invalid); }
        catch (JsonException) { return ResourceHttp.InvalidBody("Provide a valid failure status request."); }
    }
    [LoggerMessage(Level = LogLevel.Error, Message = "Analysis output violates the generated contract for {Operation}")]
    private static partial void LogInvalidAnalysisResponse(ILogger logger, string operation, Exception exception);

}
