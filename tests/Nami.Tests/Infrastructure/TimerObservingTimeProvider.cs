using System.Threading.Channels;

namespace Nami.Tests.Infrastructure;

/// <summary>Signals only after the underlying clock has registered a timer.</summary>
internal sealed class TimerObservingTimeProvider(TimeProvider inner) : TimeProvider
{
    private readonly Channel<TimeSpan> _timers = Channel.CreateUnbounded<TimeSpan>(new UnboundedChannelOptions
    {
        SingleReader = true,
        AllowSynchronousContinuations = false
    });

    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();
    public override long GetTimestamp() => inner.GetTimestamp();
    public override long TimestampFrequency => inner.TimestampFrequency;
    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = inner.CreateTimer(callback, state, dueTime, period);
        _timers.Writer.TryWrite(dueTime);
        return timer;
    }

    public ValueTask<TimeSpan> WaitForTimerAsync(CancellationToken cancellationToken) =>
        _timers.Reader.ReadAsync(cancellationToken);

    public bool TryReadTimer(out TimeSpan dueTime) => _timers.Reader.TryRead(out dueTime);
}
