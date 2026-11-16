using DuckDB.NET.Data;

namespace Nami.IntegrationTests;

/// <summary>Every row of every table of a closed store file, as DuckDB prints it, for comparing whole stores.</summary>
internal static class StoreDump
{
    /// <summary>
    ///     The rows of each table in <paramref name="path" />, sorted. An entry of <paramref name="excluded" /> names
    ///     a table to leave out, or as <c>table.column</c> one column to leave out of that table's rows.
    /// </summary>
    internal static SortedDictionary<string, string[]> Read(string path, params string[] excluded)
    {
        // The builder quotes a path that holds a separator of the connection string, such as ';' or '='.
        using var connection = new DuckDBConnection(
            new DuckDBConnectionStringBuilder { DataSource = path }.ConnectionString);
        connection.Open();
        var tables = new List<string>();
        using (var list = connection.CreateCommand())
        {
            list.CommandText =
                "SELECT table_name FROM information_schema.tables WHERE table_schema = 'main' ORDER BY table_name";
            using var reader = list.ExecuteReader();
            while (reader.Read()) tables.Add(reader.GetString(0));
        }

        var dump = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var table in tables.Where(table => !excluded.Contains(table, StringComparer.Ordinal)))
        {
            var columns = excluded.Where(entry => entry.StartsWith(table + ".", StringComparison.Ordinal))
                .Select(entry => "\"" + entry[(table.Length + 1)..] + "\"").ToArray();
            using var read = connection.CreateCommand();
            // CA2100: the names are this store's own tables and the columns the calling test lists.
            read.CommandText = "SELECT CAST(stored AS VARCHAR) FROM (SELECT *" +
                               (columns.Length == 0 ? "" : " EXCLUDE (" + string.Join(", ", columns) + ")") +
                               " FROM \"" + table + "\") AS stored ORDER BY 1";
            using var reader = read.ExecuteReader();
            var rows = new List<string>();
            while (reader.Read()) rows.Add(reader.GetString(0));
            dump.Add(table, [.. rows]);
        }

        return dump;
    }
}
