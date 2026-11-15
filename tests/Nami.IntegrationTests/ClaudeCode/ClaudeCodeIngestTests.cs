using System.Net;
using System.Text;

namespace Nami.IntegrationTests.ClaudeCode;

/// <summary>
///     Claude Code's own OTLP export, the scrubbed capture of finish-line item 6, through nami's OTLP/HTTP ingest:
///     each body is posted in arrival order as Claude Code's exporter posted it (<c>application/x-protobuf</c>) to a
///     collector composed like the engine (<see cref="CollectorHost" />), and the rows are read back from the closed
///     DuckDB file. Everything lands in project <c>default</c>: the capture names no nami project and ingest is
///     unsecured.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class ClaudeCodeIngestTests(TestContext context)
{
    private const string Project = "default";

    // Unique strings: the sentinel stands for prompt, response, file and tool content; the control goes into a key
    // nami keeps, on the same records.
    private const string Sentinel = "nami-sentinel-5b8e0c71";
    private const string Control = "nami-control-93d4a2f6";

    /// <summary>
    ///     Claude Code names each event only in the <c>event.name</c> attribute (<c>tool_result</c>) and in the body
    ///     (<c>claude_code.tool_result</c>); the OTLP <c>event_name</c> field is empty on all 95 records of both captures.
    ///     The counts are the capture's records per <c>event.name</c>. <c>plugin_loaded</c> only has to be present: a
    ///     session sends several in the same millisecond that differ only in attributes nami drops today
    ///     (<c>plugin.name</c>, <c>event.sequence</c>), so their stored identities coincide and they share one row.
    /// </summary>
    [TestMethod]
    [DataRow(ClaudeCodeCapture.SignedIn)]
    [DataRow(ClaudeCodeCapture.NotSignedIn)]
    public async Task Ingest_ClaudeCodeCapture_StoresEveryEventWithItsName(string capture)
    {
        var path = await IngestAsync(ClaudeCodeCapture.Load(capture));

        var stored = StoredRows.EventNames(path, Project);

        Assert.IsFalse(stored.TryGetValue("", out var unnamed),
            $"{unnamed} stored events have no event name. Stored: {Describe(stored)}");
        var expected = ExpectedEvents(capture);
        Assert.AreSequenceEqual(expected.Keys.Order(StringComparer.Ordinal).ToArray(), stored.Keys.ToArray(),
            Describe(stored));
        foreach (var (name, count) in expected)
            if (count is { } records) Assert.AreEqual((long)records, stored[name], name);
    }

    /// <summary>
    ///     Finish-line item 5: nami stores no prompt or response text, even when a session sends it. The sentinel goes
    ///     into every content-bearing attribute and event Claude Code can send (<see cref="ClaudeCodeContent" />), and
    ///     no column of any table may hold it afterwards. The control marker on the same records shows that each of
    ///     them was stored and that the scan reads the columns that hold their attributes.
    /// </summary>
    [TestMethod]
    public async Task Ingest_ContentInEveryClaudeCodeContentCarrier_ReachesNoStoredRow()
    {
        var planted = ClaudeCodeContent.Plant(ClaudeCodeCapture.Load(ClaudeCodeCapture.SignedIn), Sentinel, Control);
        var path = await IngestAsync(planted.Exports);

        var kept = StoredRows.ColumnsContaining(path, Control);
        Assert.AreEqual((long)planted.Logs, kept.GetValueOrDefault("logs.attributes_json"), Describe(kept));
        Assert.AreEqual((long)planted.Spans, kept.GetValueOrDefault("spans.attributes_json"), Describe(kept));
        Assert.AreEqual((long)planted.ToolOutputs, kept.GetValueOrDefault("spans.events_json"), Describe(kept));

        var leaked = StoredRows.ColumnsContaining(path, Sentinel);
        Assert.IsEmpty(leaked, "Content reached storage: " + string.Join("; ", leaked.Select(column =>
            $"{column.Key} in {column.Value} rows, for example {string.Join(" | ", StoredRows.Excerpts(path, column.Key, Sentinel))}")));
    }

    private static Dictionary<string, int?> ExpectedEvents(string capture)
    {
        return capture switch
        {
            ClaudeCodeCapture.SignedIn => new Dictionary<string, int?>
            {
                ["api_request"] = 17, ["assistant_response"] = 7, ["managed_settings_resolved"] = 3,
                ["plugin_loaded"] = null, ["subagent_completed"] = 1, ["tool_decision"] = 16, ["tool_result"] = 16,
                ["user_prompt"] = 4
            },
            ClaudeCodeCapture.NotSignedIn => new Dictionary<string, int?>
            {
                ["api_error"] = 3, ["managed_settings_resolved"] = 2, ["plugin_loaded"] = null, ["user_prompt"] = 2
            },
            _ => throw new ArgumentOutOfRangeException(nameof(capture), capture, "Unknown capture.")
        };
    }

    /// <summary>Posts every body to a fresh collector, stops it, and returns its closed DuckDB file.</summary>
    private async Task<string> IngestAsync(IReadOnlyList<ClaudeCodeExport> exports)
    {
        var cancellationToken = context.CancellationToken;
        var directory = context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");
        await using var host = await CollectorHost.StartAsync(directory, cancellationToken);
        foreach (var export in exports)
        {
            using var response = await OtlpHttp.PostAsync(host.Endpoint, export.Payload, cancellationToken,
                export.Signal);
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, Encoding.UTF8.GetString(body));
            Assert.AreEqual(0L, Signals.PartialSuccess(export.Signal, body)?.Rejected ?? 0L,
                Signals.PartialSuccess(export.Signal, body)?.Message);
        }

        await host.StopAsync(cancellationToken);
        return host.DataPath;
    }

    private static string Describe(IReadOnlyDictionary<string, long> counts)
    {
        return string.Join(", ", counts.Select(static count => $"{count.Key}={count.Value}"));
    }
}
