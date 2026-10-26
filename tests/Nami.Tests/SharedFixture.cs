namespace Nami.Tests;

/// <summary>Creates one resource for a group of tests and disposes it when that group ends.</summary>
public sealed class SharedFixture<T>(Func<Task<T>> create) : IAsyncDisposable where T : IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly Lazy<Task<T>> _value = new(async () => await create().ConfigureAwait(false));
    private Task? _disposal;
    private bool _closed;

    public Task<T> GetAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            return _value.Value.WaitAsync(cancellationToken);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _closed = true;
            _disposal ??= DisposeCoreAsync();
            return new ValueTask(_disposal);
        }
    }

    private async Task DisposeCoreAsync()
    {
        if (_value.IsValueCreated)
        {
            var value = await _value.Value.ConfigureAwait(false);
            await value.DisposeAsync().ConfigureAwait(false);
        }
    }
}
