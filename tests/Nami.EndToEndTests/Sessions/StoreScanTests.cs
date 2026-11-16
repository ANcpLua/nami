using DuckDB.NET.Data;

namespace Nami.EndToEndTests.Sessions;

[TestClass]
[TestCategory("EndToEnd")]
public sealed class StoreScanTests(TestContext context)
{
    /// <summary>
    ///     The scan takes a path as the store does: a separator or a quote of a connection string in it is part of
    ///     the path, not the end of it.
    /// </summary>
    [TestMethod]
    public async Task Find_PathWithConnectionStringCharacters_ScansThatFile()
    {
        var directory = Path.Combine(
            context.TestTempDirectory ?? throw new InvalidOperationException("No test directory"), "all;of='them'");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "nami.duckdb");
        await using (var connection = new DuckDBConnection(
                         new DuckDBConnectionStringBuilder { DataSource = path }.ConnectionString))
        {
            await connection.OpenAsync(context.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE held (text VARCHAR); INSERT INTO held VALUES ('a needle in it')";
            await command.ExecuteNonQueryAsync(context.CancellationToken);
        }

        var hits = await StoreScan.FindAsync(path, "needle", context.CancellationToken);

        Assert.AreEqual("held.text (1 rows)", Assert.ContainsSingle(hits));
    }
}
