using System.Numerics;

namespace Nami.Collector.Storage;

internal static class DuckDbValueReader
{
    // DuckDB aggregates (SUM over BIGINT, COUNT) return HUGEINT, which the provider surfaces as
    // System.Numerics.BigInteger. BigInteger is not IConvertible, so it converts to the target type
    // directly: checked for an integer, which throws OverflowException when the value does not fit,
    // and to the nearest double for a double, whatever its size.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T Number<T>(object value, Func<object, IFormatProvider, T> convert)
        where T : INumberBase<T> =>
        value is BigInteger big ? T.CreateChecked(big) : convert(value, CultureInfo.InvariantCulture);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string? ReadString(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ReadString(DbDataReader reader, int ordinal, string defaultValue) =>
        ReadString(reader, ordinal) ?? defaultValue;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong? ReadUInt64(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : Number(reader.GetValue(ordinal), Convert.ToUInt64);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ReadUInt64(DbDataReader reader, int ordinal, ulong defaultValue) =>
        ReadUInt64(reader, ordinal) ?? defaultValue;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long? ReadInt64(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : Number(reader.GetValue(ordinal), Convert.ToInt64);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ReadInt64(DbDataReader reader, int ordinal, long defaultValue) =>
        ReadInt64(reader, ordinal) ?? defaultValue;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int? ReadInt32(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : Number(reader.GetValue(ordinal), Convert.ToInt32);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReadInt32(DbDataReader reader, int ordinal, int defaultValue) =>
        ReadInt32(reader, ordinal) ?? defaultValue;

    public static uint? ReadUInt32(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : Number(reader.GetValue(ordinal), Convert.ToUInt32);

    public static uint ReadUInt32(DbDataReader reader, int ordinal, uint defaultValue) =>
        ReadUInt32(reader, ordinal) ?? defaultValue;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte? ReadByte(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : Number(reader.GetValue(ordinal), Convert.ToByte);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte ReadByte(DbDataReader reader, int ordinal, byte defaultValue) =>
        ReadByte(reader, ordinal) ?? defaultValue;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double? ReadDouble(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : Number(reader.GetValue(ordinal), Convert.ToDouble);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ReadDouble(DbDataReader reader, int ordinal, double defaultValue) =>
        ReadDouble(reader, ordinal) ?? defaultValue;

    public static byte[] ReadBytes(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return [];

        var value = reader.GetValue(ordinal);
        if (value is byte[] bytes)
            return bytes;
        if (value is not Stream stream)
            throw new InvalidCastException($"Cannot read column {ordinal} as bytes.");

        if (stream.CanSeek)
            stream.Position = 0;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public static DateTimeOffset? ReadDateTimeOffset(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return null;

        return reader.GetValue(ordinal) switch
        {
            DateTimeOffset value => value,
            DateTime value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)),
            string value when DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed) => parsed,
            _ => throw new InvalidCastException($"Cannot read column {ordinal} as DateTimeOffset.")
        };
    }
}
