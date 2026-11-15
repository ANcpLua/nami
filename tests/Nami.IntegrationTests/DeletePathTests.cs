using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Time.Testing;
using Nami.Collector.Storage;
using Nami.Tests.Infrastructure;
using Nami.Tools;

namespace Nami.IntegrationTests;

/// <summary>
///     Retention is the only path that deletes recorded telemetry. One test runs every tool against a store file
///     and compares the file's rows; the other reads the product's sources for statements that remove rows and for
///     callers of the retention methods.
/// </summary>
[TestClass]
[TestCategory("Integration")]
[DependsOn(typeof(IngestStoreTests), nameof(IngestStoreTests.Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
public sealed partial class DeletePathTests(TestContext context)
{
    private const string Storage = "src/nami.collector.storage/";
    private static readonly string[] s_productDirectories = ["src", "internal"];

    /// <summary>
    ///     Every statement in src/ and internal/ that removes rows or tables, as file, method and statement. The first
    ///     seven are retention: methods that only <c>DuckDbStore.DeleteExpiredAsync</c> calls (see
    ///     <see cref="s_retentionCalls" />). The rest never touch recorded telemetry: metric_points and
    ///     metric_exemplars are projections derived from metric_observations and rewritten from them, read_snapshots
    ///     is the cache of answered reads, and DropTables recreates the schema only while the authoritative tables are
    ///     empty (StorageSchemaTests.Open_MissingSchemaIdentityWithData_RejectsAdoptionWithoutChangingRows).
    /// </summary>
    private static readonly string[] s_rowRemovals =
    [
        Storage + "DuckDbStore.Retention.cs DeleteExpiredLogsBatchInternalAsync: DELETE FROM logs",
        Storage + "DuckDbStore.Retention.cs DeleteExpiredSpansBatchInternalAsync: DELETE FROM spans",
        Storage + "DuckDbStore.Retention.cs DeleteExpiredMetadataAsync: DELETE FROM resource_observations",
        Storage + "DuckDbStore.Retention.cs DeleteExpiredMetadataAsync: DELETE FROM read_snapshots",
        Storage + "DuckDbStore.Metrics.cs DeleteExpiredMetricsBatchAsync: DELETE FROM metric_observations",
        Storage + "DuckDbStore.Metrics.cs DeleteExpiredMetricsBatchAsync: DELETE FROM metric_exemplars",
        Storage + "DuckDbStore.Metrics.cs DeleteExpiredMetricsBatchAsync: DELETE FROM metric_series",
        Storage + "DuckDbStore.MetricObservations.cs WriteMetricProjection: DELETE FROM metric_points",
        Storage + "DuckDbStore.MetricObservations.cs WriteMetricProjection: DELETE FROM metric_exemplars",
        Storage + "DuckDbStore.MetricObservations.cs RebuildMetricProjections: DELETE FROM metric_points",
        Storage + "DuckDbStore.MetricObservations.cs RebuildMetricProjections: DELETE FROM metric_exemplars",
        Storage + "DuckDbStore.Snapshots.cs ReadSnapshotAsync: DELETE FROM read_snapshots",
        Storage + "DuckDbStore.cs DropTables: DROP TABLE"
    ];

    /// <summary>Every call of a retention method in src/ and internal/: the store's sweep, and the service that starts it.</summary>
    private static readonly string[] s_retentionCalls =
    [
        Storage + "DuckDbStore.Retention.cs DeleteExpiredAsync: DeleteExpiredLogsBatchAsync",
        Storage + "DuckDbStore.Retention.cs DeleteExpiredAsync: DeleteExpiredSpansBatchAsync",
        Storage + "DuckDbStore.Retention.cs DeleteExpiredAsync: DeleteExpiredMetricsBatchAsync",
        Storage + "DuckDbStore.Retention.cs DeleteExpiredAsync: DeleteExpiredMetadataAsync",
        Storage + "DuckDbStore.Retention.cs DeleteExpiredLogsBatchAsync: DeleteExpiredLogsBatchInternalAsync",
        Storage + "DuckDbStore.Retention.cs DeleteExpiredSpansBatchAsync: DeleteExpiredSpansBatchInternalAsync",
        "src/nami/Retention/RetentionService.cs RunCycleAsync: DeleteExpiredAsync"
    ];

    [TestMethod]
    public async Task Tools_NeverChangeStoredTelemetry()
    {
        var path = Path.Combine(
            context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"),
            "tools-read-only.duckdb");
        var clock = new FakeTimeProvider(TestData.s_now);
        var trace = 1.ToString("x32", CultureInfo.InvariantCulture);
        await using (var store = new DuckDbStore(path, timeProvider: clock))
        {
            // Recent telemetry of every signal, and telemetry older than any retention period a call could apply.
            var recent = TestData.Nanos(TestData.s_now.AddSeconds(-5));
            var old = TestData.Nanos(TestData.s_now.AddDays(-400));
            await StorageTestData.SeedTraces(store, OtlpData.Traces(recent, error: true, exceptionEvent: true),
                context.CancellationToken);
            await StorageTestData.SeedLogs(store,
                OtlpData.Logs(recent, exception: true, trace: trace,
                    span: 1.ToString("x16", CultureInfo.InvariantCulture)), clock, context.CancellationToken);
            await StorageTestData.SeedMetrics(store, OtlpData.Metrics(recent), context.CancellationToken);
            await StorageTestData.SeedTraces(store, OtlpData.Traces(old, offset: 1, service: "archive"),
                context.CancellationToken);
            await StorageTestData.SeedLogs(store, OtlpData.Logs(old, "archived", service: "archive"), clock,
                context.CancellationToken);
            await StorageTestData.SeedMetrics(store, OtlpData.Metrics(old, service: "archive"),
                context.CancellationToken);
        }

        var before = Telemetry(path);
        Assert.HasCount(2, before["spans"]);
        Assert.HasCount(2, before["logs"]);
        Assert.HasCount(2, before["metric_series"]);
        Assert.HasCount(2, before["metric_observations"]);

        clock.Advance(TimeSpan.FromMinutes(5));
        await using (var store = new DuckDbStore(path, timeProvider: clock))
        {
            var tools = new NamiToolService(store);
            Assert.HasCount(6, ToolDispatcher.Names);
            foreach (var tool in ToolDispatcher.Names)
            {
                var arguments = tool switch
                {
                    "traces_get" => "{\"trace_id\":\"" + trace + "\"}",
                    "logs_search" => "{\"query\":\"accepted\"}",
                    _ => "{}"
                };
                using var input = JsonDocument.Parse(arguments);
                var answer = await tools.InvokeAsync(tool, input.RootElement, ProjectScope.DefaultProjectId,
                    context.CancellationToken);
                Assert.IsFalse(answer.IsError, tool + ": " + answer.Content.GetRawText());
                // The same call again on the snapshot of the first answer, the path a paging agent takes.
                using var replay = JsonDocument.Parse(arguments[..^1] + (arguments.Length > 2 ? "," : "") +
                                                      "\"snapshot_id\":\"" +
                                                      answer.Content.GetProperty("snapshot_id").GetString() + "\"}");
                var replayed = await tools.InvokeAsync(tool, replay.RootElement, ProjectScope.DefaultProjectId,
                    context.CancellationToken);
                Assert.AreEqual(answer.Content.GetRawText(), replayed.Content.GetRawText(), tool);
            }
        }

        var after = Telemetry(path);
        Assert.AreSequenceEqual(before.Keys.ToArray(), after.Keys.ToArray());
        foreach (var (table, rows) in before)
            Assert.AreSequenceEqual(rows, after[table], "Tool calls changed table " + table + ".");
    }

    /// <summary>
    ///     Every row of the store file except what a read writes by design: the read_snapshots cache, and the receipt
    ///     frontier (signal_watermarks.watermark_unix_nano) that each read raises to its own time.
    /// </summary>
    private static SortedDictionary<string, string[]> Telemetry(string path)
    {
        return StoreDump.Read(path, "read_snapshots", "signal_watermarks.watermark_unix_nano");
    }

    [TestMethod]
    public void Store_DeleteSqlOnlyInRetention()
    {
        var root = ProcessRunner.RepositoryRoot;
        var removals = new List<string>();
        var calls = new List<string>();
        var files = s_productDirectories
            .SelectMany(directory => Directory.GetFiles(Path.Combine(root, directory), "*.cs",
                SearchOption.AllDirectories))
            .Where(static path => !path.Contains("/obj/", StringComparison.Ordinal) &&
                                  !path.Contains("/bin/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.IsGreaterThan(50, files.Length);
        foreach (var path in files)
        {
            var file = Path.GetRelativePath(root, path).Replace('\\', '/');
            var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(path), cancellationToken: context.CancellationToken)
                .GetRoot(context.CancellationToken);
            // SQL lives in string literals: plain, raw and the text parts of interpolated strings.
            foreach (var token in syntax.DescendantTokens().Where(static token =>
                         token.Kind() is SyntaxKind.StringLiteralToken or SyntaxKind.SingleLineRawStringLiteralToken
                             or SyntaxKind.MultiLineRawStringLiteralToken or SyntaxKind.InterpolatedStringTextToken
                             or SyntaxKind.Utf8StringLiteralToken or SyntaxKind.Utf8SingleLineRawStringLiteralToken
                             or SyntaxKind.Utf8MultiLineRawStringLiteralToken))
            {
                foreach (Match statement in RowRemoval().Matches(token.ValueText))
                {
                    var verb = Whitespace().Replace(statement.Groups["verb"].Value.ToUpperInvariant(), " ");
                    var table = statement.Groups["table"].Value;
                    removals.Add($"{file} {Method(token.Parent)}: {verb}{(table.Length == 0 ? "" : " " + table)}");
                }
            }

            foreach (var invocation in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var called = invocation.Expression switch
                {
                    MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
                    IdentifierNameSyntax name => name.Identifier.ValueText,
                    _ => ""
                };
                if (called.StartsWith("DeleteExpired", StringComparison.Ordinal))
                    calls.Add($"{file} {Method(invocation)}: {called}");
            }
        }

        AssertSameEntries(s_rowRemovals, removals, "statements that remove rows");
        AssertSameEntries(s_retentionCalls, calls, "calls of the retention methods");
    }

    private static string Method(SyntaxNode? node)
    {
        return node?.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ??
               "(no method)";
    }

    private static void AssertSameEntries(string[] expected, List<string> actual, string what)
    {
        var missing = expected.Except(actual, StringComparer.Ordinal).ToArray();
        var unexpected = actual.Except(expected, StringComparer.Ordinal).ToArray();
        Assert.IsEmpty(unexpected, "Not in the reviewed list of " + what + ": " + string.Join("; ", unexpected));
        Assert.IsEmpty(missing, "The reviewed list of " + what + " names what no longer exists: " +
                                string.Join("; ", missing));
        Assert.HasCount(expected.Length, actual, "A reviewed entry of " + what + " occurs more than once.");
    }

    [GeneratedRegex("""(?<verb>\bDELETE\s+FROM|\bTRUNCATE(?:\s+TABLE)?|\bDROP\s+TABLE)\b(?:\s+IF\s+EXISTS)?\s*"?(?<table>[A-Za-z_][A-Za-z0-9_]*)?""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RowRemoval();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
