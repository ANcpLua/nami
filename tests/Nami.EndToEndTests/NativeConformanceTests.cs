using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Nami.Tests.Infrastructure;

namespace Nami.EndToEndTests;

[TestClass]
[TestCategory("EndToEnd")]
public sealed class NativeConformanceTests(TestContext context)
{
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task NativeHost_OtlpAndStdio_PreservesAllToolsAndSnapshotsAcrossRestart()
    {
        var directory = context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");
        string snapshotId;
        string stableResult;
        await using (var host = await McpHarness.StartAsync(TestData.HostBinary, true, directory, context.CancellationToken))
        {
            var nanos = TestData.Nanos(TimeProvider.System.GetUtcNow().AddSeconds(-3));
            foreach (var signal in new[] { "traces", "logs", "metrics" })
            {
                var json = signal switch { "traces" => OtlpData.Traces(nanos), "logs" => OtlpData.Logs(nanos), _ => OtlpData.Metrics(nanos) };
                using var response = await host.SendJsonAsync(signal, json, context.CancellationToken);
                Assert.IsTrue(response.IsSuccessStatusCode, $"OTLP {signal}: {response.StatusCode}");
            }
            var tools = await host.Client.ListToolsAsync(cancellationToken: context.CancellationToken);
            Assert.HasCount(6, tools);
            foreach (var tool in tools)
            {
                var input = tool.Name switch
                {
                    "traces_get" => new JsonObject { ["trace_id"] = 1.ToString("x32", CultureInfo.InvariantCulture) }.ToJsonString(),
                    "logs_search" => "{\"query\":\"order\"}",
                    _ => "{}"
                };
                var result = await host.CallAsync(tool.Name, input, context.CancellationToken);
                Assert.IsFalse(result.IsError == true, tool.Name);
                var content = Structured(result);
                Assert.IsTrue(content.TryGetProperty("freshness", out var freshness), tool.Name);
                Assert.IsTrue(freshness.GetProperty("complete").GetBoolean(), tool.Name);
                Assert.HasCount(2, result.Content);
                Assert.AreEqual(content.GetRawText(), Assert.IsExactInstanceOfType<TextContentBlock>(result.Content[1]).Text);
                if (tool.Name == "traces_get") Assert.AreEqual(1, content.GetProperty("trace").GetProperty("span_count").GetInt32());
                if (tool.Name == "logs_search") Assert.AreEqual(1, content.GetProperty("records").GetArrayLength());
            }
            var first = Structured(await host.CallAsync("logs_search", "{\"query\":\"order\"}", context.CancellationToken));
            snapshotId = first.GetProperty("snapshot_id").GetString() ?? throw new InvalidOperationException("Snapshot ID absent");
            stableResult = first.GetRawText();
            var replay = Structured(await host.CallAsync("logs_search", Replay(snapshotId), context.CancellationToken));
            Assert.AreEqual(stableResult, replay.GetRawText());
        }
        await using var restarted = await McpHarness.StartAsync(TestData.HostBinary, true, directory, context.CancellationToken);
        var persisted = await restarted.CallAsync("logs_search", Replay(snapshotId), context.CancellationToken);
        Assert.IsFalse(persisted.IsError == true);
        Assert.AreEqual(stableResult, Structured(persisted).GetRawText());
    }

    private static string Replay(string id) => new JsonObject { ["query"] = "order", ["snapshot_id"] = id }.ToJsonString();
    private static JsonElement Structured(CallToolResult result) => (object?)result.StructuredContent is JsonElement content
        ? content : throw new InvalidOperationException("Missing structured result");
}
