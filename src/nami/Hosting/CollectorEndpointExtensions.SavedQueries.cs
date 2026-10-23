using Microsoft.AspNetCore.Mvc;
using WireJson = Nami.Api.Contracts.ContractJson;
using Nami.Api.Contracts.Common.Pagination;
using Nami.Api.Contracts.SavedQueries;
using Nami.Collector.Storage;

namespace Nami.Collector.Hosting;

// Saved queries (Contract 11.6.0): named, structured filters over traces, logs or metrics,
// unique by name within the caller's project. A saved query stores the filter, never results;
// `:run` evaluates it against current telemetry with a window relative to the run.
internal static partial class CollectorEndpointExtensions
{
    private const string SavedQueryOperation = "saved-queries";

    private static async Task<IResult> CreateSavedQueryAsync(
        HttpContext httpContext,
        INamiStore store,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (ResourceHttp.ReadIdempotencyKey(httpContext.Request, out var idempotencyKey) is { } keyError)
            return keyError;

        var (request, bodyError) = await ReadResourceBodyAsync(httpContext.Request, global::Nami.Api.Contracts.ContractTypes.TypeInfo<global::Nami.Api.Contracts.SavedQueries.SavedQueryCreateRequest>(),
            ct).ConfigureAwait(false);
        if (bodyError is not null)
            return bodyError;

        if (ValidateSavedQueryFilter(request!.Filter) is { } filterError)
            return filterError;

        var projectId = ResolveProjectScope(httpContext);
        var fingerprint = ResourceHttp.Fingerprint(request);
        var notBefore = ResourceHttp.IdempotencyNotBefore(timeProvider);
        var now = ResourceHttp.NowUnixNano(timeProvider);
        var row = new SavedQueryRow
        {
            ProjectId = projectId,
            SavedQueryId = ResourceHttp.NewId("sq_"),
            Name = request.Name,
            Description = request.Description,
            Signal = SavedQueryMapper.SignalName(request.Filter),
            FilterJson = SavedQueryMapper.WriteFilter(request.Filter),
            CreatedAtUnixNano = now,
            UpdatedAtUnixNano = now,
            Etag = ResourceHttp.NewEtag()
        };
        var savedQuery = SavedQueryMapper.ToContract(row);
        var idempotency = idempotencyKey is null
            ? null
            : new IdempotencyKeyRow
            {
                ProjectId = projectId,
                Operation = SavedQueryOperation,
                IdempotencyKey = idempotencyKey,
                RequestHash = fingerprint,
                ResourceId = row.SavedQueryId,
                CreatedAtUnixNano = now
            };

        var outcome = await store.CreateSavedQueryAsync(row, idempotency, notBefore, ct).ConfigureAwait(false);
        return outcome.Status switch
        {
            ResourceWriteStatus.Created => ResourceHttp.Created(
                httpContext, SavedQueryLocation(row.SavedQueryId), row.Etag, savedQuery),
            // A repeat answers the saved query its key created as it is now, with its current etag.
            ResourceWriteStatus.Replayed => ResourceHttp.Created(
                httpContext, SavedQueryLocation(outcome.Row!.SavedQueryId), outcome.Row.Etag, SavedQueryMapper.ToContract(outcome.Row)),
            ResourceWriteStatus.NameConflict => SavedQueryNameConflict(request.Name),
            _ => ResourceHttp.IdempotencyConflict(idempotencyKey!)
        };
    }

    private static async Task<IResult> ListSavedQueriesAsync(
        HttpContext httpContext,
        INamiStore store,
        CancellationToken ct)
    {
        if (ContractQueryParser.ParseSavedQueries(httpContext.Request, out var query) is { } queryError)
            return queryError;

        if (ResolvePageLimit(query.Limit, out var boundedLimit) is { } limitError)
            return limitError;

        string? afterName = null;
        if (query.Cursor is { } encoded)
        {
            if (!PageCursorCodec.TryDecode(encoded, PageCursorKind.SavedQueries, 1, out var fields))
                return InvalidCursor(encoded);
            afterName = fields[0];
        }

        var rows = await store.ListSavedQueriesAsync(
                ResolveProjectScope(httpContext), query.Signal, afterName, boundedLimit + 1, ct)
            .ConfigureAwait(false);
        var (page, hasMore) = SplitPage(rows, boundedLimit);
        return ContractHttp.Json(new CursorPageSavedQuery
        {
            Items = SavedQueryMapper.ToContracts(page),
            HasMore = hasMore,
            NextCursor = hasMore ? PageCursorCodec.Encode(PageCursorKind.SavedQueries, page[^1].Name) : null
        });
    }

