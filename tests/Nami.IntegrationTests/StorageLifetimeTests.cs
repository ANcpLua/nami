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
        await using var store = DuckDbStore.CreateForTesting(connections.Create,
            DatabasePath("reader-queue.duckdb"), maxConcurrentReads: 1, readQueueCapacity: 1);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executions = 0;
        connections.Items[1].BeforeRead = _ =>
        {
            if (Interlocked.Increment(ref executions) != 1) return;
            entered.SetResult();
            release.Wait(context.CancellationToken);
        };

        var first = store.GetStorageStatsAsync("default", context.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(context.CancellationToken);
            var second = store.GetStorageStatsAsync("default", context.CancellationToken);
            using var cancellation = new CancellationTokenSource();
            var rejected = store.GetStorageStatsAsync("default", cancellation.Token);
            Assert.IsFalse(first.IsCompleted);
            Assert.IsFalse(second.IsCompleted);
            await cancellation.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => rejected);
            Assert.AreEqual(1, Volatile.Read(ref executions));

            release.Set();
            Assert.AreEqual(0L, (await first).SpanCount);
            Assert.AreEqual(0L, (await second).SpanCount);
            Assert.AreEqual(2, executions);
        }
        finally
        {
            release.Set();
        }
    }

    [TestMethod]
    public async Task Dispose_ActiveWorkersOutliveTimeout_RetainsConnectionsUntilBothStop()
    {
        var clock = new FakeTimeProvider(TestData.s_now);
        var connections = new Connections();
        var writeGate = new WriteGate();
        var store = DuckDbStore.CreateForTesting(connections.Create, DatabasePath("shutdown.duckdb"),
            maxConcurrentReads: 1, beforeWrite: writeGate.WaitAsync, timeProvider: clock);
        using var readRelease = new ManualResetEventSlim();
        var readEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connections.Items[1].BeforeRead = _ =>
        {
            readEntered.SetResult();
            readRelease.Wait(context.CancellationToken);
        };

        var write = SeedTrace(store, context.CancellationToken);
        Task<StorageStats>? read = null;
        try
        {
            await writeGate.Entered.Task.WaitAsync(context.CancellationToken);
            read = store.GetStorageStatsAsync("default", context.CancellationToken);
            await readEntered.Task.WaitAsync(context.CancellationToken);
            var firstDisposal = store.DisposeAsync().AsTask();
            var concurrentDisposal = store.DisposeAsync().AsTask();
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
            readRelease.Set();
            await store.DisposeAsync();
        }

        await Assert.ThrowsAsync<OperationCanceledException>(() => write);
        Assert.IsNotNull(read);
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
        await using var store = DuckDbStore.CreateForTesting(connections.Create, ":memory:", timeProvider: clock);
        await SeedTrace(store, context.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(1));
        using var release = new ManualResetEventSlim();
        var inserted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = connections.Items[0];
        writer.AfterNonQuery = command =>
        {
            if (!command.CommandText.StartsWith("INSERT", StringComparison.Ordinal) ||
                !command.CommandText.Contains("read_snapshots", StringComparison.Ordinal)) return;
            inserted.SetResult();
            release.Wait(context.CancellationToken);
        };
        using var cancellation = new CancellationTokenSource();
        var snapshot = store.ReadSnapshotAsync("default", ct: cancellation.Token);
        try
        {
            await inserted.Task.WaitAsync(context.CancellationToken);
            await cancellation.CancelAsync();
        }
        finally
        {
            release.Set();
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
        await using var store = DuckDbStore.CreateForTesting(connections.Create, ":memory:");
        await SeedTrace(store, context.CancellationToken);
        var writer = connections.Items[0];
        using var release = new ManualResetEventSlim();
        var enteredNativeQuery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        writer.RegisterScalarFunction<int, int>("hold_snapshot", (_, output, count) =>
        {
            enteredNativeQuery.TrySetResult();
            release.Wait(context.CancellationToken);
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
            await enteredNativeQuery.Task.WaitAsync(context.CancellationToken);
            await cancellation.CancelAsync();
        }
        finally
        {
            release.Set();
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

    private static Task SeedTrace(DuckDbStore store, CancellationToken token) =>
        StorageTestData.SeedTraces(store, OtlpData.Traces(TestData.Nanos(TestData.s_now)), token);

    private string DatabasePath(string name) =>
        Path.Combine(context.TestTempDirectory ?? throw new InvalidOperationException("No test directory"), name);

    private sealed class WriteGate
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public TaskCompletionSource<CancellationToken> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls => Volatile.Read(ref _calls);

        public async ValueTask WaitAsync(CancellationToken token)
        {
            if (Interlocked.Increment(ref _calls) != 1) return;
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
