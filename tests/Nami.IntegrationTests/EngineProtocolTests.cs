using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Nami.Api.Contracts;
using Nami.ApiKeys;
using Nami.Collector.Storage;
using Nami.Tools;

namespace Nami.IntegrationTests;

/// <summary>
///     The engine's frame reader over in-memory pipes, fed the frames <c>engine.ts</c> writes. The host forwards an
///     agent's tool arguments and <c>_meta</c> trace context unchanged, so whatever an agent can send must leave the
///     engine answering: a request it cannot decode is refused alone.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class EngineProtocolTests(TestContext context)
{
    private static readonly DateTimeOffset s_now = DateTimeOffset.Parse("2026-11-16T00:00:00Z", CultureInfo.InvariantCulture);

    // JSON.stringify(EngineCommandJson.toJson(request)) + "\n", as engine.ts encodes a call.
    private static string Call(string id, string tool, string input, string traceparent = "null",
        string tracestate = "null") =>
        $"{{\"id\":\"{id}\",\"revision\":\"{ContractRevision.Value}\",\"method\":\"call\",\"name\":\"{tool}\"," +
        $"\"input\":{input},\"credential\":null,\"traceparent\":{traceparent},\"tracestate\":{tracestate}}}\n";

    private static string Health(string id) =>
        $"{{\"id\":\"{id}\",\"revision\":\"{ContractRevision.Value}\",\"method\":\"health\"}}\n";

    [TestMethod]
    [DataRow(62, DisplayName = "Inside the reader's default depth")]
    [DataRow(63, DisplayName = "One level past the default depth")]
    [DataRow(4000, DisplayName = "Far past the default depth")]
    public Task HostFrame_ArgumentsNestedPastTheReaderDepth_RefusedAloneAndTheEngineKeepsServing(int depth) =>
        WithEngineAsync(async exchange =>
        {
            // Arguments the MCP's schema refused are forwarded unchanged, so the engine can return its own refusal.
            var arguments = $"{{\"fields\":{new string('[', depth)}{new string(']', depth)}}}";
            var reply = await exchange(Call("1", "sources_status", arguments));
            Assert.AreEqual("1", reply.GetProperty("id").GetString());
            Assert.IsTrue(reply.GetProperty("is_error").GetBoolean());
            Assert.AreEqual("invalid_argument", reply.GetProperty("result").GetProperty("code").GetString());

            var health = await exchange(Health("2"));
            Assert.IsFalse(health.GetProperty("is_error").GetBoolean());
        });

    [TestMethod]
    [DataRow("\"\\ud800\"", "null", true, DisplayName = "Traceparent with a lone high surrogate")]
    [DataRow("\"00-\\udfff-01\"", "null", true, DisplayName = "Traceparent with a lone low surrogate")]
    [DataRow("null", "\"a=\\ud800\"", true, DisplayName = "Tracestate with a lone surrogate")]
    [DataRow("\"\\ud83d\\ude00\"", "null", false, DisplayName = "Traceparent with a surrogate pair")]
    public Task HostFrame_TraceContextWithAnUnpairedSurrogate_RefusedAloneAndTheEngineKeepsServing(string traceparent,
        string tracestate, bool refused) =>
        WithEngineAsync(async exchange =>
        {
            // JSON.stringify writes an unpaired surrogate of an agent's _meta trace context as an escape.
            var reply = await exchange(Call("1", "sources_status", "{}", traceparent, tracestate));
            Assert.AreEqual("1", reply.GetProperty("id").GetString());
            Assert.AreEqual(refused, reply.GetProperty("is_error").GetBoolean(), reply.GetRawText());
            if (refused)
                Assert.AreEqual("invalid_argument", reply.GetProperty("result").GetProperty("code").GetString());

            var health = await exchange(Health("2"));
            Assert.IsFalse(health.GetProperty("is_error").GetBoolean());
        });

    [TestMethod]
    [DataRow("{\"service\":\"a\\ud800\"}", "service", DisplayName = "Argument value")]
    [DataRow("{\"\\udc00\":1}", "", DisplayName = "Argument name")]
    public Task HostFrame_ArgumentWithAnUnpairedSurrogate_RefusedAsAnInvalidArgument(string arguments, string field) =>
        WithEngineAsync(async exchange =>
        {
            var reply = await exchange(Call("1", "sources_status", arguments));
            Assert.IsTrue(reply.GetProperty("is_error").GetBoolean());
            Assert.AreEqual("invalid_argument", reply.GetProperty("result").GetProperty("code").GetString(),
                reply.GetRawText());
            Assert.AreEqual(field, reply.GetProperty("result").GetProperty("field").GetString());
        });

    /// <summary>Serves one engine over pipes for the test body, which exchanges frames for replies.</summary>
    private async Task WithEngineAsync(Func<Func<string, Task<JsonElement>>, Task> body)
    {
        var clock = new FakeTimeProvider(s_now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        await using var provider = new ServiceCollection()
            .AddSingleton<INamiStore>(store)
            .AddSingleton(Options.Create(new OtlpApiKeyOptions()))
            .AddSingleton<NamiToolService>()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();
        await using var session = new EngineSession(provider);
        Assert.IsNotNull(await session.ReadLineAsync(TimeSpan.FromSeconds(30), context.CancellationToken),
            "The engine did not report ready.");
        await body(async frame =>
        {
            await session.WriteAsync(frame, new Random(1), context.CancellationToken);
            var line = await session.ReadLineAsync(TimeSpan.FromSeconds(30), context.CancellationToken);
            if (line is null)
            {
                Assert.IsTrue(session.Run.IsCompleted, "The engine neither answered nor stopped.");
                Assert.Fail("The engine stopped instead of answering: " + session.Run.Exception?.InnerException);
            }

            using var reply = JsonDocument.Parse(line);
            return reply.RootElement.Clone();
        });
    }
}
