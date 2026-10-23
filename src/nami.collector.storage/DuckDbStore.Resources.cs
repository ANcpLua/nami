using DuckDB.NET.Data;

namespace Nami.Collector.Storage;

/// <summary>
/// An <c>If-Match</c> precondition: <c>*</c> matches any current version, otherwise one of the
/// listed entity tags must be the current one (strong comparison, so weak tags never match).
/// </summary>
internal readonly record struct EntityTagCondition(bool Any, IReadOnlyList<string> Tags)
{
    public bool Matches(string currentEtag) =>
        Any || Tags.Contains(currentEtag, StringComparer.Ordinal);
}

internal enum ResourceWriteStatus
{
    Created,
    Replayed,
    Updated,
    Deleted,
    NotFound,
    PreconditionFailed,
    IdempotencyConflict,
    NameConflict
}

/// <summary>The result of one serialized resource write; a repeated create's <see cref="Row"/> is the record as it is now.</summary>
internal sealed record ResourceWriteOutcome<TRow>(
    ResourceWriteStatus Status,
    TRow? Row = null,
    string? CurrentEtag = null)
    where TRow : class;

/// <summary>One page of annotations, newest first; <see cref="TraceId"/> includes its spans' notes.</summary>
internal sealed record AnnotationListQuery
{
    public required string ProjectId { get; init; }
    public required int Limit { get; init; }
    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
    public string? SessionId { get; init; }
    public AnnotationPageCursor? Cursor { get; init; }
}

internal readonly record struct AnnotationPageCursor(ulong CreatedAtUnixNano, string AnnotationId);

internal sealed partial class DuckDbStore
{
    // Every resource write runs as one job on the single storage writer, so the existence,
    // name-uniqueness, entity-tag and idempotency checks below cannot race another write: the
    // writer is the serialization point, and each job commits or rolls back as a whole.