    private static async Task<IResult> GetSavedQueryAsync(
        HttpContext httpContext,
        [FromRoute(Name = "saved_query_id")] string savedQueryId,
        INamiStore store,
        CancellationToken ct) =>
        await store.GetSavedQueryAsync(ResolveProjectScope(httpContext), savedQueryId, ct).ConfigureAwait(false)
            is not { } row
            ? ContractErrorResults.NotFound("saved_query", savedQueryId)
            : ResourceHttp.WithEtag(httpContext, row.Etag, SavedQueryMapper.ToContract(row));

    private static async Task<IResult> UpdateSavedQueryAsync(
        HttpContext httpContext,
        [FromRoute(Name = "saved_query_id")] string savedQueryId,
        INamiStore store,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (ResourceHttp.ReadIfMatch(httpContext.Request, out var ifMatch) is { } preconditionError)
            return preconditionError;

        var (request, bodyError) = await ReadResourceBodyAsync(httpContext.Request, global::Nami.Api.Contracts.ContractTypes.TypeInfo<global::Nami.Api.Contracts.SavedQueries.SavedQueryUpdateRequest>(),
            ct).ConfigureAwait(false);
        if (bodyError is not null)
            return bodyError;

        if (request!.Filter is { } filter && ValidateSavedQueryFilter(filter) is { } filterError)
            return filterError;

        var now = ResourceHttp.NowUnixNano(timeProvider);
        var etag = ResourceHttp.NewEtag();
        var outcome = await store.UpdateSavedQueryAsync(
            ResolveProjectScope(httpContext),
            savedQueryId,
            ifMatch,
            current => current with
            {
                // Omitted members keep their value; the contract has no way to send a null.
                Name = request.Name ?? current.Name,
                Description = request.Description ?? current.Description,
                Signal = request.Filter is { } next ? SavedQueryMapper.SignalName(next) : current.Signal,
                FilterJson = request.Filter is { } replacement
                    ? SavedQueryMapper.WriteFilter(replacement)
                    : current.FilterJson,
                UpdatedAtUnixNano = now,
                Etag = etag
            },
            ct).ConfigureAwait(false);

        return outcome.Status switch
        {
            ResourceWriteStatus.Updated => ResourceHttp.WithEtag(httpContext, outcome.Row!.Etag,
                SavedQueryMapper.ToContract(outcome.Row)),
            ResourceWriteStatus.PreconditionFailed => ContractErrorResults.PreconditionFailed(outcome.CurrentEtag),
            ResourceWriteStatus.NameConflict => SavedQueryNameConflict(request.Name!),
            _ => ContractErrorResults.NotFound("saved_query", savedQueryId)
        };
    }

    private static async Task<IResult> DeleteSavedQueryAsync(
        HttpContext httpContext,
        [FromRoute(Name = "saved_query_id")] string savedQueryId,
        INamiStore store,
        CancellationToken ct)
    {
        if (ResourceHttp.ReadIfMatch(httpContext.Request, out var ifMatch) is { } preconditionError)
            return preconditionError;
        if (await RefuseBodyAsync(httpContext.Request, ct).ConfigureAwait(false) is { } bodyError)
            return bodyError;

        var outcome = await store.DeleteSavedQueryAsync(ResolveProjectScope(httpContext), savedQueryId, ifMatch, ct)
            .ConfigureAwait(false);
        return outcome.Status switch
        {
            ResourceWriteStatus.Deleted => Results.NoContent(),
            ResourceWriteStatus.PreconditionFailed => ContractErrorResults.PreconditionFailed(outcome.CurrentEtag),
            _ => ContractErrorResults.NotFound("saved_query", savedQueryId)
        };
    }

