using System;
using System.IO;

namespace Nami.Collector.Storage;

internal static class DuckDbValueReader
{
    public static T? Read<T>(DbDataReader reader, int ordinal) where T : struct =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<T>(ordinal);

    public static T Read<T>(DbDataReader reader, int ordinal, T defaultValue) where T : struct =>
        Read<T>(reader, ordinal) ?? defaultValue;

    public static string? ReadString(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

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

}
