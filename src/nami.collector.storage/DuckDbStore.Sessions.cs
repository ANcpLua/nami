using DuckDB.NET.Data;

namespace Nami.Collector.Storage;

internal sealed partial class DuckDbStore
{
    private const int SessionFacetValueLimit = 16;

    // The range ($2, $3) selects the sessions with a span starting in it; the aggregates stay over every
    // span of the session, so a listed session has the totals GET /sessions/{id} reports.
    private const string SessionInRangeSql =
        "bool_or(($2::UBIGINT IS NULL OR start_time_unix_nano >= $2) AND ($3::UBIGINT IS NULL OR start_time_unix_nano <= $3))";

    private static string SessionSelectColumns => $$"""
                                                    SELECT
                                                        COALESCE(session_id, trace_id) AS session_id,
                                                        MIN(start_time_unix_nano) AS start_time,
                                                        MAX(end_time_unix_nano) AS last_activity,
                                                        COUNT(*) AS span_count,
                                                        COUNT(DISTINCT trace_id) AS trace_count,
                                                        SUM(CASE WHEN status_code = 2 THEN 1 ELSE 0 END) AS error_count,
                                                        -- Exclude the GenAI agent roll-up span (operation 'invoke_agent'): it reports the
                                                        -- aggregate usage of its child model calls, so summing it with the chat spans
                                                        -- double-counts tokens/requests for an agent session.
                                                        COALESCE(SUM(gen_ai_input_tokens) FILTER (WHERE name NOT LIKE 'invoke_agent%'), 0) AS input_tokens,
                                                        COALESCE(SUM(gen_ai_output_tokens) FILTER (WHERE name NOT LIKE 'invoke_agent%'), 0) AS output_tokens,
                                                        COUNT(*) FILTER (WHERE gen_ai_provider_name IS NOT NULL AND name NOT LIKE 'invoke_agent%') AS genai_request_count,
                                                        MIN(DISTINCT gen_ai_provider_name, {{SessionFacetValueLimit}}) FILTER (WHERE gen_ai_provider_name IS NOT NULL) AS providers,
                                                        MIN(DISTINCT COALESCE(
                                                            NULLIF(TRIM(gen_ai_response_model), ''),
                                                            NULLIF(TRIM(gen_ai_request_model), '')
                                                        ), {{SessionFacetValueLimit}}) FILTER (WHERE COALESCE(
                                                            NULLIF(TRIM(gen_ai_response_model), ''),
                                                            NULLIF(TRIM(gen_ai_request_model), '')
                                                        ) IS NOT NULL) AS models,
                                                        MIN(DISTINCT service_name, {{SessionFacetValueLimit}}) FILTER (WHERE service_name IS NOT NULL) AS services
                                                    FROM spans
                                                    """;

    public async Task<IReadOnlyList<SessionQueryRow>> GetSessionsAsync(
        string projectId,
        ulong activeCutoffUnixNano,
        int limit = 100,
        SessionPageCursor? cursor = null,
        bool? isActive = null,
        DateTimeOffset? startTime = null,
        DateTimeOffset? endTime = null,
        CancellationToken ct = default) =>
        await ExecuteReadAsync<IReadOnlyList<SessionQueryRow>>(con =>
        {
            using var cmd = con.CreateCommand();
            // Keyset order: latest activity first, the session identity breaking ties, so a page
            // boundary is a position in that order rather than an offset that shifts under ingest.
            // CA2100: SessionSelectColumns is a constant; every value is a $n parameter.
            cmd.CommandText = "SELECT * FROM ("
                              + SessionSelectColumns
                              + " WHERE project_id = $1"
                              + " GROUP BY COALESCE(session_id, trace_id)"
                              + " HAVING " + SessionInRangeSql
                              + " AND ($4::BOOLEAN IS NULL OR (MAX(end_time_unix_nano) >= $5) = $4)"
                              + ") AS sessions"
                              + " WHERE $7::UBIGINT IS NULL OR last_activity < $7"
                              + " OR (last_activity = $7 AND session_id < $8::VARCHAR)"
                              + " ORDER BY last_activity DESC, session_id DESC"
                              + " LIMIT $6";

            cmd.Parameters.Add(new DuckDBParameter { Value = projectId });
            AddUnixNanoParam(cmd, startTime);
            AddUnixNanoParam(cmd, endTime);
            cmd.Parameters.Add(new DuckDBParameter { Value = isActive ?? (object)DBNull.Value });
            cmd.Parameters.Add(new DuckDBParameter { Value = (decimal)activeCutoffUnixNano });
            cmd.Parameters.Add(new DuckDBParameter { Value = limit });
            cmd.Parameters.Add(new DuckDBParameter
            {
                Value = cursor is { } after ? (decimal)after.LastActivityUnixNano : DBNull.Value
            });
            cmd.Parameters.Add(new DuckDBParameter { Value = cursor?.SessionId ?? (object)DBNull.Value });
            return ExecuteSessionQuery(cmd);
        }, ct).ConfigureAwait(false);

