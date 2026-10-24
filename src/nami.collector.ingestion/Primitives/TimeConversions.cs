using System;

namespace Nami.Collector.Ingestion.Primitives;

internal static class TimeConversions
{
    private const ulong NanosPerTick = 100UL;

    public static ulong ToUnixNanoUnsigned(DateTimeOffset dto) =>
        TryToUnixNanoUnsigned(dto, out var unixNano)
            ? unixNano
            : throw new ArgumentOutOfRangeException(
                nameof(dto),
                dto,
                "Timestamp is outside the unsigned OTLP Unix-nanosecond range.");

    private static bool TryToUnixNanoUnsigned(DateTimeOffset dto, out ulong unixNano)
    {
        unixNano = 0;
        var utcTicks = dto.UtcTicks;
        var unixEpochTicks = DateTimeOffset.UnixEpoch.UtcTicks;
        if (utcTicks < unixEpochTicks)
            return false;

        var elapsedTicks = (ulong)(utcTicks - unixEpochTicks);
        if (elapsedTicks > ulong.MaxValue / NanosPerTick)
            return false;

        unixNano = elapsedTicks * NanosPerTick;
        return true;
    }


    public static DateTimeOffset NanosToDateTimeOffset(ulong nanos) =>
        DateTimeOffset.UnixEpoch.AddTicks((long)(nanos / NanosPerTick));
}
