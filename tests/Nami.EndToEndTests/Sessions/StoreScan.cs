using System.Globalization;
using System.IO.Compression;
using System.Text;
using DuckDB.NET.Data;

namespace Nami.EndToEndTests.Sessions;

/// <summary>
///     Searches every value a stopped nami stored for a text: each column of each table, cast to text, and each BLOB
///     as UTF-8 and, when it is Brotli, decompressed (snapshot payloads). The schema is read from the file, so the
///     scan also covers tables M2 adds.
/// </summary>
internal static class StoreScan
{
    internal static async Task<IReadOnlyList<string>> FindAsync(string databasePath, string needle,
        CancellationToken cancellationToken)
    {
        await using var connection = new DuckDBConnection(
            string.Create(CultureInfo.InvariantCulture, $"DataSource={databasePath};vacuum_rebuild_indexes={ulong.MaxValue}"));
        await connection.OpenAsync(cancellationToken);
        var columns = new List<(string Schema, string Table, string Column, string Type)>();
        await using (var list = connection.CreateCommand())
        {
            list.CommandText = """
                               SELECT c.table_schema, c.table_name, c.column_name, c.data_type
                               FROM information_schema.columns c
                               JOIN information_schema.tables t
                                 ON t.table_catalog = c.table_catalog AND t.table_schema = c.table_schema
                                AND t.table_name = c.table_name
                               WHERE t.table_type = 'BASE TABLE' AND c.table_catalog = current_database()
                               ORDER BY c.table_schema, c.table_name, c.ordinal_position
                               """;
            await using var rows = await list.ExecuteReaderAsync(cancellationToken);
            while (await rows.ReadAsync(cancellationToken))
                columns.Add((rows.GetString(0), rows.GetString(1), rows.GetString(2), rows.GetString(3)));
        }

        if (columns.Count == 0) throw new InvalidDataException($"{databasePath} holds no table.");
        var hits = new List<string>();
        foreach (var (schema, table, column, type) in columns)
        {
            var source = Quote(schema) + "." + Quote(table);
            var name = $"{table}.{column}";
            await using var command = connection.CreateCommand();
            if (type == "BLOB")
            {
                command.CommandText = $"SELECT {Quote(column)} FROM {source} WHERE {Quote(column)} IS NOT NULL";
                await using var rows = await command.ExecuteReaderAsync(cancellationToken);
                while (await rows.ReadAsync(cancellationToken))
                {
                    if (!Holds(Bytes(rows.GetValue(0)), needle)) continue;
                    hits.Add(name + " (BLOB)");
                    break;
                }
            }
            else
            {
                command.CommandText =
                    $"SELECT count(*) FROM {source} WHERE strpos(CAST({Quote(column)} AS VARCHAR), $1) > 0";
                command.Parameters.Add(new DuckDBParameter { Value = needle });
                var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
                if (count > 0) hits.Add($"{name} ({count} rows)");
            }
        }

        return hits;
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static byte[] Bytes(object value)
    {
        switch (value)
        {
            case byte[] bytes:
                return bytes;
            case Stream stream:
                {
                    using var copy = new MemoryStream();
                    stream.CopyTo(copy);
                    return copy.ToArray();
                }
            default:
                return Encoding.UTF8.GetBytes(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
        }
    }

    private static bool Holds(byte[] value, string needle)
    {
        if (Encoding.UTF8.GetString(value).Contains(needle, StringComparison.Ordinal)) return true;
        try
        {
            using var compressed = new MemoryStream(value, false);
            using var brotli = new BrotliStream(compressed, CompressionMode.Decompress);
            using var text = new StreamReader(brotli, Encoding.UTF8);
            return text.ReadToEnd().Contains(needle, StringComparison.Ordinal);
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}
