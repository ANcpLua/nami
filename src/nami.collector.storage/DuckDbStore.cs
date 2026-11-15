using DuckDB.NET.Data;
using Microsoft.Extensions.Logging.Abstractions;
using static System.Threading.Volatile;

namespace Nami.Collector.Storage;

internal sealed partial class DuckDbStore : INamiStore
{
    private const int MaxSpansPerBatch = 100;

    private const int MaxLogsPerBatch = 150;

    private static readonly TimeSpan s_shutdownTimeout = TimeSpan.FromSeconds(5);
    private readonly Func<CancellationToken, ValueTask>? _beforeWrite;

    private readonly CancellationTokenSource _cts;
    private readonly string _databasePath;
    private readonly bool _isInMemory;
    private readonly Channel<WriteJob> _jobs;
    private readonly ILogger<DuckDbStore> _logger;
    private readonly DuckDBConnection[] _readerConnections;
    private readonly Task[] _readerTasks;
    private readonly Channel<IReadJob>? _reads;
    private readonly Lock _shutdownLock = new();
    private readonly TimeSpan _snapshotLifetime;
    private readonly TimeProvider _timeProvider;
    private readonly Task _writerTask;
    private Task<List<Exception>?>? _shutdownTask;
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
        : this(static value => new DuckDBConnection(value), databasePath, jobQueueCapacity, maxConcurrentReads,
            readQueueCapacity, memoryLimit, threads, tempDirectory, beforeWrite, loggerFactory, timeProvider,
            snapshotLifetime)
    {
    }

    internal static DuckDbStore CreateForTesting(
        Func<string, DuckDBConnection> connectionFactory,
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
        TimeSpan? snapshotLifetime = null) =>
        new(connectionFactory, databasePath, jobQueueCapacity, maxConcurrentReads, readQueueCapacity,
            memoryLimit, threads, tempDirectory, beforeWrite, loggerFactory, timeProvider, snapshotLifetime);

