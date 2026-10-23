using Microsoft.AspNetCore.Mvc;
using Nami.Api.Contracts.Annotations;
using Nami.Api.Contracts.Common.Pagination;
using Nami.Collector.Analysis;
using Nami.Collector.Storage;

namespace Nami.Collector.Hosting;

// Annotations (Contract 11.6.0): user notes on a trace, span or session inside the caller's
// project. A note never changes the telemetry it points at.
internal static partial class CollectorEndpointExtensions
{
    private const string AnnotationOperation = "annotations";

    private static async Task<IResult> CreateAnnotationAsync(
        HttpContext httpContext,
        INamiStore store,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (ResourceHttp.ReadIdempotencyKey(httpContext.Request, out var idempotencyKey) is { } keyError)
            return keyError;

        var (request, bodyError) = await ReadResourceBodyAsync(httpContext.Request, global::Nami.Api.Contracts.ContractTypes.TypeInfo<global::Nami.Api.Contracts.Annotations.AnnotationCreateRequest>(),
            ct).ConfigureAwait(false);
        if (request is null)
            return bodyError!;

        var projectId = ResolveProjectScope(httpContext);
        var fingerprint = ResourceHttp.Fingerprint(request);
        var notBefore = ResourceHttp.IdempotencyNotBefore(timeProvider);

        // A retry answers the annotation its key created, as it is now, even if the target has since
        // expired, so the key is consulted before the target is; once that annotation was deleted, the
        // retry creates it again.
        if (idempotencyKey is not null &&
            await store.GetIdempotencyKeyAsync(projectId, AnnotationOperation, idempotencyKey, notBefore, ct)
                .ConfigureAwait(false) is { } previous)
        {
            if (!string.Equals(previous.RequestHash, fingerprint, StringComparison.Ordinal))
                return ResourceHttp.IdempotencyConflict(idempotencyKey);
            if (await store.GetAnnotationAsync(projectId, previous.ResourceId, ct).ConfigureAwait(false) is { } current)
                return await RepeatedAnnotationAsync(httpContext, store, current, ct).ConfigureAwait(false);
        }

        if (await FindMissingTargetAsync(store, projectId, request.Target, ct).ConfigureAwait(false) is { } missing)
            return missing;

        var now = ResourceHttp.NowUnixNano(timeProvider);
        var row = new AnnotationRow
        {
            ProjectId = projectId,
            AnnotationId = ResourceHttp.NewId("ann_"),
            TargetKind = request.Target switch
            {
                TraceAnnotationTarget => AnnotationMapper.TraceKind,
                SpanAnnotationTarget => AnnotationMapper.SpanKind,
                FailureAnnotationTarget => "failure",
                _ => AnnotationMapper.SessionKind
            },
            TraceId = request.Target switch
            {
                TraceAnnotationTarget trace => trace.TraceId,
                SpanAnnotationTarget span => span.TraceId,
                _ => null
            },
            SpanId = (request.Target as SpanAnnotationTarget)?.SpanId,
            SessionId = (request.Target as SessionAnnotationTarget)?.SessionId,
            Text = request.Text,
            CreatedAtUnixNano = now,
            UpdatedAtUnixNano = now,
            Etag = ResourceHttp.NewEtag()
        };
        var annotation = AnnotationMapper.ToContract(row, (request.Target as FailureAnnotationTarget)?.FailureId, request.Labels, request.Classification ?? AnnotationClassification.Note);
        var metadata = AnnotationMetadata(row, annotation);
        var idempotency = idempotencyKey is null
            ? null
            : new IdempotencyKeyRow
            {
                ProjectId = projectId,
                Operation = AnnotationOperation,
                IdempotencyKey = idempotencyKey,
                RequestHash = fingerprint,
                ResourceId = row.AnnotationId,
                CreatedAtUnixNano = now
            };

        var outcome = await store.CreateAnnotationAsync(row, idempotency, notBefore, metadata, ct).ConfigureAwait(false);
        return outcome.Status switch
        {
            ResourceWriteStatus.Created => ResourceHttp.Created(
                httpContext, AnnotationLocation(row.AnnotationId), row.Etag, annotation),
            ResourceWriteStatus.Replayed => await RepeatedAnnotationAsync(httpContext, store, outcome.Row!, ct).ConfigureAwait(false),
            _ => ResourceHttp.IdempotencyConflict(idempotencyKey!)
        };
    }

