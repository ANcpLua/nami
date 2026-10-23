namespace Nami.Collector.Ingestion.Primitives;

internal static class TimeConversions
{
    private const long NanosPerMillisecond = 1_000_000L;
    private const ulong NanosPerTick = 100UL;

    public static ulong ToUnixNanoUnsigned(DateTimeOffset dto) =>
        TryToUnixNanoUnsigned(dto, out var unixNano)
            ? unixNano
            : throw new ArgumentOutOfRangeException(
                nameof(dto),
                dto,
                "Timestamp is outside the unsigned OTLP Unix-nanosecond range.");

    public static bool TryToUnixNanoUnsigned(DateTimeOffset dto, out ulong unixNano)
    {
        unixNano = 0;
        var utcTicks = dto.ToUniversalTime().Ticks;
        var unixEpochTicks = DateTimeOffset.UnixEpoch.Ticks;
        if (utcTicks < unixEpochTicks)
            return false;

        var elapsedTicks = (ulong)(utcTicks - unixEpochTicks);
        if (elapsedTicks > ulong.MaxValue / NanosPerTick)
            return false;

        unixNano = elapsedTicks * NanosPerTick;
        return true;
    }

    public static double NanosToMs(ulong nanos) => nanos / (double)NanosPerMillisecond;

    public static DateTimeOffset NanosToDateTimeOffset(ulong nanos) =>
        DateTimeOffset.UnixEpoch.AddTicks((long)(nanos / NanosPerTick));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DateTime UnixNanoToDateTime(ulong unixNano)
    {
        var ticks = (long)(unixNano / NanosPerTick);
        return DateTime.UnixEpoch.AddTicks(ticks);
    }
}