    public Task<bool> TraceExistsAsync(string projectId, string traceId, CancellationToken ct = default) =>
        ExecuteReadAsync(con =>
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM spans WHERE project_id = $1 AND trace_id = $2)";
            AddParameters(cmd, projectId, traceId);
            return cmd.ExecuteScalar() is true;
        }, ct);

    public Task<bool> SpanExistsAsync(string projectId, string traceId, string spanId, CancellationToken ct = default) =>
        ExecuteReadAsync(con =>
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText =
                "SELECT EXISTS (SELECT 1 FROM spans WHERE project_id = $1 AND trace_id = $2 AND span_id = $3)";
            AddParameters(cmd, projectId, traceId, spanId);
            return cmd.ExecuteScalar() is true;
        }, ct);

    public Task<IdempotencyKeyRow?> GetIdempotencyKeyAsync(
        string projectId,
        string operation,
        string idempotencyKey,
        ulong notBeforeUnixNano,
        CancellationToken ct = default) =>
        ExecuteReadAsync(con => ReadIdempotencyKey(con, null, projectId, operation, idempotencyKey, notBeforeUnixNano), ct);

    public Task<AnnotationRow?> GetAnnotationAsync(string projectId, string annotationId, CancellationToken ct = default) =>
        ExecuteReadAsync(con => ReadAnnotation(con, null, projectId, annotationId), ct);

    public Task<IReadOnlyList<AnnotationRow>> ListAnnotationsAsync(AnnotationListQuery query, CancellationToken ct = default)
    {
        if (query.Limit < 1)
            throw new ArgumentOutOfRangeException(nameof(query), query.Limit, "Annotation page limit must be positive.");

        return ExecuteReadAsync<IReadOnlyList<AnnotationRow>>(con =>
        {
            var qb = new QueryBuilder();
            qb.Add("project_id = $N", query.ProjectId);
            if (query.TraceId is { } traceId)
                qb.Add("trace_id = $N", traceId);
            if (query.SpanId is { } spanId)
                qb.Add("target_kind = 'span' AND span_id = $N", spanId);
            if (query.SessionId is { } sessionId)
                qb.Add("target_kind = 'session' AND session_id = $N", sessionId);
            if (query.Cursor is { } after)
                qb.Add("(created_at_unix_nano < $A OR (created_at_unix_nano = $A AND annotation_id < $B))",
                    (decimal)after.CreatedAtUnixNano, after.AnnotationId);

            using var cmd = con.CreateCommand();
            // CA2100: the generated column list, QueryBuilder's $n predicates and the limit's placeholder; values are parameters.
            cmd.CommandText = "SELECT " + AnnotationRow.SelectColumnList
                              + " FROM annotations " + qb.WhereClause
                              + " ORDER BY created_at_unix_nano DESC, annotation_id DESC LIMIT " + qb.NextParam;
            qb.ApplyTo(cmd);
            cmd.Parameters.Add(new DuckDBParameter { Value = query.Limit });

            var rows = new List<AnnotationRow>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                rows.Add(AnnotationRow.MapFromReader(reader));
            return rows;
        }, ct);
    }

    public Task<ResourceWriteOutcome<AnnotationRow>> CreateAnnotationAsync(
        AnnotationRow row,
        IdempotencyKeyRow? idempotency,
        ulong idempotencyNotBeforeUnixNano,
        AnalysisStateRow? metadata = null, CancellationToken ct = default) =>
        ExecuteWriteAsync(async (con, token) =>
        {
            await using var tx = await con.BeginTransactionAsync(token).ConfigureAwait(false);
            if (idempotency is not null && Repeat(con, tx, idempotency, idempotencyNotBeforeUnixNano,
                    id => ReadAnnotation(con, tx, idempotency.ProjectId, id)) is { } repeat)
            {
                return repeat;
            }

            await InsertRowsBatchedAsync(con, tx, [row], AnnotationRow.AddParameters,
                AnnotationRow.BuildMultiRowInsertSql, 1, token).ConfigureAwait(false);
            if (idempotency is not null)
            {
                await InsertRowsBatchedAsync(con, tx, [idempotency], IdempotencyKeyRow.AddParameters,
                    IdempotencyKeyRow.BuildMultiRowInsertSql, 1, token).ConfigureAwait(false);
            }

            if (metadata is not null)
                await InsertRowsBatchedAsync(con, tx, [metadata], AnalysisStateRow.AddParameters, AnalysisStateRow.BuildMultiRowInsertSql, 1, token).ConfigureAwait(false);
            await tx.CommitAsync(token).ConfigureAwait(false);
            return new ResourceWriteOutcome<AnnotationRow>(ResourceWriteStatus.Created, row);
        }, ct);

    public Task<ResourceWriteOutcome<AnnotationRow>> UpdateAnnotationAsync(
        string projectId,
        string annotationId,
        EntityTagCondition ifMatch,
        Func<AnnotationRow, AnnotationRow> update,
        AnalysisStateRow? metadata = null, CancellationToken ct = default) =>
        ExecuteWriteAsync(async (con, token) =>
        {
            await using var tx = await con.BeginTransactionAsync(token).ConfigureAwait(false);
            if (ReadAnnotation(con, tx, projectId, annotationId) is not { } current)
                return new ResourceWriteOutcome<AnnotationRow>(ResourceWriteStatus.NotFound);
            if (!ifMatch.Matches(current.Etag))
                return new ResourceWriteOutcome<AnnotationRow>(ResourceWriteStatus.PreconditionFailed,
                    CurrentEtag: current.Etag);

            var updated = update(current);
            await InsertRowsBatchedAsync(con, tx, [updated], AnnotationRow.AddParameters,
                AnnotationRow.BuildMultiRowInsertSql, 1, token).ConfigureAwait(false);
            if (metadata is not null)
                await InsertRowsBatchedAsync(con, tx, [metadata], AnalysisStateRow.AddParameters, AnalysisStateRow.BuildMultiRowInsertSql, 1, token).ConfigureAwait(false);
            await tx.CommitAsync(token).ConfigureAwait(false);
            return new ResourceWriteOutcome<AnnotationRow>(ResourceWriteStatus.Updated, updated);
        }, ct);

    public Task<ResourceWriteOutcome<AnnotationRow>> DeleteAnnotationAsync(
        string projectId,
        string annotationId,
        EntityTagCondition ifMatch,
        CancellationToken ct = default) =>
        ExecuteWriteAsync(async (con, token) =>
        {
            await using var tx = await con.BeginTransactionAsync(token).ConfigureAwait(false);
            if (ReadAnnotation(con, tx, projectId, annotationId) is not { } current)
                return new ResourceWriteOutcome<AnnotationRow>(ResourceWriteStatus.NotFound);
            if (!ifMatch.Matches(current.Etag))
                return new ResourceWriteOutcome<AnnotationRow>(ResourceWriteStatus.PreconditionFailed,
                    CurrentEtag: current.Etag);

            await using var cmd = con.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM annotations WHERE project_id = $1 AND annotation_id = $2";
            AddParameters(cmd, projectId, annotationId);
            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            cmd.Parameters.Clear();
            cmd.CommandText = "DELETE FROM analysis_state WHERE project_id = $1 AND state_key = $2";
            AddParameters(cmd, projectId, "annotation:" + annotationId);
            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            await DeleteIdempotencyKeysAsync(cmd, projectId, annotationId, token).ConfigureAwait(false);
            await tx.CommitAsync(token).ConfigureAwait(false);
            return new ResourceWriteOutcome<AnnotationRow>(ResourceWriteStatus.Deleted, current);
        }, ct);

    public Task<SavedQueryRow?> GetSavedQueryAsync(string projectId, string savedQueryId, CancellationToken ct = default) =>
        ExecuteReadAsync(con => ReadSavedQuery(con, null, projectId, savedQueryId), ct);

    public Task<IReadOnlyList<SavedQueryRow>> ListSavedQueriesAsync(
        string projectId,
        string? signal,
        string? afterName,
        int limit,
        CancellationToken ct = default)
    {
        if (limit < 1)
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Saved-query page limit must be positive.");

        return ExecuteReadAsync<IReadOnlyList<SavedQueryRow>>(con =>
        {
            var qb = new QueryBuilder();
            qb.Add("project_id = $N", projectId);
            if (signal is not null)
                qb.Add("signal = $N", signal);
            if (afterName is not null)
                qb.Add("name > $N", afterName);

            using var cmd = con.CreateCommand();
            // CA2100: the generated column list, QueryBuilder's $n predicates and the limit's placeholder; values are parameters.
            cmd.CommandText = "SELECT " + SavedQueryRow.SelectColumnList
                              + " FROM saved_queries " + qb.WhereClause
                              + " ORDER BY name ASC LIMIT " + qb.NextParam;
            qb.ApplyTo(cmd);
            cmd.Parameters.Add(new DuckDBParameter { Value = limit });

            var rows = new List<SavedQueryRow>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                rows.Add(SavedQueryRow.MapFromReader(reader));
            return rows;
        }, ct);
    }

    public Task<ResourceWriteOutcome<SavedQueryRow>> CreateSavedQueryAsync(
        SavedQueryRow row,
        IdempotencyKeyRow? idempotency,
        ulong idempotencyNotBeforeUnixNano,
        CancellationToken ct = default) =>
        ExecuteWriteAsync(async (con, token) =>
        {
            await using var tx = await con.BeginTransactionAsync(token).ConfigureAwait(false);
            if (idempotency is not null && Repeat(con, tx, idempotency, idempotencyNotBeforeUnixNano,
                    id => ReadSavedQuery(con, tx, idempotency.ProjectId, id)) is { } repeat)
            {
                return repeat;
            }

            if (SavedQueryNameTaken(con, tx, row.ProjectId, row.Name, exceptSavedQueryId: null))
                return new ResourceWriteOutcome<SavedQueryRow>(ResourceWriteStatus.NameConflict);

            await InsertRowsBatchedAsync(con, tx, [row], SavedQueryRow.AddParameters,
                SavedQueryRow.BuildMultiRowInsertSql, 1, token).ConfigureAwait(false);
            if (idempotency is not null)
            {
                await InsertRowsBatchedAsync(con, tx, [idempotency], IdempotencyKeyRow.AddParameters,
                    IdempotencyKeyRow.BuildMultiRowInsertSql, 1, token).ConfigureAwait(false);
            }

            await tx.CommitAsync(token).ConfigureAwait(false);
            return new ResourceWriteOutcome<SavedQueryRow>(ResourceWriteStatus.Created, row);
        }, ct);

    public Task<ResourceWriteOutcome<SavedQueryRow>> UpdateSavedQueryAsync(
        string projectId,
        string savedQueryId,
        EntityTagCondition ifMatch,
        Func<SavedQueryRow, SavedQueryRow> update,
        CancellationToken ct = default) =>
        ExecuteWriteAsync(async (con, token) =>
        {
            await using var tx = await con.BeginTransactionAsync(token).ConfigureAwait(false);
            if (ReadSavedQuery(con, tx, projectId, savedQueryId) is not { } current)
                return new ResourceWriteOutcome<SavedQueryRow>(ResourceWriteStatus.NotFound);
            if (!ifMatch.Matches(current.Etag))
                return new ResourceWriteOutcome<SavedQueryRow>(ResourceWriteStatus.PreconditionFailed,
                    CurrentEtag: current.Etag);

            var updated = update(current);
            if (!string.Equals(updated.Name, current.Name, StringComparison.Ordinal) &&
                SavedQueryNameTaken(con, tx, projectId, updated.Name, exceptSavedQueryId: savedQueryId))
            {
                return new ResourceWriteOutcome<SavedQueryRow>(ResourceWriteStatus.NameConflict);
            }

            await InsertRowsBatchedAsync(con, tx, [updated], SavedQueryRow.AddParameters,
                SavedQueryRow.BuildMultiRowInsertSql, 1, token).ConfigureAwait(false);
            await tx.CommitAsync(token).ConfigureAwait(false);
            return new ResourceWriteOutcome<SavedQueryRow>(ResourceWriteStatus.Updated, updated);
        }, ct);

    public Task<ResourceWriteOutcome<SavedQueryRow>> DeleteSavedQueryAsync(
        string projectId,
        string savedQueryId,
        EntityTagCondition ifMatch,
        CancellationToken ct = default) =>
        ExecuteWriteAsync(async (con, token) =>
        {
            await using var tx = await con.BeginTransactionAsync(token).ConfigureAwait(false);
            if (ReadSavedQuery(con, tx, projectId, savedQueryId) is not { } current)
                return new ResourceWriteOutcome<SavedQueryRow>(ResourceWriteStatus.NotFound);
            if (!ifMatch.Matches(current.Etag))
                return new ResourceWriteOutcome<SavedQueryRow>(ResourceWriteStatus.PreconditionFailed,
                    CurrentEtag: current.Etag);

            await using var cmd = con.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM saved_queries WHERE project_id = $1 AND saved_query_id = $2";
            AddParameters(cmd, projectId, savedQueryId);
            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            await DeleteIdempotencyKeysAsync(cmd, projectId, savedQueryId, token).ConfigureAwait(false);
            await tx.CommitAsync(token).ConfigureAwait(false);
            return new ResourceWriteOutcome<SavedQueryRow>(ResourceWriteStatus.Deleted, current);
        }, ct);

    /// <summary>
    /// A create repeated under its retry key answers the record that key created, as it is now; the same key with another
    /// body is a conflict. Null when the key is new or its record was deleted: the create runs again and takes the key over.
    /// </summary>
    private static ResourceWriteOutcome<TRow>? Repeat<TRow>(
        DuckDBConnection con,
        DbTransaction tx,
        IdempotencyKeyRow attempt,
        ulong notBeforeUnixNano,
        Func<string, TRow?> read)
        where TRow : class
    {
        if (ReadIdempotencyKey(con, tx, attempt.ProjectId, attempt.Operation, attempt.IdempotencyKey, notBeforeUnixNano) is not { } previous)
            return null;
        if (!string.Equals(previous.RequestHash, attempt.RequestHash, StringComparison.Ordinal))
            return new ResourceWriteOutcome<TRow>(ResourceWriteStatus.IdempotencyConflict);
        return read(previous.ResourceId) is { } current ? new ResourceWriteOutcome<TRow>(ResourceWriteStatus.Replayed, current) : null;
    }

    // A deleted record's retry keys go with it, so a repeat of its create makes a new one.
    private static async Task DeleteIdempotencyKeysAsync(DuckDBCommand cmd, string projectId, string resourceId, CancellationToken token)
    {
        cmd.Parameters.Clear();
        cmd.CommandText = "DELETE FROM idempotency_keys WHERE project_id = $1 AND resource_id = $2";
        AddParameters(cmd, projectId, resourceId);
        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static IdempotencyKeyRow? ReadIdempotencyKey(
        DuckDBConnection con,
        DbTransaction? tx,
        string projectId,
        string operation,
        string idempotencyKey,
        ulong notBeforeUnixNano)
    {
        using var cmd = con.CreateCommand();
        cmd.Transaction = tx;
        // An expired key reads as absent; the next write with it overwrites the row in place.
        cmd.CommandText = "SELECT " + IdempotencyKeyRow.SelectColumnList
                          + " FROM idempotency_keys WHERE project_id = $1 AND operation = $2"
                          + " AND idempotency_key = $3 AND created_at_unix_nano >= $4";
        AddParameters(cmd, projectId, operation, idempotencyKey, (decimal)notBeforeUnixNano);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? IdempotencyKeyRow.MapFromReader(reader) : null;
    }

    private static AnnotationRow? ReadAnnotation(
        DuckDBConnection con,
        DbTransaction? tx,
        string projectId,
        string annotationId)
    {
        using var cmd = con.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT " + AnnotationRow.SelectColumnList
                          + " FROM annotations WHERE project_id = $1 AND annotation_id = $2";
        AddParameters(cmd, projectId, annotationId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? AnnotationRow.MapFromReader(reader) : null;
    }

    private static SavedQueryRow? ReadSavedQuery(
        DuckDBConnection con,
        DbTransaction? tx,
        string projectId,
        string savedQueryId)
    {
        using var cmd = con.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT " + SavedQueryRow.SelectColumnList
                          + " FROM saved_queries WHERE project_id = $1 AND saved_query_id = $2";
        AddParameters(cmd, projectId, savedQueryId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? SavedQueryRow.MapFromReader(reader) : null;
    }

    private static bool SavedQueryNameTaken(
        DuckDBConnection con,
        DbTransaction tx,
        string projectId,
        string name,
        string? exceptSavedQueryId)
    {
        using var cmd = con.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM saved_queries WHERE project_id = $1 AND name = $2"
                          + " AND ($3::VARCHAR IS NULL OR saved_query_id <> $3))";
        AddParameters(cmd, projectId, name, exceptSavedQueryId ?? (object)DBNull.Value);
        return cmd.ExecuteScalar() is true;
    }
}