    private static async Task<IResult> ListAnnotationsAsync(
        HttpContext httpContext,
        INamiStore store,
        CancellationToken ct)
    {
        if (ContractQueryParser.ParseAnnotations(httpContext.Request, out var query) is { } queryError)
            return queryError;

        if (ResolvePageLimit(query.Limit, out var boundedLimit) is { } limitError)
            return limitError;

        AnnotationPageCursor? cursor = null;
        if (query.Cursor is { } encoded)
        {
            if (!PageCursorCodec.TryDecode(encoded, PageCursorKind.Annotations, 2, out var fields) ||
                !PageCursorCodec.TryParseHex(fields[0], out var createdAt))
            {
                return InvalidCursor(encoded);
            }

            cursor = new AnnotationPageCursor(createdAt, fields[1]);
        }

        var rows = await store.ListAnnotationsAsync(new AnnotationListQuery
        {
            ProjectId = ResolveProjectScope(httpContext),
            Limit = boundedLimit + 1,
            TraceId = query.TraceId,
            SpanId = query.SpanId,
            SessionId = query.SessionId,
            Cursor = cursor
        }, ct).ConfigureAwait(false);

        var (page, hasMore) = SplitPage(rows, boundedLimit);
        return ContractHttp.Json(new CursorPageAnnotation
        {
            Items = await AnnotationContractsAsync(store, page, ct),
            HasMore = hasMore,
            NextCursor = hasMore
                ? PageCursorCodec.Encode(
                    PageCursorKind.Annotations,
                    PageCursorCodec.Hex(page[^1].CreatedAtUnixNano),
                    page[^1].AnnotationId)
                : null
        });
    }

    private static async Task<IResult> GetAnnotationAsync(HttpContext httpContext,
        [FromRoute(Name = "annotation_id")] string annotationId, INamiStore store, CancellationToken ct)
    {
        if (await store.GetAnnotationAsync(ResolveProjectScope(httpContext), annotationId, ct).ConfigureAwait(false) is not { } row) return ContractErrorResults.NotFound("annotation", annotationId);
        var contracts = await AnnotationContractsAsync(store, [row], ct).ConfigureAwait(false);
        return ResourceHttp.WithEtag(httpContext, row.Etag, contracts[0]);
    }

    private static async Task<IResult> UpdateAnnotationAsync(
        HttpContext httpContext,
        [FromRoute(Name = "annotation_id")] string annotationId,
        INamiStore store,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        if (ResourceHttp.ReadIfMatch(httpContext.Request, out var ifMatch) is { } preconditionError)
            return preconditionError;

        var (request, bodyError) = await ReadResourceBodyAsync(httpContext.Request, global::Nami.Api.Contracts.ContractTypes.TypeInfo<global::Nami.Api.Contracts.Annotations.AnnotationUpdateRequest>(),
            ct).ConfigureAwait(false);
        if (bodyError is not null)
            return bodyError;

        if (await store.GetAnnotationAsync(ResolveProjectScope(httpContext), annotationId, ct).ConfigureAwait(false) is not { } existing) return ContractErrorResults.NotFound("annotation", annotationId);
        var prior = (await AnnotationContractsAsync(store, [existing], ct).ConfigureAwait(false))[0];
        var now = ResourceHttp.NowUnixNano(timeProvider);
        var etag = ResourceHttp.NewEtag();
        var updatedRow = existing with { Text = request!.Text ?? existing.Text, UpdatedAtUnixNano = now, Etag = etag };
        var updatedContract = AnnotationMapper.ToContract(updatedRow, (prior.Target as FailureAnnotationTarget)?.FailureId,
            request.Labels ?? prior.Labels, request.Classification ?? prior.Classification);
        var metadata = AnnotationMetadata(updatedRow, updatedContract);
        var outcome = await store.UpdateAnnotationAsync(
            ResolveProjectScope(httpContext),
            annotationId,
            ifMatch,
            current => current with { Text = request.Text ?? current.Text, UpdatedAtUnixNano = now, Etag = etag },
            metadata, ct).ConfigureAwait(false);

        return outcome.Status switch
        {
            ResourceWriteStatus.Updated => ResourceHttp.WithEtag(httpContext, outcome.Row!.Etag,
                updatedContract),
            ResourceWriteStatus.PreconditionFailed => ContractErrorResults.PreconditionFailed(outcome.CurrentEtag),
            _ => ContractErrorResults.NotFound("annotation", annotationId)
        };
    }

