using System.Data;
using System.Data.Common;
using System.Globalization;
using DuckDB.NET.Data;
using Microsoft.Extensions.Time.Testing;
using Nami.Collector.Storage;
using Nami.Tests.Infrastructure;
using ThreadState = System.Threading.ThreadState;

namespace Nami.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
public sealed class StorageLifetimeTests(TestContext context)
{
    [TestMethod]
    [DataRow(0, 1, 1, "jobQueueCapacity", DisplayName = "Invalid writer capacity")]
    [DataRow(1, 0, 1, "maxConcurrentReads", DisplayName = "Invalid reader count")]
    [DataRow(1, 1, 0, "readQueueCapacity", DisplayName = "Invalid reader capacity")]
    public void Open_InvalidCapacity_DoesNotAcquireConnections(
        int writes, int readers, int reads, string parameter)
    {
        var connections = new Connections();

        var failure = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            DuckDbStore.CreateForTesting(connections.Create, DatabasePath("invalid.duckdb"), writes, readers, reads));

        Assert.AreEqual(parameter, failure.ParamName);
        Assert.IsEmpty(connections.Items);
    }

    [TestMethod]
    public async Task Open_SecondReaderFails_DisposesEveryAcquiredConnection()
    {
        var path = DatabasePath("failed-open.duckdb");
        var expected = new InvalidOperationException("Injected reader-open failure.");
        var connections = new Connections { FailOpenAt = 2, OpenFailure = expected };

        var failure = Assert.ThrowsExactly<InvalidOperationException>(() =>
            DuckDbStore.CreateForTesting(connections.Create, path, maxConcurrentReads: 3));

        Assert.AreSame(expected, failure);
        Assert.HasCount(3, connections.Items);
        foreach (var connection in connections.Items)
        {
            Assert.AreEqual(1, connection.DisposeCalls);
            Assert.AreEqual(ConnectionState.Closed, connection.State);
        }

        await using var reopened = new DuckDbStore(path);
        Assert.AreEqual(0L, (await reopened.GetStorageStatsAsync("default", context.CancellationToken)).SpanCount);
    }

    [TestMethod]
    public async Task Reads_AfterIdle_StayOnTheSameDedicatedThread()
    {
        var connections = new Connections();
        await using var store = DuckDbStore.CreateForTesting(connections.Create,
            DatabasePath("reader-thread.duckdb"), maxConcurrentReads: 1);
        var reader = connections.Items[1];
        Thread? originalThread = null;

        for (var cycle = 0; cycle < 3; cycle++)
        {
            var stats = await store.GetStorageStatsAsync("default", context.CancellationToken);
            Assert.AreEqual(0L, stats.SpanCount);
            var thread = reader.LastReadThread;
            Assert.IsNotNull(thread);
            Assert.IsFalse(thread.IsThreadPoolThread);
            originalThread ??= thread;
            Assert.AreSame(originalThread, thread);

            // Observe the worker returning to its empty-channel wait before submitting again.
            // The old async loop instead left this dedicated thread in the Stopped state.
            Assert.IsTrue(SpinWait.SpinUntil(() =>
                    (thread.ThreadState & (ThreadState.WaitSleepJoin | ThreadState.Stopped)) != 0,
                TimeSpan.FromSeconds(10)), "Reader did not reach an idle state.");
            Assert.AreNotEqual(ThreadState.Stopped, thread.ThreadState & ThreadState.Stopped);
        }
    }

    [TestMethod]
    public async Task Reads_CanceledWhileQueueIsFull_DoNotExecuteOrShareTheConnection()
    {
        var connections = new Connections();
        using var gate = new ReadGate(context.CancellationToken);
        await using var store = DuckDbStore.CreateForTesting(connections.Create,
            DatabasePath("reader-queue.duckdb"), maxConcurrentReads: 1, readQueueCapacity: 1);
        connections.Items[1].BeforeRead = gate.BlockFirstRead;

        var first = store.GetStorageStatsAsync("default", context.CancellationToken);
        try
        {
            await gate.Entered.WaitAsync(context.CancellationToken);
            var second = store.GetStorageStatsAsync("default", context.CancellationToken);
            using var cancellation = new CancellationTokenSource();
            var rejected = store.GetStorageStatsAsync("default", cancellation.Token);
            Assert.IsFalse(first.IsCompleted);
            Assert.IsFalse(second.IsCompleted);
            await cancellation.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => rejected);
            Assert.AreEqual(1, gate.Executions);

            gate.Release();
            Assert.AreEqual(0L, (await first).SpanCount);
            Assert.AreEqual(0L, (await second).SpanCount);
            Assert.AreEqual(2, gate.Executions);
        }
        finally
        {
            gate.Release();
        }
    }

    [TestMethod]
    public async Task Dispose_ActiveWorkersOutliveTimeout_RetainsConnectionsUntilBothStop()
    {
        var clock = new FakeTimeProvider(TestData.s_now);
        var connections = new Connections();
        var writeGate = new WriteGate();
        using var readGate = new ReadGate(context.CancellationToken);
        var store = DuckDbStore.CreateForTesting(connections.Create, DatabasePath("shutdown.duckdb"),
            maxConcurrentReads: 1, beforeWrite: writeGate.WaitAsync, timeProvider: clock);
        connections.Items[1].BeforeRead = readGate.BlockRead;

        var write = SeedTrace(store, context.CancellationToken);
        Task<StorageStats> read;
        try
        {
            await writeGate.Entered.Task.WaitAsync(context.CancellationToken);
            read = store.GetStorageStatsAsync("default", context.CancellationToken);
            await readGate.Entered.WaitAsync(context.CancellationToken);
            var firstDisposal = store.DisposeAsync().AsTask();
            var concurrentDisposal = store.DisposeAsync().AsTask();
            await writeGate.Canceled.Task.WaitAsync(context.CancellationToken);
            clock.Advance(TimeSpan.FromMilliseconds(4999));
            Assert.IsFalse(firstDisposal.IsCompleted);
            Assert.IsFalse(concurrentDisposal.IsCompleted);
            clock.Advance(TimeSpan.FromMilliseconds(1));

            var failure = await Assert.ThrowsExactlyAsync<AggregateException>(() => firstDisposal);
            Assert.IsInstanceOfType<TimeoutException>(Assert.ContainsSingle(failure.InnerExceptions));
            await Assert.ThrowsExactlyAsync<AggregateException>(() => concurrentDisposal);
            foreach (var connection in connections.Items)
            {
                Assert.AreEqual(ConnectionState.Open, connection.State);
                Assert.AreEqual(0, connection.DisposeCalls);
                Assert.AreEqual(0, connection.CheckpointCalls);
            }
        }
        finally
        {
            writeGate.Release();
            readGate.Release();
            await store.DisposeAsync();
        }

        await Assert.ThrowsAsync<OperationCanceledException>(() => write);
        await Assert.ThrowsAsync<OperationCanceledException>(() => read);
        await Task.WhenAll(store.DisposeAsync().AsTask(), store.DisposeAsync().AsTask());
        foreach (var connection in connections.Items)
        {
            Assert.AreEqual(ConnectionState.Closed, connection.State);
            Assert.AreEqual(1, connection.DisposeCalls);
        }
        Assert.AreEqual(1, connections.Items[0].CheckpointCalls);
    }

    [TestMethod]
    public async Task Dispose_CheckpointFailsAfterTimeout_RetainsFailureAndDisposesAllConnections()
    {
        var clock = new FakeTimeProvider(TestData.s_now);
        var connections = new Connections();
        var gate = new WriteGate();
        var store = DuckDbStore.CreateForTesting(connections.Create, DatabasePath("failed-checkpoint.duckdb"),
            maxConcurrentReads: 1, beforeWrite: gate.WaitAsync, timeProvider: clock);
        var expected = new InvalidOperationException("Injected final checkpoint failure.");
        connections.Items[0].BeforeNonQuery = command =>
        {
            if (command.CommandText == "CHECKPOINT") throw expected;
        };
        var write = SeedTrace(store, context.CancellationToken);
        try
        {
            await gate.Entered.Task.WaitAsync(context.CancellationToken);
            var disposal = store.DisposeAsync().AsTask();
            await gate.Canceled.Task.WaitAsync(context.CancellationToken);
            clock.Advance(TimeSpan.FromSeconds(5));
            await Assert.ThrowsExactlyAsync<AggregateException>(() => disposal);
        }
        finally
        {
            gate.Release();
        }

        var failure = await Assert.ThrowsExactlyAsync<AggregateException>(() => store.DisposeAsync().AsTask());
        var checkpointFailure = Assert.ContainsSingle(failure.InnerExceptions);
        Assert.AreSame(expected, checkpointFailure.InnerException);
        var repeated = await Assert.ThrowsExactlyAsync<AggregateException>(() => store.DisposeAsync().AsTask());
        Assert.AreSame(checkpointFailure, Assert.ContainsSingle(repeated.InnerExceptions));
        await Assert.ThrowsAsync<OperationCanceledException>(() => write);
        foreach (var connection in connections.Items)
        {
            Assert.AreEqual(1, connection.DisposeCalls);
            Assert.AreEqual(ConnectionState.Closed, connection.State);
        }
        Assert.AreEqual(1, connections.Items[0].CheckpointCalls);
    }

    [TestMethod]
    public async Task Snapshot_CanceledAfterInsert_RollsBackSnapshotAndWatermark()
    {
        var clock = new FakeTimeProvider(TestData.s_now);
        var connections = new Connections();
        using var gate = new ReadGate(context.CancellationToken);
        await using var store = DuckDbStore.CreateForTesting(connections.Create, ":memory:", timeProvider: clock);
        await SeedTrace(store, context.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(1));
        var writer = connections.Items[0];
        writer.AfterNonQuery = gate.BlockSnapshotInsert;
        using var cancellation = new CancellationTokenSource();
        var snapshot = store.ReadSnapshotAsync("default", ct: cancellation.Token);
        try
        {
            await gate.Entered.WaitAsync(context.CancellationToken);
            await cancellation.CancelAsync();
        }
        finally
        {
            gate.Release();
        }

        await Assert.ThrowsAsync<OperationCanceledException>(() => snapshot);
        Assert.IsTrue(snapshot.IsCanceled);
        writer.AfterNonQuery = null;
        await using (var probe = writer.Duplicate())
        {
            probe.Open();
            await using var command = probe.CreateCommand();
            command.CommandText = """
                                  SELECT (SELECT count(*) FROM read_snapshots),
                                         (SELECT CAST(watermark_unix_nano AS VARCHAR) FROM signal_watermarks LIMIT 1)
                                  """;
            await using var rows = command.ExecuteReader();
            Assert.IsTrue(rows.Read());
            Assert.AreEqual(0L, rows.GetInt64(0));
            Assert.AreEqual(TestData.Nanos(TestData.s_now).ToString(CultureInfo.InvariantCulture), rows.GetString(1));
        }

        var subsequent = await store.ReadSnapshotAsync("default", ct: context.CancellationToken);
        Assert.ContainsSingle(subsequent.Spans);
        Assert.AreEqual(subsequent.Id,
            (await store.ReadSnapshotAsync("default", subsequent.Id, context.CancellationToken)).Id);
    }

    [TestMethod]
    public async Task Snapshot_CanceledDuringNativeQuery_InterruptsAndAllowsNextOperation()
    {
        var connections = new Connections();
        using var gate = new ReadGate(context.CancellationToken);
        await using var store = DuckDbStore.CreateForTesting(connections.Create, ":memory:");
        await SeedTrace(store, context.CancellationToken);
        var writer = connections.Items[0];
        Action waitForRelease = gate.Wait;
        writer.RegisterScalarFunction<int, int>("hold_snapshot", (_, output, count) =>
        {
            waitForRelease();
            for (ulong row = 0; row < count; row++) output.WriteValue(1, row);
        }, new ScalarFunctionOptions { IsPureFunction = false });
        OperationCanceledException? nativeFailure = null;
        writer.BeforeRead = command =>
        {
            if (!command.CommandText.Contains(" FROM spans WHERE", StringComparison.Ordinal)) return;
            using var nativeQuery = new DuckDBCommand(
                "SELECT sum(hold_snapshot(i::INTEGER)) FROM range(10000) AS t(i)", writer);
            try
            {
                nativeQuery.ExecuteScalar();
            }
            catch (OperationCanceledException error)
            {
                nativeFailure = error;
                throw;
            }
        };
        using var cancellation = new CancellationTokenSource();
        var snapshot = store.ReadSnapshotAsync("default", ct: cancellation.Token);
        try
        {
            await gate.Entered.WaitAsync(context.CancellationToken);
            await cancellation.CancelAsync();
        }
        finally
        {
            gate.Release();
        }

        await Assert.ThrowsAsync<OperationCanceledException>(() => snapshot);
        Assert.IsNotNull(nativeFailure, "The real native query must observe the connection interrupt.");
        writer.BeforeRead = null;
        Assert.ContainsSingle((await store.ReadSnapshotAsync("default", ct: context.CancellationToken)).Spans);
    }

    [TestMethod]
    public async Task Snapshot_CanceledBeforeClaim_DoesNotExecute()
    {
        var connections = new Connections();
        var gate = new WriteGate();
        await using var store = DuckDbStore.CreateForTesting(connections.Create, ":memory:",
            beforeWrite: gate.WaitAsync);
        var write = SeedTrace(store, context.CancellationToken);
        try
        {
            await gate.Entered.Task.WaitAsync(context.CancellationToken);
            using var cancellation = new CancellationTokenSource();
            var snapshot = store.ReadSnapshotAsync("default", ct: cancellation.Token);
            await cancellation.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => snapshot);
        }
        finally
        {
            gate.Release();
        }

        await write;
        Assert.AreEqual(1, gate.Calls);
        var result = await store.ReadSnapshotAsync("default", ct: context.CancellationToken);
        Assert.ContainsSingle(result.Spans);
        Assert.AreEqual(2, gate.Calls);
    }

    [TestMethod]
    public async Task Maintenance_CanceledAfterClaim_ObservesRequestCancellation()
    {
        var gate = new WriteGate();
        await using var store = new DuckDbStore(":memory:", beforeWrite: gate.WaitAsync);
        using var cancellation = new CancellationTokenSource();
        var checkpoint = store.CheckpointAsync(cancellation.Token);
        try
        {
            var operationToken = await gate.Entered.Task.WaitAsync(context.CancellationToken);
            await cancellation.CancelAsync();
            Assert.IsTrue(operationToken.IsCancellationRequested);
        }
        finally
        {
            gate.Release();
        }

        await Assert.ThrowsAsync<OperationCanceledException>(() => checkpoint);
        await store.CheckpointAsync(context.CancellationToken);
        Assert.AreEqual(2, gate.Calls);
    }

    [TestMethod]
    public async Task Ingestion_CallerCancelsAfterClaim_CommitsAcceptedBatch()
    {
        var gate = new WriteGate();
        await using var store = new DuckDbStore(":memory:", beforeWrite: gate.WaitAsync);
        using var cancellation = new CancellationTokenSource();
        var ingestion = SeedTrace(store, cancellation.Token);
        try
        {
            var operationToken = await gate.Entered.Task.WaitAsync(context.CancellationToken);
            await cancellation.CancelAsync();
            Assert.IsFalse(operationToken.IsCancellationRequested);
        }
        finally
        {
            gate.Release();
        }

        await ingestion;
        Assert.IsTrue(ingestion.IsCompletedSuccessfully);
        var snapshot = await store.ReadSnapshotAsync("default", ct: context.CancellationToken);
        Assert.ContainsSingle(snapshot.Spans);
    }

    [TestMethod]
    public async Task Ingestion_CanceledBeforeClaim_IsNotCommitted()
    {
        // A caller told "cancelled" may send the batch again, so a batch cancelled before the writer took it
        // is never written. Lean: Spec.CancelledBeforeClaimNeverRuns and Spec.ReportedFailureMeansNotWritten
        // (eng/proofs/storage).
        var gate = new WriteGate();
        await using var store = new DuckDbStore(DatabasePath("ingest-cancel.duckdb"), beforeWrite: gate.WaitAsync);
        var running = SeedTrace(store, context.CancellationToken);
        try
        {
            await gate.Entered.Task.WaitAsync(context.CancellationToken);
            using var cancellation = new CancellationTokenSource();
            var queued = StorageTestData.SeedTraces(store,
                OtlpData.Traces(TestData.Nanos(TestData.s_now), offset: 1), cancellation.Token);
            await cancellation.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => queued);
        }
        finally
        {
            gate.Release();
        }

        await running;
        Assert.ContainsSingle((await store.ReadSnapshotAsync("default", ct: context.CancellationToken)).Spans);
        Assert.AreEqual(2, gate.Calls);
    }

    [TestMethod]
    public async Task Dispose_QueuedJobs_EndCanceledAndUncommitted()
    {
        // I18 at the store: jobs still in the channel when disposal starts get a result (cancelled), none
        // commits, and disposal ends. Lean: Spec.ShutdownLeavesNoPendingQueuedJob and
        // Spec.ReportedFailureMeansNotWritten (eng/proofs/storage).
        var gate = new WriteGate();
        var path = DatabasePath("queued-dispose.duckdb");
        var store = new DuckDbStore(path, beforeWrite: gate.WaitAsync);
        var running = SeedTrace(store, context.CancellationToken);
        Task queuedWrite;
        Task<Nami.Collector.Storage.TelemetrySnapshot> queuedRead;
        try
        {
            await gate.Entered.Task.WaitAsync(context.CancellationToken);
            queuedWrite = StorageTestData.SeedTraces(store,
                OtlpData.Traces(TestData.Nanos(TestData.s_now), offset: 1), context.CancellationToken);
            queuedRead = store.ReadSnapshotAsync("default", ct: context.CancellationToken);
            Assert.IsFalse(queuedWrite.IsCompleted);
            Assert.IsFalse(queuedRead.IsCompleted);
            var disposal = store.DisposeAsync().AsTask();
            await gate.Canceled.Task.WaitAsync(context.CancellationToken);
            gate.Release();
            await disposal;
        }
        finally
        {
            gate.Release();
        }

        await Assert.ThrowsAsync<OperationCanceledException>(() => running);
        await Assert.ThrowsAsync<OperationCanceledException>(() => queuedWrite);
        await Assert.ThrowsAsync<OperationCanceledException>(() => queuedRead);
        await using var reopened = new DuckDbStore(path);
        Assert.AreEqual(0L, (await reopened.GetStorageStatsAsync("default", context.CancellationToken)).SpanCount);
    }

    [TestMethod]
    public async Task Trace_SequentialCalls_MatchTheModel()
    {
        // A run recorded without hooks in product code; eng/proofs/storage replays the same lines through the
        // model's step (NamiStorage/Queue/Traces.lean, theorem sequential_accepted).
        var log = new TraceLog();
        var connections = new Connections();
        await using var store = DuckDbStore.CreateForTesting(connections.Create, DatabasePath("trace-sequential.duckdb"),
            beforeWrite: log.Claim);
        log.Observe(connections.Items[0]);

        log.Add("call ingest 0");
        await SeedTrace(store, context.CancellationToken);
        log.Add("call read 1");
        var captured = await store.ReadSnapshotAsync("default", ct: context.CancellationToken);
        log.Add("call replay:1 2");
        var replayed = await store.ReadSnapshotAsync("default", captured.Id, context.CancellationToken);
        log.Add("outcome 0 ok");
        log.Add("outcome 1 ok");
        log.Add("outcome 2 ok");

        Assert.AreEqual(captured.Id, replayed.Id);
        Assert.AreSequenceEqual(
            ["call ingest 0", "claim", "commit", "call read 1", "claim", "commit", "call replay:1 2", "claim", "commit",
             "outcome 0 ok", "outcome 1 ok", "outcome 2 ok"],
            log.Lines);
    }

    [TestMethod]
    public async Task Trace_CancelledBeforeClaim_MatchesTheModel()
    {
        // The writer skips the cancelled read without running it and takes the next job
        // (NamiStorage/Queue/Traces.lean, theorem cancelledBeforeClaim_accepted).
        var gate = new WriteGate();
        var log = new TraceLog(gate);
        var connections = new Connections();
        await using var store = DuckDbStore.CreateForTesting(connections.Create, DatabasePath("trace-cancel.duckdb"),
            beforeWrite: log.Claim);
        log.Observe(connections.Items[0]);

        log.Add("call ingest 0");
        var first = SeedTrace(store, context.CancellationToken);
        Task next;
        try
        {
            await gate.Entered.Task.WaitAsync(context.CancellationToken);
            using var cancellation = new CancellationTokenSource();
            log.Add("call read 1");
            var read = store.ReadSnapshotAsync("default", ct: cancellation.Token);
            log.Add("cancel 1");
            await cancellation.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => read);
            log.Add("call ingest 2");
            next = StorageTestData.SeedTraces(store,
                OtlpData.Traces(TestData.Nanos(TestData.s_now), offset: 1), context.CancellationToken);
        }
        finally
        {
            gate.Release();
        }

        await first;
        await next;
        log.Add("outcome 0 ok");
        log.Add("outcome 1 canceled");
        log.Add("outcome 2 ok");
        Assert.AreSequenceEqual(
            ["call ingest 0", "claim", "call read 1", "cancel 1", "call ingest 2", "commit", "claim", "commit",
             "outcome 0 ok", "outcome 1 canceled", "outcome 2 ok"],
            log.Lines);
    }

    [TestMethod]
    public async Task Trace_DisposeWithQueuedJobs_MatchesTheModel()
    {
        // Disposal while one job holds the writer and two wait: all three end cancelled, none commits
        // (NamiStorage/Queue/Traces.lean, theorem disposeQueued_accepted).
        var gate = new WriteGate();
        var log = new TraceLog(gate);
        var connections = new Connections();
        var store = DuckDbStore.CreateForTesting(connections.Create, DatabasePath("trace-dispose.duckdb"),
            beforeWrite: log.Claim);
        log.Observe(connections.Items[0]);

        log.Add("call ingest 0");
        var first = SeedTrace(store, context.CancellationToken);
        Task second;
        Task<Nami.Collector.Storage.TelemetrySnapshot> third;
        try
        {
            await gate.Entered.Task.WaitAsync(context.CancellationToken);
            log.Add("call ingest 1");
            second = StorageTestData.SeedTraces(store,
                OtlpData.Traces(TestData.Nanos(TestData.s_now), offset: 1), context.CancellationToken);
            log.Add("call read 2");
            third = store.ReadSnapshotAsync("default", ct: context.CancellationToken);
            log.MarkDisposal();
            var disposal = store.DisposeAsync().AsTask();
            await gate.Canceled.Task.WaitAsync(context.CancellationToken);
            gate.Release();
            await disposal;
            log.Add("stopped");
        }
        finally
        {
            gate.Release();
        }

        await Assert.ThrowsAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAsync<OperationCanceledException>(() => second);
        await Assert.ThrowsAsync<OperationCanceledException>(() => third);
        log.Add("outcome 0 canceled");
        log.Add("outcome 1 canceled");
        log.Add("outcome 2 canceled");
        Assert.AreSequenceEqual(
            ["call ingest 0", "claim", "call ingest 1", "call read 2", "dispose", "stopped",
             "outcome 0 canceled", "outcome 1 canceled", "outcome 2 canceled"],
            log.Lines);
    }

    private static Task SeedTrace(DuckDbStore store, CancellationToken token) =>
        StorageTestData.SeedTraces(store, OtlpData.Traces(TestData.Nanos(TestData.s_now)), token);

    private string DatabasePath(string name) =>
        Path.Combine(context.TestTempDirectory ?? throw new InvalidOperationException("No test directory"), name);

    /// <summary>
    ///     Records a run in the replay format of eng/proofs/storage (NamiStorage/Queue/Trace.lean): the test
    ///     writes its own calls, the store's beforeWrite seam marks each claim, and the writer connection marks
    ///     each COMMIT (or a maintenance CHECKPOINT before disposal).
    /// </summary>
    private sealed class TraceLog(WriteGate? gate = null)
    {
        private readonly Lock _lock = new();
        private readonly List<string> _lines = [];
        private bool _disposing;

        public IReadOnlyList<string> Lines
        {
            get
            {
                lock (_lock) return [.. _lines];
            }
        }

        public void Add(string line)
        {
            lock (_lock) _lines.Add(line);
        }

        public void MarkDisposal()
        {
            lock (_lock)
            {
                _disposing = true;
                _lines.Add("dispose");
            }
        }

        public ValueTask Claim(CancellationToken token)
        {
            Add("claim");
            return gate?.WaitAsync(token) ?? ValueTask.CompletedTask;
        }

        public void Observe(ObservedConnection writer) => writer.AfterNonQuery = command =>
        {
            var text = command.CommandText.Trim().TrimEnd(';');
            lock (_lock)
            {
                if (text == "COMMIT" || (text == "CHECKPOINT" && !_disposing))
                    _lines.Add("commit");
            }
        };
    }

    private sealed class ReadGate(CancellationToken cancellationToken) : IDisposable
    {
        private readonly ManualResetEventSlim _released = new();
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _executions;

        public Task Entered => _entered.Task;
        public int Executions => Volatile.Read(ref _executions);

        public void BlockFirstRead(DuckDBCommand _)
        {
            if (Interlocked.Increment(ref _executions) != 1) return;
            BlockRead(_);
        }

        public void BlockRead(DuckDBCommand _)
        {
            _entered.SetResult();
            _released.Wait(cancellationToken);
        }

        public void BlockSnapshotInsert(DuckDBCommand command)
        {
            if (!command.CommandText.StartsWith("INSERT", StringComparison.Ordinal) ||
                !command.CommandText.Contains("read_snapshots", StringComparison.Ordinal)) return;
            BlockRead(command);
        }

        public void Wait()
        {
            _entered.TrySetResult();
            _released.Wait(cancellationToken);
        }

        public void Release() => _released.Set();
        public void Dispose() => _released.Dispose();
    }

    private sealed class WriteGate
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public TaskCompletionSource<CancellationToken> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Canceled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls => Volatile.Read(ref _calls);

        public async ValueTask WaitAsync(CancellationToken token)
        {
            if (Interlocked.Increment(ref _calls) != 1) return;
            using var registration = token.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), Canceled);
            Entered.SetResult(token);
            await _released.Task;
        }

        public void Release() => _released.TrySetResult();
    }

    private sealed class Connections
    {
        public List<ObservedConnection> Items { get; } = [];
        public int? FailOpenAt { get; init; }
        public Exception? OpenFailure { get; init; }

        public ObservedConnection Create(string connectionString)
        {
            var connection = new ObservedConnection(connectionString)
            {
                OpenFailure = Items.Count == FailOpenAt ? OpenFailure : null
            };
            Items.Add(connection);
            return connection;
        }
    }

    private sealed class ObservedConnection(string connectionString) : DuckDBConnection(connectionString)
    {
        private int _disposeCalls;
        private int _checkpointCalls;

        public Exception? OpenFailure { get; init; }
        public Action<DuckDBCommand>? BeforeRead { get; set; }
        public Action<DuckDBCommand>? BeforeNonQuery { get; set; }
        public Action<DuckDBCommand>? AfterNonQuery { get; set; }
        public Thread? LastReadThread { get; private set; }
        public int DisposeCalls => Volatile.Read(ref _disposeCalls);
        public int CheckpointCalls => Volatile.Read(ref _checkpointCalls);

        private void RecordCheckpoint() => Interlocked.Increment(ref _checkpointCalls);

        public override void Open()
        {
            if (OpenFailure is { } error) throw error;
            base.Open();
        }

        public override DuckDBCommand CreateCommand()
        {
            using var command = base.CreateCommand();
            return new ObservedCommand(this, command.Transaction);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Interlocked.Increment(ref _disposeCalls);
            base.Dispose(disposing);
        }

        private sealed class ObservedCommand : DuckDBCommand
        {
            private readonly ObservedConnection _owner;

            public ObservedCommand(ObservedConnection owner, DbTransaction? transaction)
            {
                _owner = owner;
                Connection = owner;
                Transaction = transaction;
            }

            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            {
                _owner.LastReadThread = Thread.CurrentThread;
                _owner.BeforeRead?.Invoke(this);
                return base.ExecuteDbDataReader(behavior);
            }

            public override int ExecuteNonQuery()
            {
                if (CommandText == "CHECKPOINT") _owner.RecordCheckpoint();
                _owner.BeforeNonQuery?.Invoke(this);
                var result = base.ExecuteNonQuery();
                _owner.AfterNonQuery?.Invoke(this);
                return result;
            }
        }
    }
}