    private DuckDbStore(
        Func<string, DuckDBConnection> connectionFactory,
        string databasePath,
        int jobQueueCapacity,
        int maxConcurrentReads,
        int readQueueCapacity,
        string? memoryLimit,
        int? threads,
        string? tempDirectory,
        Func<CancellationToken, ValueTask>? beforeWrite,
        ILoggerFactory? loggerFactory,
        TimeProvider? timeProvider,
        TimeSpan? snapshotLifetime)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(jobQueueCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrentReads);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(readQueueCapacity);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _snapshotLifetime = snapshotLifetime ?? TimeSpan.FromMinutes(30);
        var resolvedLoggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = resolvedLoggerFactory.CreateLogger<DuckDbStore>();
        _isInMemory = databasePath == ":memory:";
        _databasePath = _isInMemory ? databasePath : Path.GetFullPath(databasePath);
        var connectionString = $"DataSource={_databasePath};vacuum_rebuild_indexes={ulong.MaxValue}";
        _beforeWrite = beforeWrite;
        _jobs = Channel.CreateBounded<WriteJob>(new BoundedChannelOptions(jobQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        // DuckDB.NET's *Async methods are synchronous-over-async: the embedded engine has no IO to
        // await, so every read blocks its calling thread for the full query. To keep that blocking
        // off the shared thread pool (Kestrel), reads run on a dedicated set of OS threads, each with
        // its own connection. DuckDB.NET caches one native database instance per file path, so every
        // connection to that file shares the instance and reads are MVCC-concurrent with the writer.
        // In-memory mode has no shared on-disk instance, so its reads are serialized through the
        // single writer connection instead.
        if (_isInMemory)
        {
            _readerConnections = [];
            _readerTasks = [];
            _reads = null;
        }
        else
        {
            _reads = Channel.CreateBounded<IReadJob>(new BoundedChannelOptions(readQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            });

            _readerConnections = new DuckDBConnection[maxConcurrentReads];
            _readerTasks = new Task[maxConcurrentReads];
        }

        _cts = new CancellationTokenSource();
        var acquiredConnections = new List<DuckDBConnection>();
        var startedWorkers = new List<Task>();
        try
        {
            Connection = OpenConnection();
            ConfigureDatabase(Connection, memoryLimit, threads, tempDirectory);
            var resetDerived = InitializeSchema(Connection);
            if (resetDerived || MetricProjectionsNeedRebuild(Connection))
                RebuildMetricProjections(Connection);

            // Reader connections use the same connection string as the writer on purpose: DuckDB.NET
            // keys its native-instance cache by file path alone, so an ACCESS_MODE=READ_ONLY token
            // here would be silently ignored on reuse (the writer already opened the instance
            // read-write). Sharing that one instance is exactly what makes these reads MVCC-concurrent.
            // Open every reader connection up front (on this thread) so a failure surfaces loudly and
            // immediately as a startup error, not asynchronously on a background thread.
            for (var i = 0; i < _readerConnections.Length; i++)
                _readerConnections[i] = OpenConnection();

            // No worker may outlive a failed connection initialization.
            _writerTask = Task.Run(WriterLoopAsync, CancellationToken.None);
            startedWorkers.Add(_writerTask);
            for (var i = 0; i < _readerTasks.Length; i++)
            {
                _readerTasks[i] = Task.Factory.StartNew(
                        static state =>
                        {
                            var owner = ((DuckDbStore Store, DuckDBConnection Connection))state!;
                            owner.Store.ReaderLoop(owner.Connection);
                        },
                        (Store: this, Connection: _readerConnections[i]),
                        CancellationToken.None,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default);
                startedWorkers.Add(_readerTasks[i]);
            }
        }
        catch (Exception error)
        {
            // This also covers a worker-start failure after all connections opened.
            var errors = new List<Exception> { error };
            _jobs.Writer.TryComplete();
            _reads?.Writer.TryComplete();
            try { _cts.Cancel(); }
            catch (Exception cancellationError) { errors.Add(cancellationError); }
            foreach (var worker in startedWorkers)
            {
                try { worker.GetAwaiter().GetResult(); }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    // Startup rollback intentionally cancels every already-started worker.
                }
                catch (Exception workerError) { errors.Add(workerError); }
            }
            foreach (var connection in acquiredConnections)
            {
                try { connection.Dispose(); }
                catch (Exception disposalError) { errors.Add(disposalError); }
            }
            _cts.Dispose();
            if (errors.Count > 1)
                throw new AggregateException("DuckDB store initialization and cleanup failed.", errors);
            throw;
        }

        DuckDBConnection OpenConnection()
        {
            var connection = connectionFactory(connectionString);
            acquiredConnections.Add(connection);
            connection.Open();
            return connection;
        }
    }


    private DuckDBConnection Connection { get; }

    public async ValueTask DisposeAsync()
    {
        Task<List<Exception>?> shutdown;
        lock (_shutdownLock)
        {
            Write(ref _disposed, 1);
            shutdown = _shutdownTask ??= ShutdownAsync();
        }

        List<Exception>? shutdownErrors;
        try
        {
            shutdownErrors = await shutdown.WaitAsync(s_shutdownTimeout, _timeProvider).ConfigureAwait(false);
        }
        catch (TimeoutException error)
        {
            // The retained task still owns every connection and observes/logs cleanup errors.
            // A later DisposeAsync joins that same task instead of disposing underneath it.
            var timeout = new TimeoutException("DuckDB workers did not stop before shutdown timeout.", error);
            DuckDbStoreLog.ShutdownFailed(_logger, timeout);
            throw new AggregateException("DuckDB store did not shut down cleanly.", timeout);
        }

        if (shutdownErrors is { Count: > 0 })
            throw new AggregateException("DuckDB store did not shut down cleanly.", shutdownErrors);
    }

    private async Task<List<Exception>?> ShutdownAsync()
    {
        // Always dispatch cleanup to the thread pool before touching native resources. The
        // retained task owns cleanup even when DisposeAsync stops waiting after its deadline.
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        List<Exception>? shutdownErrors = null;

        _jobs.Writer.TryComplete();
        _reads?.Writer.TryComplete();
        try
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AddShutdownError(ref shutdownErrors, ex);
        }

        var workers = new[] { _writerTask }.Concat(_readerTasks).ToArray();
        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch
        {
            foreach (var worker in workers)
                if (worker.Exception is { } failure)
                    foreach (var error in failure.InnerExceptions)
                        AddShutdownError(ref shutdownErrors, error);
        }

