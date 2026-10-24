using DuckDB.NET.Data;
using Microsoft.Extensions.Logging.Abstractions;
using static System.Threading.Volatile;

namespace Nami.Collector.Storage;

internal sealed partial class DuckDbStore : INamiStore
{

    private const int MaxSpansPerBatch = 100;

    private const int MaxLogsPerBatch = 150;


    private static readonly TimeSpan s_shutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _cts = new();
    private readonly Func<CancellationToken, ValueTask>? _beforeWrite;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _snapshotLifetime;
    private readonly string _databasePath;
    private readonly bool _isInMemory;
    private readonly ILogger<DuckDbStore> _logger;
    private readonly Channel<WriteJob> _jobs;
    private readonly Channel<IReadJob>? _reads;
    private readonly Task[] _readerTasks;
    private readonly Task _writerTask;
    private int _disposed;


    public DuckDbStore(
        string databasePath,
        int jobQueueCapacity = 1000,
        int maxConcurrentReads = 8,
        int readQueueCapacity = 1000,
        string? memoryLimit = null,
        int? threads = null,
        string? tempDirectory = null,
        Func<CancellationToken, ValueTask>? beforeWrite = null,
        ILoggerFactory? loggerFactory = null,
        TimeProvider? timeProvider = null,
        TimeSpan? snapshotLifetime = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _snapshotLifetime = snapshotLifetime ?? TimeSpan.FromMinutes(30);
        var resolvedLoggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = resolvedLoggerFactory.CreateLogger<DuckDbStore>();
        _isInMemory = databasePath == ":memory:";
        _databasePath = _isInMemory ? databasePath : Path.GetFullPath(databasePath);
        var connectionString = $"DataSource={_databasePath};vacuum_rebuild_indexes={ulong.MaxValue}";
        var boundedJobQueueCapacity = Math.Max(1, jobQueueCapacity);
        _beforeWrite = beforeWrite;
        var connection = new DuckDBConnection(connectionString);
        try
        {
            connection.Open();
            ConfigureDatabase(connection, memoryLimit, threads, tempDirectory);
            var preservedHistory = PreserveHistoricalMetricPoints(connection);
            MigrateUnknownLogTimes(connection);
            var resetDerived = InitializeSchema(connection);
            var migratedSeries = BackfillMetricInstrumentIds(connection);
            if (preservedHistory || resetDerived || migratedSeries || MetricProjectionsNeedRebuild(connection))
                RebuildMetricProjections(connection);
        }
        catch
        {
            connection.Dispose();
            throw;
        }

        Connection = connection;

        _jobs = Channel.CreateBounded<WriteJob>(new BoundedChannelOptions(boundedJobQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false
        });
        _writerTask = Task.Run(WriterLoopAsync);

        // DuckDB.NET's *Async methods are synchronous-over-async: the embedded engine has no IO to
        // await, so every read blocks its calling thread for the full query. To keep that blocking
        // off the shared thread pool (Kestrel), reads run on a dedicated set of OS threads, each with
        // its own connection. DuckDB.NET caches one native database instance per file path, so every
        // connection to that file shares the instance and reads are MVCC-concurrent with the writer.
        // In-memory mode has no shared on-disk instance, so its reads are serialized through the
        // single writer connection instead.
        if (_isInMemory)
        {
            _readerTasks = [];
            _reads = null;
        }
        else
        {
            var concurrency = Math.Max(1, maxConcurrentReads);
            _reads = Channel.CreateBounded<IReadJob>(new BoundedChannelOptions(readQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait, SingleReader = false, SingleWriter = false
            });

            // Reader connections use the same connection string as the writer on purpose: DuckDB.NET
            // keys its native-instance cache by file path alone, so an ACCESS_MODE=READ_ONLY token
            // here would be silently ignored on reuse (the writer already opened the instance
            // read-write). Sharing that one instance is exactly what makes these reads MVCC-concurrent.
            // Open every reader connection up front (on this thread) so a failure surfaces loudly and
            // immediately as a startup error, not asynchronously on a background thread.
            var connections = new DuckDBConnection[concurrency];
            for (var i = 0; i < concurrency; i++)
            {
                var con = new DuckDBConnection(connectionString);
                con.Open();
                connections[i] = con;
            }

            _readerTasks = new Task[concurrency];
            for (var i = 0; i < concurrency; i++)
            {
                var con = connections[i];
                _readerTasks[i] = Task.Factory.StartNew(
                        static state =>
                        {
                            var owner = ((DuckDbStore Store, DuckDBConnection Connection))state!;
                            return owner.Store.ReaderLoopAsync(owner.Connection);
                        },
                        (Store: this, Connection: con),
                        CancellationToken.None,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default)
                    .Unwrap();
            }
        }
    }


    private DuckDBConnection Connection { get; }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
            return;

        List<Exception>? shutdownErrors = null;

        _jobs.Writer.TryComplete();
        _reads?.Writer.TryComplete();
        await _cts.CancelAsync().ConfigureAwait(false);