    public async Task<SessionQueryRow?> GetSessionAsync(
        string sessionId,
        string projectId,
        CancellationToken ct = default)
    {
        var results = await ExecuteReadAsync(con =>
        {
            using var cmd = con.CreateCommand();
            // CA2100: SessionSelectColumns is a constant; the project and session are $1 and $2.
            cmd.CommandText = SessionSelectColumns
                              + " WHERE project_id = $1 AND (session_id = $2 OR (session_id IS NULL AND trace_id = $2))"
                              + " GROUP BY COALESCE(session_id, trace_id)";

            cmd.Parameters.Add(new DuckDBParameter { Value = projectId });
            cmd.Parameters.Add(new DuckDBParameter { Value = sessionId });

            return ExecuteSessionQuery(cmd);
        }, ct).ConfigureAwait(false);

        return results.Count > 0 ? results[0] : null;
    }

    public Task<SessionStatsRow> GetSessionStatsAsync(
        string projectId,
        ulong activeCutoffUnixNano,
        DateTimeOffset? startTime = null,
        DateTimeOffset? endTime = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return ExecuteReadAsync(con =>
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = $$"""
                              WITH sessions AS (
                                  SELECT
                                      COALESCE(session_id, trace_id) AS session_id,
                                      MIN(start_time_unix_nano) AS start_time_unix_nano,
                                      MAX(end_time_unix_nano) AS end_time_unix_nano,
                                      COUNT(*) AS span_count,
                                      SUM(CASE WHEN status_code = 2 THEN 1 ELSE 0 END) AS error_count,
                                      COUNT(CASE WHEN gen_ai_provider_name IS NOT NULL THEN 1 END) AS genai_request_count
                                  FROM spans
                                  WHERE project_id = $1
                                  GROUP BY COALESCE(session_id, trace_id)
                                  HAVING {{SessionInRangeSql}}
                              )
                              SELECT
                                  COUNT(*) AS total_sessions,
                                  COUNT(CASE WHEN end_time_unix_nano >= $4 THEN 1 END) AS active_sessions,
                                  COALESCE(AVG(CASE
                                      WHEN end_time_unix_nano >= start_time_unix_nano
                                          THEN CAST(end_time_unix_nano - start_time_unix_nano AS DOUBLE)
                                      ELSE 0
                                  END), 0) AS avg_duration_ns,
                                  COALESCE(SUM(CASE WHEN error_count > 0 THEN 1 ELSE 0 END), 0) AS sessions_with_errors,
                                  COALESCE(SUM(CASE WHEN genai_request_count > 0 THEN 1 ELSE 0 END), 0) AS sessions_with_genai,
                                  COALESCE(SUM(CASE WHEN span_count <= 1 THEN 1 ELSE 0 END), 0) AS bounced_sessions
                              FROM sessions
                              """;

            cmd.Parameters.Add(new DuckDBParameter { Value = projectId });
            AddUnixNanoParam(cmd, startTime);
            AddUnixNanoParam(cmd, endTime);
            cmd.Parameters.Add(new DuckDBParameter { Value = (decimal)activeCutoffUnixNano });

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return new SessionStatsRow();

            var totalSessions = DuckDbValueReader.ReadInt64(reader, 0, 0);
            var bouncedSessions = DuckDbValueReader.ReadInt64(reader, 5, 0);
            return new SessionStatsRow
            {
                TotalSessions = totalSessions,
                ActiveSessions = DuckDbValueReader.ReadInt64(reader, 1, 0),
                AvgDurationMs = DuckDbValueReader.ReadDouble(reader, 2, 0) / 1_000_000d,
                SessionsWithErrors = DuckDbValueReader.ReadInt64(reader, 3, 0),
                SessionsWithGenAi = DuckDbValueReader.ReadInt64(reader, 4, 0),
                BounceRate = totalSessions > 0 ? (double)bouncedSessions / totalSessions : 0
            };
        }, ct);
    }

    private static void AddUnixNanoParam(DuckDBCommand cmd, DateTimeOffset? value)
    {
        cmd.Parameters.Add(new DuckDBParameter
        {
            Value = value.HasValue
                ? (decimal)NamiTimeConversions.ToUnixNanoUnsigned(value.Value.ToUniversalTime())
                : DBNull.Value
        });
    }

    private static List<SessionQueryRow> ExecuteSessionQuery(DbCommand cmd)
    {
        var sessions = new List<SessionQueryRow>();
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            var startTimeNano = DuckDbValueReader.ReadUInt64(reader, 1, 0);
            var lastActivityNano = DuckDbValueReader.ReadUInt64(reader, 2, 0);
            var startTime = NamiTimeConversions.UnixNanoToDateTime(startTimeNano);
            var lastActivity = NamiTimeConversions.UnixNanoToDateTime(lastActivityNano);

            var spanCount = DuckDbValueReader.ReadInt64(reader, 3, 0);
            var errorCount = DuckDbValueReader.ReadInt64(reader, 5, 0);
            var inputTokens = DuckDbValueReader.ReadInt64(reader, 6, 0);
            var outputTokens = DuckDbValueReader.ReadInt64(reader, 7, 0);

            sessions.Add(new SessionQueryRow
            {
                SessionId = reader.GetString(0),
                StartTime = startTime,
                LastActivity = lastActivity,
                LastActivityUnixNano = lastActivityNano,
                DurationMs = NamiTimeConversions.NanosToMs(lastActivityNano - startTimeNano),
                SpanCount = spanCount,
                TraceCount = DuckDbValueReader.ReadInt64(reader, 4, 0),
                ErrorCount = errorCount,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                GenAiRequestCount = DuckDbValueReader.ReadInt64(reader, 8, 0),
                Providers = ReadStringList(reader, 9),
                Models = ReadStringList(reader, 10),
                Services = ReadStringList(reader, 11)
            });
        }

        return sessions;
    }

    private static IReadOnlyList<string> ReadStringList(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return [];

        var value = reader.GetValue(ordinal);
        return value switch
        {
            IReadOnlyList<string> list => list,
            object[] array => Array.ConvertAll(array, static item => item.ToString() ?? ""),
            _ => []
        };
    }
}

internal sealed record SessionQueryRow
{
    public required string SessionId { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime LastActivity { get; init; }

    /// <summary>The exact keyset position of this row in the session list; DateTime would round it.</summary>
    public ulong LastActivityUnixNano { get; init; }
    public double DurationMs { get; init; }
    public long SpanCount { get; init; }
    public long TraceCount { get; init; }
    public long ErrorCount { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public long GenAiRequestCount { get; init; }
    public IReadOnlyList<string> Providers { get; init; } = [];
    public IReadOnlyList<string> Models { get; init; } = [];
    public IReadOnlyList<string> Services { get; init; } = [];
}
