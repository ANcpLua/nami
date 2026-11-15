using System.Globalization;
using DuckDB.NET.Data;

namespace Nami.IntegrationTests.ClaudeCode;

/// <summary>Reads what a stopped collector left in its DuckDB file, with SQL and without nami's store.</summary>
internal static class StoredRows
{
    /// <summary>The number of stored log records (events) per event name in <paramref name="project" />; "" counts those without one.</summary>
    internal static SortedDictionary<string, long> EventNames(string path, string project)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT coalesce(event_name, ''), count(*) FROM logs WHERE project_id = $1 GROUP BY ALL";
        command.Parameters.Add(new DuckDBParameter { Value = project });
        using var reader = command.ExecuteReader();
        var names = new SortedDictionary<string, long>(StringComparer.Ordinal);
        while (reader.Read()) names[reader.GetString(0)] = reader.GetInt64(1);
        return names;
    }

    /// <summary>
    ///     The rows that contain <paramref name="marker" />, per <c>table.column</c>, over every column of every table
    ///     in the file, each read as text; a column without such a row is left out.
    /// </summary>
    internal static SortedDictionary<string, long> ColumnsContaining(string path, string marker)
    {
        using var connection = Open(path);
        var columns = new List<(string Table, string Column)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT table_name, column_name FROM duckdb_columns()
                WHERE database_name = current_database() AND schema_name = 'main' AND NOT internal
                ORDER BY table_name, column_index
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read()) columns.Add((reader.GetString(0), reader.GetString(1)));
        }

        if (columns.Count is 0) throw new InvalidDataException($"{path} holds no table.");
        var found = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var (table, column) in columns)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT count(*) FROM {Quote(table)} WHERE contains(CAST({Quote(column)} AS VARCHAR), $1)";
            command.Parameters.Add(new DuckDBParameter { Value = marker });
            var rows = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (rows > 0) found[table + "." + column] = rows;
        }

        return found;
    }

    /// <summary>Up to <paramref name="limit" /> stored values of <c>table.column</c> around <paramref name="marker" />.</summary>
    internal static IReadOnlyList<string> Excerpts(string path, string tableColumn, string marker, int limit = 3)
    {
        var (table, column) = (tableColumn[..tableColumn.IndexOf('.', StringComparison.Ordinal)],
            tableColumn[(tableColumn.IndexOf('.', StringComparison.Ordinal) + 1)..]);
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT CAST({Quote(column)} AS VARCHAR) FROM {Quote(table)} WHERE contains(CAST({Quote(column)} AS VARCHAR), $1) LIMIT {limit}";
        command.Parameters.Add(new DuckDBParameter { Value = marker });
        using var reader = command.ExecuteReader();
        var excerpts = new List<string>();
        while (reader.Read())
        {
            var text = reader.GetString(0);
            var at = text.IndexOf(marker, StringComparison.Ordinal);
            var start = Math.Max(0, at - 60);
            excerpts.Add(text[start..Math.Min(text.Length, at + marker.Length + 60)]);
        }

        return excerpts;
    }

    private static DuckDBConnection Open(string path)
    {
        var connection = new DuckDBConnection($"DataSource={path};ACCESS_MODE=READ_ONLY");
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static string Quote(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
