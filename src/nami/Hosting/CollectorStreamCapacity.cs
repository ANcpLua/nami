namespace Nami.Collector.Hosting;

/// <summary>
/// Bounds long-lived collector streams independently from ordinary HTTP requests. Each admitted
/// stream owns a DuckDB polling loop, so admission must be immediate and queue-free.
/// </summary>
internal sealed class CollectorStreamCapacity(int maximum = CollectorStreamCapacity.DefaultMaximum)
{
    private const int DefaultMaximum = 16;

    private readonly int _maximum = maximum > 0
        ? maximum
        : throw new ArgumentOutOfRangeException(nameof(maximum));
    private int _active;

    internal int Active => Volatile.Read(ref _active);

    internal IDisposable? TryAcquire()
    {
        while (true)
        {
            var current = Volatile.Read(ref _active);
            if (current >= _maximum)
            {
                return null;
            }

            if (Interlocked.CompareExchange(ref _active, current + 1, current) != current) continue;
            return new Lease(this);
        }
    }

    private sealed class Lease(CollectorStreamCapacity owner) : IDisposable
    {
        private CollectorStreamCapacity? _owner = owner;

        public void Dispose()
        {
            var ownerToRelease = Interlocked.Exchange(ref _owner, null);
            if (ownerToRelease is not null) Interlocked.Decrement(ref ownerToRelease._active);
        }
    }
}