        if (_reads is not null)
        {
            while (_reads.Reader.TryRead(out var leftover))
                leftover.Cancel();
        }

        // Flush the WAL into the main database file and truncate it so the next start is a clean,
        // fast open instead of a WAL replay. Pointless for an in-memory database.
        if (!_isInMemory)
        {
            try
            {
                await using var checkpoint = Connection.CreateCommand();
                checkpoint.CommandText = "CHECKPOINT";
                checkpoint.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                // A failed final checkpoint is not data loss (the WAL is replayed on next open), but
                // it must surface and must not abort the rest of teardown — route it into the
                // shutdown AggregateException instead of throwing past Connection.Dispose().
                AddShutdownError(
                    ref shutdownErrors,
                    new InvalidOperationException("DuckDB shutdown CHECKPOINT failed.", ex));
            }
        }

        foreach (var connection in _readerConnections.Append(Connection))
        {
            try { connection.Dispose(); }
            catch (Exception error) { AddShutdownError(ref shutdownErrors, error); }
        }
        _cts.Dispose();
        return shutdownErrors;
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
                    SpanCount = DuckDbValueReader.Read<long>(reader, 0, 0),
                    SessionCount = DuckDbValueReader.Read<long>(reader, 1, 0)
                };
            }

            return new StorageStats();
        }, ct);
    }

    public async Task InsertLogsAsync(IReadOnlyList<LogStorageRow> logs, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (logs.Count is 0)
            return;

        var knownTimes = logs.Any(static row => row.TimeUnixNano == 0 || row.ObservedTimeUnixNano == 0)
            ? logs.Select(static row => row with
            {
                TimeUnixNano = row.TimeUnixNano is > 0 ? row.TimeUnixNano : null,
                ObservedTimeUnixNano = row.ObservedTimeUnixNano is > 0 ? row.ObservedTimeUnixNano : null
            }).ToArray()
            : logs;
        await ExecuteWriteAsync(async (con, token) =>
        {
            await using var tx = await con.BeginTransactionAsync(token).ConfigureAwait(false);
            await InsertRowsBatchedAsync(con, tx, knownTimes, LogStorageRow.AddParameters,
                LogStorageRow.BuildMultiRowInsertSql, MaxLogsPerBatch, token);
            RecordIngestion(con, tx, "logs",
                knownTimes.Select(static row => new ArrivingResource(row.ProjectId, row.ServiceName, row.ResourceJson,
                    row.AttributesJson, row.ScopeAttributesJson, row.AttributeRenames)), token);
            await tx.CommitAsync(token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }


    private async Task<T> ExecuteReadAsync<T>(Func<DuckDBConnection, T> read, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        // In-memory mode shares the single writer connection (not safe for concurrent use), so its
        // reads are serialized through the writer queue rather than the dedicated reader pool.
        if (_reads is null)
            return await ExecuteWriteAsync((con, _) => new ValueTask<T>(read(con)),
                cancelAfterClaim: true, ct).ConfigureAwait(false);

        var job = new ReadJob<T>(read, ct);
        await _reads.Writer.WriteAsync(job, ct).ConfigureAwait(false);
        return await job.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    private async Task<T> ExecuteWriteAsync<T>(Func<DuckDBConnection, CancellationToken, ValueTask<T>> operation,
        bool cancelAfterClaim = false, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        var job = new WriteJob<T>(operation, cancelAfterClaim, ct);
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
        }, cancelAfterClaim: false, ct);
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
        var job = new WriteJob<T>(operation, cancelAfterClaim: true, ct);
        await _jobs.Writer.WriteAsync(job, ct).ConfigureAwait(false);
        job.ArmCancellation();
        return await job.Task.ConfigureAwait(false);
    }


    private void AddShutdownError(ref List<Exception>? errors, Exception error)
    {
        errors ??= [];
        errors.Add(error);
        DuckDbStoreLog.ShutdownFailed(_logger, error);
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
                    await job.ExecuteAsync(Connection, _beforeWrite, _cts.Token).ConfigureAwait(false);
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

    // The channel wait and query both stay on this dedicated thread. Shutdown joins
    // every worker before disposing its connection.
    private void ReaderLoop(DuckDBConnection con)
    {
        var reader = _reads!.Reader;
        try
        {
            while (reader.WaitToReadAsync(_cts.Token).AsTask().GetAwaiter().GetResult())
            {
                while (reader.TryRead(out var job))
                {
                    try
                    {
                        job.Execute(con, _cts.Token);
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
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            // Store disposal cancels idle readers and active native operations.
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
        RecordIngestion(con, tx, "traces",
            batch.Spans.Select(static row => new ArrivingResource(row.ProjectId, row.ServiceName, row.ResourceJson,
                row.AttributesJson, row.ScopeAttributesJson, row.AttributeRenames)), ct);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    private static void ConfigureDatabase(DuckDBConnection con, string? memoryLimit, int? threads,
        string? tempDirectory)
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
    private static string EscapeSqlLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static bool InitializeSchema(DuckDBConnection con)
    {
        using var transaction = con.BeginTransaction();
        var bootstrapMismatches = FindSchemaMismatches(con, transaction, DuckDbGeneratedSchema.BootstrapTables);
        if (bootstrapMismatches.Count > 0)
        {
            if (HasRows(con, transaction, DuckDbGeneratedSchema.BootstrapTableNames) ||
                HasRows(con, transaction, DuckDbGeneratedSchema.AuthoritativeTableNames))
                throw new NamiSchemaMismatchException("Persisted data has incompatible or missing schema metadata. Use a new database.");
            DropTables(con, transaction, DuckDbGeneratedSchema.BootstrapTableNames);
        }
        CreateSchema(con, transaction, DuckDbGeneratedSchema.BootstrapDdl);

        var stored = ReadSchemaIdentity(con, transaction);
        if (stored is null && HasRows(con, transaction, DuckDbGeneratedSchema.AuthoritativeTableNames))
            throw new NamiSchemaMismatchException("Persisted data has no current schema identity. Use a new database.");
        IReadOnlyList<string> authoritativeMismatches = FindSchemaMismatches(
            con,
            transaction,
            DuckDbGeneratedSchema.AuthoritativeTables, true);
        if (stored is { AuthoritativeSchemaHash: not DuckDbGeneratedSchema.AuthoritativeHash })
            authoritativeMismatches = DuckDbGeneratedSchema.AuthoritativeTableNames;

        var derivedMismatches = FindSchemaMismatches(
            con,
            transaction,
            DuckDbGeneratedSchema.DerivedTables);
        var resetDerived = authoritativeMismatches.Count > 0 || derivedMismatches.Count > 0 ||
                           stored is { DerivedSchemaHash: not DuckDbGeneratedSchema.DerivedHash };

        if (authoritativeMismatches.Count > 0)
        {
            if (HasRows(
                    con,
                    transaction,
                    DuckDbGeneratedSchema.AuthoritativeTableNames))
            {
                var difference = stored is { AuthoritativeSchemaHash: not DuckDbGeneratedSchema.AuthoritativeHash }
                    ? $"stored authoritative DDL hash {stored.AuthoritativeSchemaHash} does not match " +
                      DuckDbGeneratedSchema.AuthoritativeHash
                    : $"incompatible tables: {string.Join(", ", authoritativeMismatches)}";
                throw new NamiSchemaMismatchException(
                    "The authoritative DuckDB schema differs from this build while persisted data exists, " +
                    "and this private build does not migrate earlier schemas. Use a new database; nami will not rewrite or delete " +
                    $"that data automatically. Difference: {difference}");
            }

            // No authoritative rows exist: recreate the whole owned schema and its sequences.
            DropExpectedIndexes(con, transaction, DuckDbGeneratedSchema.AuthoritativeTables);
            DropTables(con, transaction, DuckDbGeneratedSchema.AuthoritativeTableNames);
            CreateSchema(con, transaction, DuckDbGeneratedSchema.DropAuthoritativeSequencesDdl);
        }
        CreateSchema(con, transaction, DuckDbGeneratedSchema.AuthoritativeDdl);
        if (resetDerived)
        {
            DropExpectedIndexes(con, transaction, DuckDbGeneratedSchema.DerivedTables);
            DropTables(con, transaction, DuckDbGeneratedSchema.DerivedTableNames);
        }

        CreateSchema(con, transaction, DuckDbGeneratedSchema.DerivedDdl);
        WriteSchemaIdentity(con, transaction);
        transaction.Commit();

        VerifyPersistedPrimaryKeys(con);
        return resetDerived;
    }

    private static SchemaMetadataRow? ReadSchemaIdentity(DuckDBConnection con, DbTransaction transaction)
    {
        using var command = con.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT " + SchemaMetadataRow.SelectColumnList + " FROM " + SchemaMetadataRow.TableName +
                              " WHERE singleton = 0";
        using var reader = command.ExecuteReader();
        return reader.Read() ? SchemaMetadataRow.MapFromReader(reader) : null;
    }

    private static void WriteSchemaIdentity(DuckDBConnection con, DbTransaction transaction)
    {
        using var command = con.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = SchemaMetadataRow.InsertSql;
        SchemaMetadataRow.AddParameters(command, new SchemaMetadataRow
        {
            AuthoritativeSchemaHash = DuckDbGeneratedSchema.AuthoritativeHash,
            DerivedSchemaHash = DuckDbGeneratedSchema.DerivedHash
        });
        command.ExecuteNonQuery();
    }

    private static List<string> FindSchemaMismatches(
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
            (!allowMissingNonUniqueIndexes && actualIndexes.Count != table.Indexes.Length) ||
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
        return [.. value.Split(',')
            .Select(static column => column.Trim().Trim('\'').Trim('"'))];
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

    private static bool DefaultsMatch(string? actual, string? expected)
    {
        if (actual is null || expected is null)
            return actual is null && expected is null;

        return string.Equals(Normalize(actual), Normalize(expected), StringComparison.Ordinal);

        // The rewrite runs before the parentheses are trimmed: trimming first cut the closing
        // parenthesis off CAST(...) so the rewrite could never match.
        static string Normalize(string value) => string.Concat(
                value.Where(static character => !char.IsWhiteSpace(character)))
            .ToUpperInvariant()
            .Replace("\"", "", StringComparison.Ordinal)
            // DuckDB reports a uuid()::varchar default as CAST(uuid() AS VARCHAR).
            .Replace("CAST(UUID()ASVARCHAR)", "UUID()::VARCHAR", StringComparison.Ordinal)
            .Trim('(', ')');
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
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

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

    private sealed record ActualColumn(string Name, string Type, bool Nullable, string? Default);


    private abstract class WriteJob
    {
        public abstract bool TryClaim();

        public abstract ValueTask ExecuteAsync(DuckDBConnection con, Func<CancellationToken, ValueTask>? beforeWrite,
            CancellationToken ct);

        public virtual void OnAborted(Exception error)
        {
        }
    }

    private sealed class WriteJob<TResult>(
        Func<DuckDBConnection, CancellationToken, ValueTask<TResult>> action,
        bool cancelAfterClaim,
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

        public override async ValueTask ExecuteAsync(DuckDBConnection con, Func<CancellationToken, ValueTask>? beforeWrite,
            CancellationToken ct)
        {
            // Accepted ingestion finishes independently of the caller; reads and maintenance
            // retain request cancellation while they own the writer connection.
            using var linkedCancellation = cancelAfterClaim
                ? CancellationTokenSource.CreateLinkedTokenSource(ct, cancellationToken)
                : null;
            var token = linkedCancellation?.Token ?? ct;
            try
            {
                await using var interrupt = token.Register(con.NativeConnection.Interrupt);
                token.ThrowIfCancellationRequested();
                if (beforeWrite is not null)
                    await beforeWrite(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var result = await action(con, token).ConfigureAwait(false);
                _tcs.TrySetResult(result);
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                _tcs.TrySetCanceled(token);
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
        void Execute(DuckDBConnection con, CancellationToken shutdownToken);
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
        public void Execute(DuckDBConnection con, CancellationToken shutdownToken)
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, shutdownToken);
            var token = linkedCancellation.Token;
            try
            {
                using var interrupt = token.Register(con.NativeConnection.Interrupt);
                token.ThrowIfCancellationRequested();
                var result = read(con);
                token.ThrowIfCancellationRequested();
                _tcs.TrySetResult(result);
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                _tcs.TrySetCanceled(token);
            }
            catch (OperationCanceledException oce)
            {
                _tcs.TrySetCanceled(oce.CancellationToken);
            }
            catch (Exception ex)
            {
                _tcs.TrySetException(ex);
            }
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
}
