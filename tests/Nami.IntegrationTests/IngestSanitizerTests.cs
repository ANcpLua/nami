using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nami.Collector.Storage;
using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

/// <summary>
///     The exception-text policy where telemetry enters: real OTLP exports over each transport, read back from the
///     store. The boundaries of the sanitizer function itself are in IngestStoreTests (Sanitizer_*).
/// </summary>
[TestClass]
[TestCategory("Integration")]
[DependsOn(typeof(IngestStoreTests), nameof(IngestStoreTests.Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
public sealed class IngestSanitizerTests(TestContext context)
{
    private const int InputLimit = 128 * 1024;
    private const int StoredLimit = 32 * 1024;
    private const string Failed = "[SANITIZATION_FAILED]";
    // Not hexadecimal, so no identifier the store generates can contain it by chance.
    private const string Token = "s3cr3t-b34r3r-t0k3n";
    private const string Credential = "Authorization: Bearer " + Token;
    private const string Redacted = "Authorization: Bearer [REDACTED]";

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    [DataRow(CollectorHost.JsonTransport, DisplayName = "Exception text over HTTP JSON")]
    [DataRow(CollectorHost.ProtobufTransport, DisplayName = "Exception text over HTTP protobuf")]
    [DataRow(CollectorHost.GrpcTransport, DisplayName = "Exception text over gRPC")]
    public async Task Ingest_ExceptionText_IsSanitizedOnEveryTransport(string transport)
    {
        var path = Path.Combine(
            context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"),
            "sanitizer.duckdb");
        var time = TestData.Nanos(DateTimeOffset.UtcNow.AddSeconds(-2));
        var tooLong = new string('x', InputLimit + 1);
        var longest = new string('y', InputLimit);

        // Span events: the first carries a message over the input limit and a credential in its stack trace,
        // the second a message exactly at the input limit.
        var traces = JsonNode.Parse(OtlpData.Traces(time, 2))!;
        var spans = traces["resourceSpans"]![0]!["scopeSpans"]![0]!["spans"]!.AsArray();
        spans[0]!["events"] = ExceptionEvent(time, tooLong, "at Checkout.Pay() " + Credential);
        spans[1]!["events"] = ExceptionEvent(time, longest, "at Checkout.Pay()");
        // Log record: the same two cases on the other attribute of each.
        var logs = JsonNode.Parse(OtlpData.Logs(time, exception: true))!;
        logs["resourceLogs"]![0]!["scopeLogs"]![0]!["logRecords"]![0]!["attributes"] = ExceptionAttributes(
            "declined " + Credential, tooLong);

        IReadOnlyList<SpanStorageRow> storedSpans;
        LogStorageRow storedLog;
        await using (var host = await CollectorHost.StartAsync(path, context.CancellationToken))
        {
            await host.SendAsync(transport, Signals.Traces, traces.ToJsonString(), context.CancellationToken);
            await host.SendAsync(transport, Signals.Logs, logs.ToJsonString(), context.CancellationToken);
            var snapshot = await host.Store.ReadSnapshotAsync("default", ct: context.CancellationToken);
            storedSpans = snapshot.Spans;
            storedLog = Assert.ContainsSingle(snapshot.Logs);
        }

        Assert.HasCount(2, storedSpans);
        var first = EventAttributes(storedSpans.Single(static span => span.SpanId.EndsWith('1')));
        Assert.AreEqual(Failed, first.GetProperty("exception.message").GetString());
        Assert.AreEqual("at Checkout.Pay() " + Redacted, first.GetProperty("exception.stacktrace").GetString());
        var second = EventAttributes(storedSpans.Single(static span => span.SpanId.EndsWith('2')));
        var bounded = second.GetProperty("exception.message").GetString()!;
        Assert.IsLessThanOrEqualTo(StoredLimit, Encoding.UTF8.GetByteCount(bounded));
        Assert.StartsWith(new string('y', 1024), bounded);
        Assert.AreEqual("at Checkout.Pay()", second.GetProperty("exception.stacktrace").GetString());
        using var logAttributes = JsonDocument.Parse(storedLog.AttributesJson!);
        Assert.AreEqual("declined " + Redacted, logAttributes.RootElement.GetProperty("exception.message").GetString());
        Assert.AreEqual(Failed, logAttributes.RootElement.GetProperty("exception.stacktrace").GetString());

        // Nothing the store holds, in any table, still carries the token, the refused text or more than the bound.
        var rows = StoreDump.Read(path).SelectMany(static table => table.Value).ToArray();
        Assert.IsNotEmpty(rows);
        Assert.IsFalse(rows.Any(static row => row.Contains(Token, StringComparison.Ordinal)), "The token was stored.");
        Assert.IsFalse(rows.Any(static row => row.Contains(new string('x', 64), StringComparison.Ordinal)),
            "Text over the input limit was stored.");
        Assert.IsFalse(rows.Any(static row => row.Contains(new string('y', StoredLimit + 1), StringComparison.Ordinal)),
            "Text at the input limit was stored beyond the persisted bound.");
    }

    private static JsonArray ExceptionAttributes(string message, string stacktrace)
    {
        return new JsonArray(OtlpData.Attribute("exception.type", "OrderFailure"),
            OtlpData.Attribute("exception.message", message), OtlpData.Attribute("exception.stacktrace", stacktrace));
    }

    private static JsonArray ExceptionEvent(ulong time, string message, string stacktrace)
    {
        return new JsonArray(new JsonObject
        {
            ["name"] = "exception", ["timeUnixNano"] = time.ToString(CultureInfo.InvariantCulture),
            ["attributes"] = ExceptionAttributes(message, stacktrace)
        });
    }

    private static JsonElement EventAttributes(SpanStorageRow span)
    {
        var stored = Assert.ContainsSingle(SpanChildStorage.DeserializeEvents(span.EventsJson)!);
        using var attributes = JsonDocument.Parse(stored.AttributesJson!);
        return attributes.RootElement.Clone();
    }
}
