namespace Nami.UnitTests;

[TestClass]
public sealed class SharedFixtureLifetimeTests
{
    [TestMethod]
    public async Task GetAsync_ConcurrentConsumers_CreateAndDisposeOnce()
    {
        var created = 0;
        var counter = new Counter();
        var fixture = new SharedFixture<Resource>(() =>
        {
            Interlocked.Increment(ref created);
            return Task.FromResult(new Resource(counter));
        });
        try
        {
            var values = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => fixture.GetAsync()));
            Assert.AreEqual(1, created);
            foreach (var value in values) Assert.AreSame(values[0], value);
            Assert.AreEqual(0, counter.Disposed);
        }
        finally
        {
            await Task.WhenAll(fixture.DisposeAsync().AsTask(), fixture.DisposeAsync().AsTask());
        }

        Assert.AreEqual(1, counter.Disposed);
        Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.GetAsync());
    }

    [TestMethod]
    public async Task DisposeAsync_UnusedFixture_DoesNotCreateAResource()
    {
        var created = 0;
        var fixture = new SharedFixture<Resource>(() =>
        {
            created++;
            return Task.FromResult(new Resource(new Counter()));
        });
        await fixture.DisposeAsync();
        Assert.AreEqual(0, created);
        Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.GetAsync());
    }

    [TestMethod]
    public async Task DisposeAsync_PendingCreation_WaitsAndThenDisposes()
    {
        var counter = new Counter();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fixture = new SharedFixture<Resource>(async () =>
        {
            await ready.Task;
            return new Resource(counter);
        });
        var waiting = fixture.GetAsync();
        var disposal = fixture.DisposeAsync().AsTask();
        Assert.IsFalse(disposal.IsCompleted);
        ready.SetResult();
        _ = await waiting;
        await disposal;
        Assert.AreEqual(1, counter.Disposed);
    }

    private sealed class Counter
    {
        internal int Disposed;
    }

    private sealed class Resource(Counter counter) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref counter.Disposed);
            return ValueTask.CompletedTask;
        }
    }
}