    /// <summary>
    /// Evaluates a saved query. The window is the request's <c>start_time</c>/<c>end_time</c>
    /// where given, otherwise the saved <c>lookback_minutes</c> (default 60) ending now; a member
    /// <c>SavedQueryRunRequest</c> does not declare is refused like an analysis argument. A trace or
    /// log run's <c>next_cursor</c> carries the saved query's id and the window of its first page,
    /// so every later page reads the same window and no other saved query accepts it.
    /// </summary>
    private static async Task<IResult> RunSavedQueryAsync(
        HttpContext httpContext,
        [FromRoute(Name = "saved_query_id")] string savedQueryId,
        INamiStore store,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        try
        {
            if ((httpContext.Request.ContentLength is > 0
                ? await System.Text.Json.Nodes.JsonNode.ParseAsync(httpContext.Request.Body, cancellationToken: ct).ConfigureAwait(false) as System.Text.Json.Nodes.JsonObject
                : new System.Text.Json.Nodes.JsonObject()) is not { } body) return ResourceHttp.InvalidBody("Provide an object with the run options.");
            CheckContract(body, global::Nami.Api.Contracts.ContractTypes.TypeInfo<global::Nami.Api.Contracts.SavedQueries.SavedQueryRunRequest>());
            body["id"] = savedQueryId;
            var project = ResolveProjectScope(httpContext);
            if (await store.GetSavedQueryAsync(project, savedQueryId, ct).ConfigureAwait(false) is not { } saved)
                return AnalysisError(Analysis.AnalysisEngine.NoSavedQuery(savedQueryId));
            // The run reads its saved filter's signal over its own window, as the analysis route does.
            var now = timeProvider.GetUtcNow(); var handles = CreateAnalysisHandles(httpContext, project);
            var query = Analysis.AnalysisReadPlan.Resolve("run_saved_query", Analysis.AnalysisEngine.SavedQueryInput(saved, body), now, handles, project);
            var snapshot = await store.ReadAnalysisSnapshotAsync(project, query, ct).ConfigureAwait(false);
            var result = new Analysis.AnalysisEngine(snapshot, project, handles, now).Run("run_saved_query", body);
            // The analysis answer names its rows by signal and reports the run's window beside them; the HTTP route answers
            // with the run result of the contract, whose metric result states its window itself.
            var signal = Analysis.J.String(result["saved_query"]?["filter"], "signal")!;
            var run = signal == "metrics"
                ? Analysis.J.O(("signal", signal), ("result", MetricRunResult(result)))
                : Analysis.J.O(("signal", signal), ("items", result[signal]), ("has_more", result["has_more"]), ("next_cursor", result["next_cursor"]));
            var wire = WireJson.Serialize("SavedQueries.SavedQueryRunResult", WireJson.ToElement(run));
            return Results.Json(wire, global::Nami.Api.Contracts.ContractTypes.TypeInfo<global::System.Text.Json.JsonElement>());
        }
        catch (Analysis.AnalysisInputException invalid) { return AnalysisError(invalid); }
        catch (JsonException) { return ResourceHttp.InvalidBody("Provide valid run options."); }
    }

    private static System.Text.Json.Nodes.JsonObject MetricRunResult(System.Text.Json.Nodes.JsonObject result)
    {
        var metric = result["metric"]!.DeepClone().AsObject();
        metric["start_time"] = result["window"]!["start_time"]!.DeepClone();
        metric["end_time"] = result["window"]!["end_time"]!.DeepClone();
        metric["truncated"] = result["coverage"]!["truncated"]!.DeepClone();
        foreach (var field in new[] { "method", "exemplar_trace_ids", "comparison" }) metric.Remove(field);
        return metric;
    }

    private static string SavedQueryLocation(string savedQueryId) =>
        "/api/v1/saved-queries/" + Uri.EscapeDataString(savedQueryId);

    private static IResult SavedQueryNameConflict(string name) =>
        ContractErrorResults.Conflict(name, "A saved query with this name already exists in the project.");

    // TypeSpec validates each field. Operand relationships still depend on the selected operator.
    private static IResult? ValidateSavedQueryFilter(SavedQueryFilter filter)
    {
        try { Analysis.AnalysisMatchers.Validate(System.Text.Json.Nodes.JsonNode.Parse(SavedQueryMapper.WriteFilter(filter))?["matchers"]); }
        catch (Analysis.AnalysisInputException invalid) { return ContractErrorResults.Validation("filter.matchers", invalid.Description, invalid.Code); }
        return null;
    }
}