    private static async Task<IResult> DeleteAnnotationAsync(
        HttpContext httpContext,
        [FromRoute(Name = "annotation_id")] string annotationId,
        INamiStore store,
        CancellationToken ct)
    {
        if (ResourceHttp.ReadIfMatch(httpContext.Request, out var ifMatch) is { } preconditionError)
            return preconditionError;
        if (await RefuseBodyAsync(httpContext.Request, ct).ConfigureAwait(false) is { } bodyError)
            return bodyError;

        var outcome = await store.DeleteAnnotationAsync(ResolveProjectScope(httpContext), annotationId, ifMatch, ct)
            .ConfigureAwait(false);
        return outcome.Status switch
        {
            ResourceWriteStatus.Deleted => Results.NoContent(),
            ResourceWriteStatus.PreconditionFailed => ContractErrorResults.PreconditionFailed(outcome.CurrentEtag),
            _ => ContractErrorResults.NotFound("annotation", annotationId)
        };
    }

    private static string AnnotationLocation(string annotationId) =>
        "/api/v1/annotations/" + Uri.EscapeDataString(annotationId);

    // A repeated create answers the annotation its key created as it is now: its current text, labels,
    // classification and etag.
    private static async Task<IResult> RepeatedAnnotationAsync(
        HttpContext httpContext,
        INamiStore store,
        AnnotationRow current,
        CancellationToken ct)
    {
        var annotation = (await AnnotationContractsAsync(store, [current], ct).ConfigureAwait(false))[0];
        return ResourceHttp.Created(httpContext, AnnotationLocation(current.AnnotationId), current.Etag, annotation);
    }

    private static async Task<IResult?> FindMissingTargetAsync(
        INamiStore store,
        string projectId,
        AnnotationTarget target,
        CancellationToken ct) => target switch
    {
        TraceAnnotationTarget trace =>
            await store.TraceExistsAsync(projectId, trace.TraceId, ct).ConfigureAwait(false)
                ? null
                : ContractErrorResults.NotFound("trace", trace.TraceId),
        SpanAnnotationTarget span =>
            await store.SpanExistsAsync(projectId, span.TraceId, span.SpanId, ct).ConfigureAwait(false)
                ? null
                : ContractErrorResults.NotFound("span", span.SpanId),
        FailureAnnotationTarget failure =>
            await ValidateFailureTargetAsync(store, projectId, failure.FailureId, ct).ConfigureAwait(false),
        SessionAnnotationTarget session =>
            await store.GetSessionAsync(session.SessionId, projectId, ct).ConfigureAwait(false) is not null
                ? null
                : ContractErrorResults.NotFound("session", session.SessionId),
        // The body was read against its contract, which takes these four targets only.
        _ => throw new UnreachableException("An annotation target outside the contract's four kinds.")
    };
    private static async Task<IResult?> ValidateFailureTargetAsync(INamiStore store, string projectId, string failureId, CancellationToken ct)
    {
        var snapshot = await store.ReadAnalysisSnapshotAsync(projectId, new AnalysisReadQuery { ErrorsOnly = true, Tables = AnalysisTables.Spans }, ct).ConfigureAwait(false);
        if (snapshot.Spans.Any(s => AnalysisEngine.FailureIdentity(projectId, s) == failureId)) return null;
        return snapshot.ScanComplete ? ContractErrorResults.NotFound("failure", failureId) : Results.Problem(statusCode: 503, title: "Failure lookup exceeded its scan budget.");
    }
    private static AnalysisStateRow AnnotationMetadata(AnnotationRow row, Annotation annotation) => new()
    {
        ProjectId = row.ProjectId, StateKey = "annotation:" + row.AnnotationId, Etag = row.Etag, UpdatedAtUnixNano = row.UpdatedAtUnixNano,
        ValueJson = JsonSerializer.Serialize(annotation, global::Nami.Api.Contracts.ContractTypes.TypeInfo<global::Nami.Api.Contracts.Annotations.Annotation>())
    };
    private static async Task<IReadOnlyList<Annotation>> AnnotationContractsAsync(INamiStore store, IReadOnlyList<AnnotationRow> rows, CancellationToken ct)
    {
        if (rows.Count is 0) return [];
        var state = await store.ReadAnalysisStateAsync(rows[0].ProjectId, rows.Select(row => "annotation:" + row.AnnotationId).ToArray(), ct).ConfigureAwait(false);
        return rows.Select(row => state.FirstOrDefault(s => s.StateKey == "annotation:" + row.AnnotationId) is { } metadata
            ? JsonSerializer.Deserialize(metadata.ValueJson, global::Nami.Api.Contracts.ContractTypes.TypeInfo<global::Nami.Api.Contracts.Annotations.Annotation>())!
            : AnnotationMapper.ToContract(row)).ToArray();
    }

}