        try
        {
            await _writerTask.WaitAsync(s_shutdownTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            AddShutdownError(ref shutdownErrors, new TimeoutException("DuckDB writer did not stop before shutdown timeout.", ex));
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            _jobs.Writer.TryComplete();
        }
        catch (Exception ex)
        {
            AddShutdownError(ref shutdownErrors, ex);
        }

        foreach (var readerTask in _readerTasks)
        {
            try
            {
                await readerTask.WaitAsync(s_shutdownTimeout).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                // Shutdown cancelled this reader on purpose; only unexpected failures are shutdown errors.
            }
            catch (Exception ex)
            {
                AddShutdownError(ref shutdownErrors, ex);
            }
        }

        if (_reads is not null)
            while (_reads.Reader.TryRead(out var leftover))
                leftover.Cancel();

        // Flush the WAL into the main database file and truncate it so the next start is a clean,
        // fast open instead of a WAL replay. Pointless for an in-memory database.
        if (!_isInMemory)
        {
            try
            {
                await using var checkpoint = Connection.CreateCommand();
                checkpoint.CommandText = "CHECKPOINT";
                await checkpoint.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            catch (DuckDBException ex)
            {
                // A failed final checkpoint is not data loss (the WAL is replayed on next open), but
                // it must surface and must not abort the rest of teardown — route it into the
                // shutdown AggregateException instead of throwing past Connection.Dispose().
                AddShutdownError(
                    ref shutdownErrors,
                    new InvalidOperationException("DuckDB shutdown CHECKPOINT failed.", ex));
            }
        }

        Connection.Dispose();
        _cts.Dispose();

        if (shutdownErrors is { Count: > 0 })
            throw new AggregateException("DuckDB store did not shut down cleanly.", shutdownErrors);
    }


    private async Task<T> ExecuteReadAsync<T>(Func<DuckDBConnection, T> read, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        // In-memory mode shares the single writer connection (not safe for concurrent use), so its
        // reads are serialized through the writer queue rather than the dedicated reader pool.
        if (_reads is null)
            return await ExecuteWriteAsync((con, _) => new ValueTask<T>(read(con)), ct).ConfigureAwait(false);

        var job = new ReadJob<T>(read, ct);
        await _reads.Writer.WriteAsync(job, ct).ConfigureAwait(false);
        return await job.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    private async Task<T> ExecuteWriteAsync<T>(Func<DuckDBConnection, CancellationToken, ValueTask<T>> operation,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        var job = new WriteJob<T>(operation, ct);
        if (!_jobs.Writer.TryWrite(job))
            throw new NamiStoreUnavailableException("DuckDB write queue is at capacity.");
        job.ArmCancellation();
        return await job.Task.ConfigureAwait(false);
    }

    private async Task ExecuteWriteAsync(Func<DuckDBConnection, CancellationToken, ValueTask> operation,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        var job = new WriteJob<int>(async (con, token) =>
        {
            await operation(con, token).ConfigureAwait(false);
            return 0;
        }, ct);
        if (!_jobs.Writer.TryWrite(job))
            throw new NamiStoreUnavailableException("DuckDB write queue is at capacity.");
        job.ArmCancellation();
        await job.Task.ConfigureAwait(false);
    }

    private async Task<T> ExecuteMaintenanceWriteAsync<T>(
        Func<DuckDBConnection, CancellationToken, ValueTask<T>> operation,
        CancellationToken ct)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        var job = new WriteJob<T>(operation, ct);
        await _jobs.Writer.WriteAsync(job, ct).ConfigureAwait(false);
        job.ArmCancellation();
        return await job.Task.ConfigureAwait(false);
    }


    public async ValueTask EnqueueAsync(SpanBatch batch, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (ct.IsCancellationRequested)
            await ValueTask.FromCanceled(ct).ConfigureAwait(false);
        if (batch.Spans.Count is 0)
        {
            return;
        }

        await ExecuteWriteAsync(
                (con, token) => WriteBatchInternalAsync(con, batch, token),
                ct)
            .ConfigureAwait(false);
    }

    public Task<IReadOnlyList<SpanStorageRow>> GetTraceAsync(
        string traceId,
        string projectId,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return ExecuteReadAsync<IReadOnlyList<SpanStorageRow>>(con =>
        {
            var spans = new List<SpanStorageRow>();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT " + SpanStorageRow.SelectColumnList
                                        + " FROM spans WHERE project_id = $1 AND trace_id = $2 ORDER BY start_time_unix_nano ASC";
            cmd.Parameters.Add(new DuckDBParameter { Value = projectId });
            cmd.Parameters.Add(new DuckDBParameter { Value = traceId });

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                spans.Add(SpanStorageRow.MapFromReader(reader));

            return spans;
        }, ct);
    }

    public Task<IReadOnlyList<SpanStorageRow>> GetSpansAsync(
        string projectId,
        int limit = 100,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return ExecuteReadAsync<IReadOnlyList<SpanStorageRow>>(con =>
        {
            var spans = new List<SpanStorageRow>();
            var qb = new QueryBuilder();

            qb.Add("project_id = $N", projectId);

            using var cmd = con.CreateCommand();
            // CA2100: the generated column list, QueryBuilder's $n predicates and the limit's placeholder; values are parameters.
            cmd.CommandText = "SELECT " + SpanStorageRow.SelectColumnList
                                        + " FROM spans " + qb.WhereClause
                                        + " ORDER BY start_time_unix_nano DESC LIMIT "
                                        + qb.NextParam.ToString(CultureInfo.InvariantCulture);

            qb.ApplyTo(cmd);
            cmd.Parameters.Add(new DuckDBParameter { Value = limit });

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                spans.Add(SpanStorageRow.MapFromReader(reader));

            return spans;
        }, ct);
    }

    public Task<IReadOnlyList<SpanStorageRow>> GetTraceSpanPageAsync(
        string traceId,
        string projectId,
        SpanPageCursor? cursor,
        int? limit,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (limit < 1)
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Span page limit must be positive.");

        return ExecuteReadAsync<IReadOnlyList<SpanStorageRow>>(con =>
        {
            var spans = new List<SpanStorageRow>();
            using var cmd = con.CreateCommand();
            // CA2100: the generated column list only; every value is a $n parameter.
            cmd.CommandText = "SELECT " + SpanStorageRow.SelectColumnList
                                        + " FROM spans WHERE project_id = $1 AND trace_id = $2"
                                        + " AND ($3::UBIGINT IS NULL OR start_time_unix_nano > $3"
                                        + " OR (start_time_unix_nano = $3 AND span_id > $4::VARCHAR))"
                                        + " ORDER BY start_time_unix_nano ASC, span_id ASC"
                                        + (limit.HasValue ? " LIMIT $5" : "");
            cmd.Parameters.Add(new DuckDBParameter { Value = projectId });
            cmd.Parameters.Add(new DuckDBParameter { Value = traceId });
            cmd.Parameters.Add(new DuckDBParameter
            {
                Value = cursor is { } after ? (decimal)after.StartTimeUnixNano : DBNull.Value
            });
            cmd.Parameters.Add(new DuckDBParameter { Value = cursor?.SpanId ?? (object)DBNull.Value });
            if (limit is { } bounded)
                cmd.Parameters.Add(new DuckDBParameter { Value = bounded });

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                spans.Add(SpanStorageRow.MapFromReader(reader));

            return spans;
        }, ct);
    }

    public Task<TraceStoragePage> GetTracePageAsync(
        TracePageQuery query,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (query.Limit < 1)
            throw new ArgumentOutOfRangeException(nameof(query), query.Limit, "Trace page limit must be positive.");

        var limit = query.Limit;
        var projectId = query.ProjectId;
        return ExecuteReadAsync(con =>
        {
            var heads = new List<(string TraceId, ulong ActivityUnixNano)>(limit + 1);
            using (var headCommand = con.CreateCommand())
            {
                // Every filter is a HAVING over the whole trace, so a trace is listed whole or not
                // at all. A session's traces are the traces any of its spans touched, under the
                // session identity COALESCE(session_id, trace_id) the session list aggregates by.
                headCommand.CommandText = """
                                          WITH trace_heads AS (
                                              SELECT trace_id, MAX(end_time_unix_nano) AS activity_unix_nano
                                              FROM spans
                                              WHERE project_id = $1
                                                AND ($2::VARCHAR IS NULL OR trace_id IN (SELECT DISTINCT trace_id FROM spans WHERE project_id = $1 AND (session_id = $2 OR (session_id IS NULL AND trace_id = $2))))
                                              GROUP BY trace_id
                                              HAVING ($3::UBIGINT IS NULL OR MIN(start_time_unix_nano) >= $3)
                                                 AND ($4::UBIGINT IS NULL OR MIN(start_time_unix_nano) < $4)
                                                 AND ($5::VARCHAR IS NULL OR bool_or(service_name = $5))
                                                 AND (NOT $6::BOOLEAN OR bool_or(status_code = 2))
                                          )
                                          SELECT trace_id, activity_unix_nano
                                          FROM trace_heads
                                          WHERE $7::UBIGINT IS NULL
                                             OR activity_unix_nano < $7
                                             OR (activity_unix_nano = $7 AND trace_id < $8::VARCHAR)
                                          ORDER BY activity_unix_nano DESC, trace_id DESC
                                          LIMIT $9
                                          """;
                headCommand.Parameters.Add(new DuckDBParameter { Value = projectId });
                headCommand.Parameters.Add(new DuckDBParameter { Value = query.SessionId ?? (object)DBNull.Value });
                headCommand.Parameters.Add(new DuckDBParameter
                {
                    Value = query.StartUnixNano is { } start ? (decimal)start : DBNull.Value
                });
                headCommand.Parameters.Add(new DuckDBParameter
                {
                    Value = query.EndUnixNano is { } end ? (decimal)end : DBNull.Value
                });
                headCommand.Parameters.Add(new DuckDBParameter { Value = query.ServiceName ?? (object)DBNull.Value });
                headCommand.Parameters.Add(new DuckDBParameter { Value = query.ErrorsOnly });
                headCommand.Parameters.Add(new DuckDBParameter
                {
                    Value = query.Cursor is { } after ? (decimal)after.ActivityUnixNano : DBNull.Value
                });
                headCommand.Parameters.Add(new DuckDBParameter { Value = query.Cursor?.TraceId ?? (object)DBNull.Value });
                headCommand.Parameters.Add(new DuckDBParameter { Value = limit + 1 });
                using var headReader = headCommand.ExecuteReader();
                while (headReader.Read())
                {
                    heads.Add((
                        DuckDbValueReader.ReadString(headReader, 0, string.Empty),
                        DuckDbValueReader.ReadUInt64(headReader, 1, 0)));
                }
            }

            var hasMore = heads.Count > limit;
            if (hasMore) heads.RemoveRange(limit, heads.Count - limit);
            if (heads.Count is 0) return new TraceStoragePage([], HasMore: false);

            var spansByTrace = heads.ToDictionary(
                static head => head.TraceId,
                static _ => new List<SpanStorageRow>(),
                StringComparer.Ordinal);
            using (var spansCommand = con.CreateCommand())
            {
                var placeholders = string.Join(", ",
                    Enumerable.Range(2, heads.Count).Select(static index =>
                        "$" + index.ToString(CultureInfo.InvariantCulture)));
                // CA2100: the generated column list and $n placeholders; the trace ids are bound parameters.
                spansCommand.CommandText = "SELECT " + SpanStorageRow.SelectColumnList
                                           + " FROM spans WHERE project_id = $1 AND trace_id IN ("
                                           + placeholders
                                           + ") ORDER BY trace_id DESC, start_time_unix_nano ASC, span_id ASC";
                spansCommand.Parameters.Add(new DuckDBParameter { Value = projectId });
                foreach (var head in heads)
                    spansCommand.Parameters.Add(new DuckDBParameter { Value = head.TraceId });

                using var spanReader = spansCommand.ExecuteReader();
                while (spanReader.Read())
                {
                    var span = SpanStorageRow.MapFromReader(spanReader);
                    spansByTrace[span.TraceId].Add(span);
                }
            }

            return new TraceStoragePage(
                [.. heads.Select(head => new TraceStoragePageItem(
                    head.TraceId,
                    head.ActivityUnixNano,
                    spansByTrace[head.TraceId]))],
                hasMore);
        }, ct);
    }


    public Task<StorageStats> GetStorageStatsAsync(
        string projectId,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return ExecuteReadAsync(con =>
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = """
                              SELECT
                                  (SELECT COUNT(*) FROM spans WHERE project_id = $1) as span_count,
                                  (SELECT COUNT(DISTINCT COALESCE(session_id, trace_id)) FROM spans WHERE project_id = $1) as session_count
                              """;
            cmd.Parameters.Add(new DuckDBParameter { Value = projectId });

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new StorageStats
                {
                    SpanCount = DuckDbValueReader.ReadInt64(reader, 0, 0),
                    SessionCount = DuckDbValueReader.ReadInt64(reader, 1, 0)
                };
            }

            return new StorageStats();
        }, ct);
    }

    private void AddShutdownError(ref List<Exception>? errors, Exception error)
    {
        errors ??= [];
        errors.Add(error);
        DuckDbStoreLog.ShutdownFailed(_logger, error);
    }

    public async Task InsertLogsAsync(IReadOnlyList<LogStorageRow> logs, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (logs.Count is 0)
            return;

        IReadOnlyList<LogStorageRow> knownTimes = logs.Any(static row => row.TimeUnixNano == 0 || row.ObservedTimeUnixNano == 0)
            ? logs.Select(static row => row with
            { TimeUnixNano = row.TimeUnixNano is > 0 ? row.TimeUnixNano : null,
              ObservedTimeUnixNano = row.ObservedTimeUnixNano is > 0 ? row.ObservedTimeUnixNano : null }).ToArray()
            : logs;
        await ExecuteWriteAsync(async (con, token) =>
        {
            await using var tx = await con.BeginTransactionAsync(token).ConfigureAwait(false);
            await InsertRowsBatchedAsync(con, tx, knownTimes, LogStorageRow.AddParameters,
                LogStorageRow.BuildMultiRowInsertSql, MaxLogsPerBatch, token);
            RecordIngestion(con, tx, "logs", knownTimes.Select(static row => new ArrivingResource(row.ProjectId, row.ServiceName, row.ResourceJson, row.AttributesJson, row.ScopeAttributesJson, row.AttributeRenames)), token);
            await tx.CommitAsync(token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    // The search text matches literally (its LIKE metacharacters are escaped under ESCAPE '\') and
    // against the text as it was logged. $A is the text itself, for the body (decoded from its JSON
    // first), the severity and the service. $B is the text as JSON writes it, for the attributes:
    // json() re-renders them with non-ASCII characters literal but quotes, backslashes and control
    // characters still escaped. A body that is not JSON is a raw-text row from before attribute value
    // storage and is searched as it is.
    private const string LogSearchSql =
        "(CASE WHEN json_valid(body) THEN body ->> '$' ELSE body END ILIKE $A ESCAPE '\\'"
        + " OR severity_text ILIKE $A ESCAPE '\\'"
        + " OR service_name ILIKE $A ESCAPE '\\'"
        + " OR CASE WHEN json_valid(attributes_json) THEN json(attributes_json)::VARCHAR ELSE attributes_json END ILIKE $B ESCAPE '\\')";

    private static string LogSearchPattern(string search) =>
        "%" + search
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal) + "%";

    // The search text as it appears inside a JSON string: quotes, backslashes and control characters
    // escaped, every other character literal.
    private static string JsonStringContent(string search)
    {
        var builder = new StringBuilder(search.Length);
        foreach (var character in search)
        {
            _ = character switch
            {
                '"' => builder.Append("\\\""),
                '\\' => builder.Append(@"\\"),
                '\n' => builder.Append(@"\n"),
                '\r' => builder.Append(@"\r"),
                '\t' => builder.Append(@"\t"),
                '\b' => builder.Append(@"\b"),
                '\f' => builder.Append(@"\f"),
                < ' ' => builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)character:x4}"),
                _ => builder.Append(character)
            };
        }

        return builder.ToString();
    }

    public Task<IReadOnlyList<LogStorageRow>> GetLogsAsync(
        string projectId,
        string? sessionId = null,
        string? traceId = null,
        string? severityText = null,
        int? minSeverity = null,
        string? search = null,
        ulong? start = null,
        ulong? before = null,
        string? serviceName = null,
        int limit = 500,
        LogPageCursor? cursor = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return ExecuteReadAsync<IReadOnlyList<LogStorageRow>>(con =>
        {
            var logs = new List<LogStorageRow>();
            var qb = new QueryBuilder();

            qb.Add("project_id = $N", projectId);
            if (!string.IsNullOrEmpty(sessionId))
                qb.Add("session_id = $N", sessionId);
            if (!string.IsNullOrEmpty(traceId))
                qb.Add("trace_id = $N", traceId);
            if (!string.IsNullOrEmpty(severityText))
                qb.Add("severity_text = $N", severityText);
            if (minSeverity.HasValue)
                qb.Add("severity_number >= $N", minSeverity.Value);
            if (!string.IsNullOrWhiteSpace(search))
                qb.Add(LogSearchSql, LogSearchPattern(search), LogSearchPattern(JsonStringContent(search)));
            if (!string.IsNullOrEmpty(serviceName))
                qb.Add("service_name = $N", serviceName);
            if (start.HasValue)
                qb.Add(LogStorageRow.EffectiveTimeSql + " >= $N", (decimal)start.Value);
            if (before.HasValue)
                qb.Add(LogStorageRow.EffectiveTimeSql + " <= $N", (decimal)before.Value);
            if (cursor is { TimeUnixNano: { } time } after)
                qb.Add("(" + LogStorageRow.EffectiveTimeSql + " IS NULL OR " + LogStorageRow.EffectiveTimeSql + " < $A OR (" + LogStorageRow.EffectiveTimeSql + " = $A AND log_id < $B))",
                    (decimal)time, after.LogId);
            else if (cursor is { } unknownTime)
                qb.Add("(" + LogStorageRow.EffectiveTimeSql + " IS NULL AND log_id < $N)", unknownTime.LogId);

            using var cmd = con.CreateCommand();
            // CA2100: the generated column list, QueryBuilder's $n predicates and the limit's placeholder; values are parameters.
            cmd.CommandText = "SELECT " + LogStorageRow.SelectColumnList
                              + " FROM logs " + qb.WhereClause
                              + " ORDER BY " + LogStorageRow.EffectiveTimeSql + " DESC, log_id DESC LIMIT "
                              + qb.NextParam.ToString(CultureInfo.InvariantCulture);

            qb.ApplyTo(cmd);
            cmd.Parameters.Add(new DuckDBParameter { Value = limit });

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                logs.Add(LogStorageRow.MapFromReader(reader));

            return logs;
        }, ct);
    }

    public Task<IReadOnlyList<LogStorageRow>> GetLogStreamPageAsync(
        string projectId,
        string? serviceName = null,
        int? minSeverity = null,
        string? search = null,
        long? afterIngestSequence = null,
        int limit = 250,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return ExecuteReadAsync<IReadOnlyList<LogStorageRow>>(con =>
        {
            var logs = new List<LogStorageRow>();
            var qb = new QueryBuilder();

            qb.Add("project_id = $N", projectId);
            if (!string.IsNullOrEmpty(serviceName))
                qb.Add("service_name = $N", serviceName);
            if (minSeverity.HasValue)
                qb.Add("severity_number >= $N", minSeverity.Value);
            if (!string.IsNullOrWhiteSpace(search))
                qb.Add(LogSearchSql, LogSearchPattern(search), LogSearchPattern(JsonStringContent(search)));
            if (afterIngestSequence.HasValue)
                qb.Add("ingest_sequence > $N", afterIngestSequence.Value);

            using var cmd = con.CreateCommand();
            if (afterIngestSequence.HasValue)
            {
                // CA2100: the generated column list, QueryBuilder's $n predicates and the limit's placeholder; values are parameters.
                cmd.CommandText = "SELECT " + LogStorageRow.SelectColumnList
                                  + " FROM logs " + qb.WhereClause
                                  + " ORDER BY ingest_sequence ASC LIMIT "
                                  + qb.NextParam.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                // CA2100: the generated column list, QueryBuilder's $n predicates and the limit's placeholder; values are parameters.
                cmd.CommandText = "SELECT " + LogStorageRow.SelectColumnList
                                  + " FROM (SELECT " + LogStorageRow.SelectColumnList
                                  + " FROM logs " + qb.WhereClause
                                  + " ORDER BY ingest_sequence DESC LIMIT "
                                  + qb.NextParam.ToString(CultureInfo.InvariantCulture)
                                  + ") AS latest_logs ORDER BY ingest_sequence ASC";
            }

            qb.ApplyTo(cmd);
            cmd.Parameters.Add(new DuckDBParameter { Value = limit });

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                logs.Add(LogStorageRow.MapFromReader(reader));

            return logs;
        }, ct);
    }


    private static async Task InsertRowsBatchedAsync<T>(
        DuckDBConnection con, DbTransaction tx, IReadOnlyList<T> rows,
        Action<DuckDBCommand, T> addParams, Func<int, string> buildSql, int maxBatch, CancellationToken ct)
    {
        if (rows.Count is 0) return;
        var offset = 0;
        while (offset < rows.Count)
        {
            var chunk = Math.Min(maxBatch, rows.Count - offset);
            await using var cmd = con.CreateCommand();
            cmd.Transaction = tx;
            // CA2100: buildSql is a generated row type's multi-row INSERT of $n placeholders; the rows are parameters.
            cmd.CommandText = buildSql(chunk);
            for (var i = 0; i < chunk; i++) addParams(cmd, rows[offset + i]);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            offset += chunk;
        }
    }

    private async Task WriterLoopAsync()
    {
        try
        {
            await foreach (var job in _jobs.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                if (!job.TryClaim())
                    continue;
                try
                {
                    if (_beforeWrite is not null)
                        await _beforeWrite(_cts.Token).ConfigureAwait(false);
                    await job.ExecuteAsync(Connection, _cts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    job.OnAborted(ex);
                }
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            // Store disposal cancels the worker; that is the expected exit path.
        }
        catch (Exception error)
        {
            DuckDbStoreLog.StorageWorkerFailed(_logger, error);
        }
        finally
        {
            while (_jobs.Reader.TryRead(out var leftover))
            {
                leftover.OnAborted(new OperationCanceledException("Store is shutting down."));
            }
        }
    }

    // One long-running reader task per slot owns a private connection. Synchronous
    // point reads begin on its dedicated thread; Arrow jobs may yield between
    // batches without blocking a request thread or sharing the connection.
    private async Task ReaderLoopAsync(DuckDBConnection con)
    {
        var reader = _reads!.Reader;
        try
        {
            while (await reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
            {
                while (reader.TryRead(out var job))
                {
                    try
                    {
                        await job.ExecuteAsync(con).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException oce)
                    {
                        job.Abort(oce);
                    }
                    catch (Exception ex)
                    {
                        job.Abort(ex);
                    }
                }
            }
        }
        finally
        {
            con.Dispose();
        }
    }

    private async ValueTask WriteBatchInternalAsync(
        DuckDBConnection con,
        SpanBatch batch,
        CancellationToken ct)
    {
        if (batch.Spans.Count is 0)
            return;

        await using var tx = await con.BeginTransactionAsync(ct).ConfigureAwait(false);

        await InsertRowsBatchedAsync(con, tx, batch.Spans, SpanStorageRow.AddParameters,
            SpanStorageRow.BuildMultiRowInsertSql, MaxSpansPerBatch, ct);
        RecordIngestion(con, tx, "traces", batch.Spans.Select(static row => new ArrivingResource(row.ProjectId, row.ServiceName, row.ResourceJson, row.AttributesJson, row.ScopeAttributesJson, row.AttributeRenames)), ct);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    private static void ConfigureDatabase(DuckDBConnection con, string? memoryLimit, int? threads, string? tempDirectory)
    {
        // Tuning for a write-heavy embedded store. preserve_insertion_order=false drops the
        // bookkeeping that keeps physical row order — telemetry never needs it — which cuts ingest
        // memory and lifts bulk-write throughput. memory_limit / threads / temp_directory are
        // operator-supplied (trusted config); when unset, DuckDB's own defaults apply.
        ExecutePragma(con, "SET preserve_insertion_order = false");

        if (!string.IsNullOrWhiteSpace(memoryLimit))
            ExecutePragma(con, $"SET memory_limit = '{EscapeSqlLiteral(memoryLimit)}'");

        if (threads is > 0)
            ExecutePragma(con, $"SET threads = {threads.Value.ToString(CultureInfo.InvariantCulture)}");

        if (!string.IsNullOrWhiteSpace(tempDirectory))
            ExecutePragma(con, $"SET temp_directory = '{EscapeSqlLiteral(tempDirectory)}'");
    }

    private static void ExecutePragma(DuckDBConnection con, string sql)
    {
        using var cmd = con.CreateCommand();
        // CA2100: the SET statements ConfigureDatabase writes from operator config; strings go through EscapeSqlLiteral.
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // SET ... pragmas take no bound parameters, so operator-supplied values are interpolated.
    // The values are trusted config, but escape single quotes anyway so a stray quote can't break
    // the statement.
    private static string EscapeSqlLiteral(string value) => value.Replace("'", "''");

    private static bool InitializeSchema(DuckDBConnection con)
    {
        using var transaction = con.BeginTransaction();
        ExecuteSchemaSql(con, transaction, DuckDbGeneratedSchema.BootstrapDdl);

        var stored = ReadSchemaIdentity(con, transaction);
        var authoritativeMismatches = FindSchemaMismatches(
            con,
            transaction,
            DuckDbGeneratedSchema.AuthoritativeTables, allowMissingNonUniqueIndexes: true);
        if (stored is { AuthoritativeHash: not DuckDbGeneratedSchema.AuthoritativeHash })
        {
            var appended = PlanAdditiveRelease(con, transaction, authoritativeMismatches);
            if (appended is null)
            {
                authoritativeMismatches = DuckDbGeneratedSchema.AuthoritativeTableNames;
            }
            else
            {
                AppendColumns(con, transaction, appended);
                authoritativeMismatches = [];
            }
        }
        var derivedMismatches = FindSchemaMismatches(
            con,
            transaction,
            DuckDbGeneratedSchema.DerivedTables);
        var resetDerived = derivedMismatches.Count > 0 ||
            stored is { DerivedHash: not DuckDbGeneratedSchema.DerivedHash };

        if (authoritativeMismatches.Count > 0)
        {
            if (HasRows(
                    con,
                    transaction,
                    DuckDbGeneratedSchema.AuthoritativeTableNames))
            {
                var difference = stored is { AuthoritativeHash: not DuckDbGeneratedSchema.AuthoritativeHash }
                    ? $"stored authoritative DDL hash {stored.AuthoritativeHash} does not match " +
                      DuckDbGeneratedSchema.AuthoritativeHash
                    : $"incompatible tables: {string.Join(", ", authoritativeMismatches)}";
                throw new NamiSchemaMismatchException(
                    "The authoritative DuckDB schema differs from this build while persisted data exists, " +
                    "beyond the tables and trailing nullable columns a release may add. Start with a new " +
                    "database and replay or import the authoritative journal; nami will not rewrite or delete " +
                    $"that data automatically. Difference: {difference}");
            }

            DropExpectedIndexes(
                con,
                transaction,
                DuckDbGeneratedSchema.AuthoritativeTables.Where(
                    table => authoritativeMismatches.Contains(table.Name, StringComparer.Ordinal)));
            DropTables(con, transaction, authoritativeMismatches);
            if (authoritativeMismatches.Contains(LogStorageRow.TableName, StringComparer.Ordinal))
                ExecuteSchemaSql(con, transaction, "DROP SEQUENCE IF EXISTS logs_ingest_sequence");
            CreateSchema(con, transaction, DuckDbGeneratedSchema.AuthoritativeDdl);
            CreateSchema(con, transaction, DuckDbGeneratedSchema.DerivedDdl);
        }
        else
        {
            // A schema created before nami_schema_meta can be adopted only after its
            // complete generated structure has been checked above. Derived state is
            // never migrated: a mismatch is repaired by deterministic recreation.
            CreateSchema(con, transaction, DuckDbGeneratedSchema.AuthoritativeDdl);
            if (derivedMismatches.Count > 0 ||
                stored is { DerivedHash: not DuckDbGeneratedSchema.DerivedHash })
            {
                DropExpectedIndexes(con, transaction, DuckDbGeneratedSchema.DerivedTables);
                DropGeneratedTables(con, transaction, includeAuthoritative: false);
            }
            CreateSchema(con, transaction, DuckDbGeneratedSchema.DerivedDdl);
        }

        DropRetiredTables(con, transaction);
        WriteSchemaIdentity(con, transaction);
        transaction.Commit();

        VerifyPersistedPrimaryKeys(con);
        return resetDerived;
    }

    // A release may add authoritative tables (annotations and saved queries joined the telemetry
    // tables this way) and append nullable columns to an existing one (the span instrumentation
    // scope joined this way). Both keep every stored row: an absent table is created empty by the
    // IF NOT EXISTS DDL, an appended column is added with NULL in every existing row. Each present
    // table must otherwise be this build's, column for column; anything else (a changed, removed or
    // reordered column, a changed default or primary key, a required or defaulted new column) is
    // not additive and fails closed while data exists. Returns the columns to append per table, or
    // null when the difference is not additive.
    private static IReadOnlyList<(DuckDbExpectedTable Table, DuckDbExpectedColumn[] Columns)>? PlanAdditiveRelease(
        DuckDBConnection con,
        DbTransaction transaction,
        IReadOnlyList<string> mismatches)
    {
        var plan = new List<(DuckDbExpectedTable, DuckDbExpectedColumn[])>();
        foreach (var name in mismatches)
        {
            if (!TableExists(con, transaction, name))
                continue;

            var table = DuckDbGeneratedSchema.AuthoritativeTables.Single(
                expected => string.Equals(expected.Name, name, StringComparison.Ordinal));
            var actual = ReadColumns(con, transaction, name);
            if (actual.Count > table.Columns.Length)
                return null;
            for (var ordinal = 0; ordinal < actual.Count; ordinal++)
            {
                if (!ColumnMatches(actual[ordinal], table.Columns[ordinal]))
                    return null;
            }

            var appended = table.Columns[actual.Count..];
            if (appended.Any(static column => column.Required || column.DefaultSql is not null || column.PrimaryKeyOrdinal >= 0))
                return null;
            plan.Add((table, appended));
        }

        return plan;
    }

    private static void AppendColumns(
        DuckDBConnection con,
        DbTransaction transaction,
        IReadOnlyList<(DuckDbExpectedTable Table, DuckDbExpectedColumn[] Columns)> plan)
    {
        foreach (var (table, columns) in plan)
        {
            foreach (var column in columns)
            {
                ExecuteSchemaSql(
                    con,
                    transaction,
                    $"ALTER TABLE {QuoteIdentifier(table.Name)} ADD COLUMN {QuoteIdentifier(column.Name)} {column.SqlType}");
            }
        }
    }

    private static SchemaIdentity? ReadSchemaIdentity(
        DuckDBConnection con,
        DbTransaction transaction)
    {
        using var command = con.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
                              SELECT authoritative_schema_hash, derived_schema_hash
                              FROM nami_schema_meta
                              WHERE singleton = 0
                              """;
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new SchemaIdentity(reader.GetString(0), reader.GetString(1))
            : null;
    }

    private static void WriteSchemaIdentity(
        DuckDBConnection con,
        DbTransaction transaction)
    {
        using var command = con.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
                              INSERT INTO nami_schema_meta
                                  (singleton, authoritative_schema_hash, derived_schema_hash)
                              VALUES (0, $1, $2)
                              ON CONFLICT (singleton) DO UPDATE SET
                                  authoritative_schema_hash = excluded.authoritative_schema_hash,
                                  derived_schema_hash = excluded.derived_schema_hash,
                                  updated_at = now()
                              """;
        AddParameters(
            command,
            DuckDbGeneratedSchema.AuthoritativeHash,
            DuckDbGeneratedSchema.DerivedHash);
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<string> FindSchemaMismatches(
        DuckDBConnection con,
        DbTransaction transaction,
        IReadOnlyList<DuckDbExpectedTable> expectedTables,
        bool allowMissingNonUniqueIndexes = false)
    {
        var mismatches = new List<string>();
        foreach (var table in expectedTables)
        {
            var actual = ReadColumns(con, transaction, table.Name);
            var tableMatches = actual.Count == table.Columns.Length;
            for (var ordinal = 0; tableMatches && ordinal < actual.Count; ordinal++)
                tableMatches = ColumnMatches(actual[ordinal], table.Columns[ordinal]);

            if (!tableMatches || !IndexesMatch(con, transaction, table, allowMissingNonUniqueIndexes))
                mismatches.Add(table.Name);
        }

        return mismatches;
    }

    private sealed record ActualColumn(string Name, string Type, bool Nullable, string? Default);

    private static List<ActualColumn> ReadColumns(
        DuckDBConnection con,
        DbTransaction transaction,
        string table)
    {
        var columns = new List<ActualColumn>();
        using var command = con.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
                              SELECT column_name, data_type, is_nullable, column_default
                              FROM duckdb_columns()
                              WHERE schema_name = 'main' AND table_name = $1
                              ORDER BY column_index
                              """;
        AddParameters(command, table);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(new ActualColumn(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetBoolean(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return columns;
    }

    private static bool ColumnMatches(ActualColumn actual, DuckDbExpectedColumn expected) =>
        string.Equals(actual.Name, expected.Name, StringComparison.Ordinal) &&
        string.Equals(
            NormalizeDuckDbType(actual.Type),
            NormalizeDuckDbType(expected.SqlType),
            StringComparison.Ordinal) &&
        actual.Nullable != expected.Required &&
        DefaultsMatch(actual.Default, expected.DefaultSql);

    private static bool IndexesMatch(
        DuckDBConnection con,
        DbTransaction transaction,
        DuckDbExpectedTable table,
        bool allowMissingNonUniqueIndexes = false)
    {
        var actualIndexes = new Dictionary<
            string,
            (string Table, bool Unique, string[] Columns)>(StringComparer.Ordinal);
        using (var command = con.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                                  SELECT index_name, table_name, is_unique, expressions
                                  FROM duckdb_indexes()
                                  WHERE schema_name = 'main' AND table_name = $1
                                  """;
            AddParameters(command, table.Name);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!actualIndexes.TryAdd(
                        reader.GetString(0),
                        (reader.GetString(1),
                            reader.GetBoolean(2),
                            ParseDuckDbIndexExpressions(reader.GetString(3)))))
                {
                    return false;
                }
            }
        }

        if (actualIndexes.Count > table.Indexes.Length ||
            !allowMissingNonUniqueIndexes && actualIndexes.Count != table.Indexes.Length ||
            actualIndexes.Keys.Any(name => table.Indexes.All(expected => expected.Name != name))) return false;

        foreach (var expected in table.Indexes)
        {
            if (!actualIndexes.TryGetValue(expected.Name, out var actual))
            {
                // Declared nonunique indexes contain no authoritative facts. Missing ones
                // are recreated by the normal generated DDL before exposing the store.
                if (allowMissingNonUniqueIndexes && !expected.Unique) continue;
                return false;
            }
            if (!string.Equals(actual.Table, table.Name, StringComparison.Ordinal) ||
                actual.Unique != expected.Unique ||
                !actual.Columns.SequenceEqual(expected.Columns, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string[] ParseDuckDbIndexExpressions(string expressions)
    {
        var value = expressions.Trim();
        if (value is ['[', _, ..] && value[^1] == ']')
            value = value[1..^1];
        if (value.Length is 0)
            return [];

        // DuckDB lists a keyword column (e.g. saved_queries.name) as a string literal of the
        // quoted identifier, '"name"', so both quote layers come off.
        return value.Split(',')
            .Select(static column => column.Trim().Trim('\'').Trim('"'))
            .ToArray();
    }

    private static string NormalizeDuckDbType(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.StartsWith("VARCHAR", StringComparison.Ordinal))
            return "VARCHAR";
        return normalized switch
        {
            "TIMESTAMP WITH TIME ZONE" => "TIMESTAMPTZ",
            "TIMESTAMP WITHOUT TIME ZONE" => "TIMESTAMP",
            _ => normalized
        };
    }

    internal static bool DefaultsMatch(string? actual, string? expected)
    {
        if (actual is null || expected is null)
            return actual is null && expected is null;

        // The rewrite runs before the parentheses are trimmed: trimming first cut the closing
        // parenthesis off CAST(...) so the rewrite could never match.
        static string Normalize(string value) => string.Concat(
            value.Where(static character => !char.IsWhiteSpace(character)))
            .ToUpperInvariant()
            .Replace("\"", "", StringComparison.Ordinal)
            // DuckDB reports a uuid()::varchar default as CAST(uuid() AS VARCHAR).
            .Replace("CAST(UUID()ASVARCHAR)", "UUID()::VARCHAR", StringComparison.Ordinal)
            .Trim('(', ')');

        return string.Equals(Normalize(actual), Normalize(expected), StringComparison.Ordinal);
    }

    private static bool HasRows(
        DuckDBConnection con,
        DbTransaction transaction,
        IReadOnlyList<string> tables)
    {
        foreach (var table in tables)
        {
            if (!TableExists(con, transaction, table))
                continue;

            using var command = con.CreateCommand();
            command.Transaction = transaction;
            // CA2100: the table name is one of the generated schema's tables, quoted by QuoteIdentifier.
            command.CommandText = string.Concat(
                "SELECT EXISTS (SELECT 1 FROM ",
                QuoteIdentifier(table),
                " LIMIT 1)");
            if (command.ExecuteScalar() is true)
                return true;
        }

        return false;
    }

    private static bool TableExists(
        DuckDBConnection con,
        DbTransaction transaction,
        string table)
    {
        using var command = con.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
                              SELECT count(*)
                              FROM duckdb_tables()
                              WHERE schema_name = 'main' AND table_name = $1
                              """;
        AddParameters(command, table);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) is 1;
    }

    private static void CreateSchema(
        DuckDBConnection con,
        DbTransaction transaction,
        IEnumerable<string> statements)
    {
        foreach (var statement in statements)
            ExecuteSchemaSql(con, transaction, statement);
    }

    private static void DropGeneratedTables(
        DuckDBConnection con,
        DbTransaction transaction,
        bool includeAuthoritative)
    {
        DropTables(con, transaction, DuckDbGeneratedSchema.DerivedTableNames);

        if (!includeAuthoritative)
            return;

        DropTables(con, transaction, DuckDbGeneratedSchema.AuthoritativeTableNames);
        ExecuteSchemaSql(con, transaction, "DROP SEQUENCE IF EXISTS logs_ingest_sequence");
    }

    private static void DropTables(
        DuckDBConnection con,
        DbTransaction transaction,
        IEnumerable<string> tables)
    {
        foreach (var table in tables)
            ExecuteSchemaSql(con, transaction, $"DROP TABLE IF EXISTS {QuoteIdentifier(table)}");
    }

    private static void DropExpectedIndexes(
        DuckDBConnection con,
        DbTransaction transaction,
        IEnumerable<DuckDbExpectedTable> tables)
    {
        foreach (var index in tables.SelectMany(static table => table.Indexes))
            ExecuteSchemaSql(con, transaction, $"DROP INDEX IF EXISTS {QuoteIdentifier(index.Name)}");
    }

    private static void DropRetiredTables(
        DuckDBConnection con,
        DbTransaction transaction)
    {
        foreach (var table in DuckDbGeneratedSchema.RetiredTableNames)
            ExecuteSchemaSql(con, transaction, $"DROP TABLE IF EXISTS {QuoteIdentifier(table)}");
    }

    private static void ExecuteSchemaSql(
        DuckDBConnection con,
        DbTransaction transaction,
        string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return;
        using var command = con.CreateCommand();
        command.Transaction = transaction;
        // CA2100: sql is the generated schema's DDL (DuckDbGeneratedSchema); nothing from a request reaches it.
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void AddParameters(DuckDBCommand command, params object[] values)
    {
        foreach (var value in values)
            command.Parameters.Add(new DuckDBParameter { Value = value });
    }

    private static string QuoteIdentifier(string value) =>
        $"\"{value.Replace("\"", "\"\"")}\"";

    private sealed record SchemaIdentity(string AuthoritativeHash, string DerivedHash);

    private static void VerifyPersistedPrimaryKeys(DuckDBConnection con)
    {
        foreach (var table in DuckDbGeneratedSchema.AuthoritativeTables
                     .Concat(DuckDbGeneratedSchema.DerivedTables))
        {
            var columns = table.Columns
                .Where(static column => column.PrimaryKeyOrdinal >= 0)
                .OrderBy(static column => column.PrimaryKeyOrdinal)
                .Select(static column => column.Name);
            VerifyPersistedPrimaryKey(con, table.Name, string.Join(',', columns));
        }
    }

    private static void VerifyPersistedPrimaryKey(DuckDBConnection con, string table, string expectedCsv)
    {
        if (expectedCsv.Length is 0)
            return;

        var expected = expectedCsv.Split(',').ToHashSet(StringComparer.Ordinal);
        var actual = new HashSet<string>(StringComparer.Ordinal);
        using var cmd = con.CreateCommand();
        cmd.CommandText = """
                          SELECT unnest(constraint_column_names)
                          FROM duckdb_constraints()
                          WHERE table_name = $1 AND constraint_type = 'PRIMARY KEY'
                          """;
        cmd.Parameters.Add(new DuckDBParameter { Value = table });
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            actual.Add(reader.GetString(0));

        if (!expected.SetEquals(actual))
        {
            throw new InvalidOperationException(
                $"Persisted table '{table}' has primary key ({string.Join(", ", actual.Order(StringComparer.Ordinal))}); " +
                $"this build requires ({string.Join(", ", expected.Order(StringComparer.Ordinal))}).");
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Read(ref _disposed) is not 0, this);

    private abstract class WriteJob
    {
        public abstract bool TryClaim();

        public abstract ValueTask ExecuteAsync(DuckDBConnection con, CancellationToken ct);

        public virtual void OnAborted(Exception error)
        {
        }
    }

    private sealed class WriteJob<TResult>(
        Func<DuckDBConnection, CancellationToken, ValueTask<TResult>> action,
        CancellationToken cancellationToken)
        : WriteJob
    {
        private readonly TaskCompletionSource<TResult> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenRegistration _cancellationRegistration;
        private int _state;

        public Task<TResult> Task => _tcs.Task;

        public void ArmCancellation()
        {
            _cancellationRegistration = cancellationToken.Register(
                static state => ((WriteJob<TResult>)state!).CancelBeforeClaim(),
                this);
            if (Read(ref _state) is not 0)
                _cancellationRegistration.Dispose();
        }

        public override bool TryClaim()
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) is not 0)
            {
                _cancellationRegistration.Dispose();
                return false;
            }
            _cancellationRegistration.Dispose();
            return true;
        }

        public override async ValueTask ExecuteAsync(DuckDBConnection con, CancellationToken ct)
        {
            try
            {
                var result = await action(con, ct).ConfigureAwait(false);
                _tcs.TrySetResult(result);
            }
            catch (OperationCanceledException oce)
            {
                _tcs.TrySetCanceled(oce.CancellationToken);
            }
            catch (Exception ex)
            {
                _tcs.TrySetException(ex);
            }
            finally
            {
                await _cancellationRegistration.DisposeAsync().ConfigureAwait(false);
            }
        }

        public override void OnAborted(Exception error)
        {
            _cancellationRegistration.Dispose();
            if (error is OperationCanceledException oce)
                _tcs.TrySetCanceled(oce.CancellationToken);
            else
                _tcs.TrySetException(error);
        }

        private void CancelBeforeClaim()
        {
            if (Interlocked.CompareExchange(ref _state, 2, 0) is 0)
                _tcs.TrySetCanceled(cancellationToken);
        }
    }

    private interface IReadJob
    {
        ValueTask ExecuteAsync(DuckDBConnection con);
        void Cancel();
        void Abort(Exception error);
    }

    private sealed class ReadJob<TResult>(Func<DuckDBConnection, TResult> read, CancellationToken ct) : IReadJob
    {
        private readonly TaskCompletionSource<TResult> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TResult> Task => _tcs.Task;

        // Runs on a dedicated reader thread. RunContinuationsAsynchronously guarantees the awaiting
        // caller's continuation resumes on the thread pool, never inline on this reader thread.
        public ValueTask ExecuteAsync(DuckDBConnection con)
        {
            if (ct.IsCancellationRequested)
            {
                _tcs.TrySetCanceled(ct);
                return ValueTask.CompletedTask;
            }

            try
            {
                _tcs.TrySetResult(read(con));
            }
            catch (OperationCanceledException oce)
            {
                _tcs.TrySetCanceled(oce.CancellationToken);
            }
            catch (Exception ex)
            {
                _tcs.TrySetException(ex);
            }
            return ValueTask.CompletedTask;
        }

        public void Cancel() => _tcs.TrySetCanceled();

        public void Abort(Exception error)
        {
            if (error is OperationCanceledException oce)
                _tcs.TrySetCanceled(oce.CancellationToken);
            else
                _tcs.TrySetException(error);
        }
    }

    private struct QueryBuilder()
    {
        private readonly List<string> _conditions = [];
        private readonly List<DuckDBParameter> _parameters = [];
        private int _paramIndex = 1;

        public void Add(string condition, object value)
        {
            _conditions.Add(condition.Replace("$N", $"${_paramIndex++}"));
            _parameters.Add(new DuckDBParameter { Value = value });
        }

        // A keyset predicate binds two values: $A is the sort key, $B the tie-breaker.
        public void Add(string condition, object first, object second)
        {
            var a = $"${_paramIndex++}";
            var b = $"${_paramIndex++}";
            _conditions.Add(condition.Replace("$A", a).Replace("$B", b));
            _parameters.Add(new DuckDBParameter { Value = first });
            _parameters.Add(new DuckDBParameter { Value = second });
        }

        public readonly string WhereClause =>
            _conditions.Count > 0 ? $"WHERE {string.Join(" AND ", _conditions)}" : "";

        public readonly string NextParam => $"${_paramIndex}";

        public readonly void ApplyTo(DuckDBCommand cmd) => cmd.Parameters.AddRange(_parameters);
    }

}
