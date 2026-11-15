using System.Globalization;
using System.Text.Json;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Nami.ApiKeys;
using Nami.Collector.Storage;
using Nami.Tests.Infrastructure;
using Nami.Tools;

namespace Nami.IntegrationTests.Fuzz;

/// <summary>
///     Fuzzing of what agents and telemetry senders can send nami: OTLP exports into the collector, tool arguments into
///     the read tools, and the frames the MCP host forwards to the engine. Each test is a short smoke run with a fixed
///     seed; <see cref="FuzzSettings" /> names the variables for a longer round.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class IngestFuzzTests(TestContext context)
{
    private static readonly DateTimeOffset s_now = DateTimeOffset.Parse("2026-11-16T00:00:00Z", CultureInfo.InvariantCulture);

    [TestMethod]
    public async Task OtlpExports_Mutated_NeverCrashNorStoreARecordThatBreaksARule()
    {
        var settings = FuzzSettings.Read(48);
        var clock = new FakeTimeProvider(s_now);
        var nowNanos = TestData.Nanos(s_now);
        var seeds = OtlpSeeds.Load(nowNanos, settings.Corpus);
        var random = new Random(settings.Seed);
        var mutator = new OtlpMutator(random, nowNanos);
        var oracle = new OtlpOracle(clock);
        var report = new FuzzReport("otlp", settings, context);
        while (report.Continue)
            await RunOtlpCasesAsync(seeds, random, mutator, oracle, clock, settings, report);

        report.AssertClean();
    }

    /// <summary>Up to 24 cases into one store; a fresh store keeps reads over the snapshot small.</summary>
    private async Task RunOtlpCasesAsync(IReadOnlyList<OtlpSeed> seeds, Random random, OtlpMutator mutator,
        OtlpOracle oracle, TimeProvider clock, FuzzSettings settings, FuzzReport report)
    {
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        for (var i = 0; i < 24 && report.Continue; i++)
        {
            var input = mutator.Next(seeds[random.Next(seeds.Count)]);
            var note = $"{input.Signal} {input.ContentType} {input.ContentEncoding} {input.Body.Length} bytes: {input.Note}";
            report.Begin(input.Body, note);
            var findings = await report.MeasureAsync(() => oracle.RunAsync(input, store, context.CancellationToken),
                input.Body.Length);
            if (settings.Output is not null)
            {
                foreach (var finding in findings.Where(report.IsNew)
                             .Where(static f => f.Stage is not ("slow" or "allocation" or "hang")))
                    await MinimizeAsync(oracle, clock, input, finding, report);
            }

            report.Record(findings, input.Body, note);
        }
    }

    [TestMethod]
    public async Task ToolArguments_Mutated_AnswerABoundedResultOrRefusal()
    {
        var settings = FuzzSettings.Read(160);
        var clock = new FakeTimeProvider(s_now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        await SeedAsync(store, clock);
        var service = new NamiToolService(store);
        var (traceIds, cursors, snapshots) = await HarvestAsync(service);
        var random = new Random(settings.Seed);
        var mutator = new ToolInputMutator(random);
        var report = new FuzzReport("tools", settings, context);
        while (report.Continue)
        {
            var input = mutator.Next(traceIds, cursors, snapshots);
            var body = System.Text.Encoding.UTF8.GetBytes(input.Json);
            var note = $"{input.Tool} {input.Json[..Math.Min(input.Json.Length, 300)]}: {input.Note}";
            report.Begin(body, note);
            report.Record(await report.MeasureAsync(() => ToolOracle.RunAsync(service, input, context.CancellationToken),
                body.Length), body, note);
        }

        report.AssertClean();
    }

    [TestMethod]
    public async Task EngineFrames_FromTheHost_KeepTheEngineServingEveryRequest()
    {
        var settings = FuzzSettings.Read(64);
        var clock = new FakeTimeProvider(s_now);
        await using var store = new DuckDbStore(":memory:", timeProvider: clock);
        await SeedAsync(store, clock);
        var (traceIds, cursors, snapshots) = await HarvestAsync(new NamiToolService(store));
        var services = new ServiceCollection()
            .AddSingleton<INamiStore>(store)
            .AddSingleton(Options.Create(new OtlpApiKeyOptions()))
            .AddSingleton<NamiToolService>()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        await using var provider = services.BuildServiceProvider();
        var random = new Random(settings.Seed);
        var tools = new ToolInputMutator(random);
        var frames = new EngineFrameMutator(random);
        var report = new FuzzReport("engine", settings, context);
        while (report.Continue)
            await ServeSessionAsync(provider, random, () => random.Next(10) == 0 ? frames.Health() : frames.Call(tools.Next(traceIds, cursors, snapshots)), report);

        report.AssertClean();
    }

    /// <summary>One engine for up to 16 frames; an engine that stopped is replaced by the next session.</summary>
    private async Task ServeSessionAsync(IServiceProvider provider, Random random, Func<EngineFrame> next, FuzzReport report)
    {
        await using var session = new EngineSession(provider);
        var ready = await session.ReadLineAsync(TimeSpan.FromSeconds(30), context.CancellationToken);
        Assert.IsNotNull(ready, "The engine did not report ready.");
        for (var served = 0; served < 16 && report.Continue && !session.Run.IsCompleted; served++)
        {
            var frame = next();
            var body = System.Text.Encoding.UTF8.GetBytes(frame.Text + "\n");
            report.Begin(body, frame.Note);
            report.Record(await report.MeasureAsync(() => ServeAsync(session, frame, random), body.Length), body, frame.Note);
        }
    }

    private async Task MinimizeAsync(OtlpOracle oracle, TimeProvider clock, OtlpCase input, FuzzFinding finding, FuzzReport report)
    {
        var minimized = await OtlpMinimizer.MinimizeAsync(input, async candidate =>
        {
            await using var scratch = new DuckDbStore(":memory:", timeProvider: clock);
            var again = await oracle.RunAsync(candidate, scratch, context.CancellationToken);
            return again.Any(f => f.Signature == finding.Signature);
        }, 150);
        string description;
        try
        {
            var message = new OtlpSeed(input.Signal, "minimized", minimized.Body).Parse();
            description = OtlpMutator.OtlpJson(message, true);
        }
        catch (Exception error) when (error is InvalidProtocolBufferException or InvalidOperationException)
        {
            description = Convert.ToHexString(minimized.Body);
        }

        report.SaveMinimized(finding, minimized.Body, $"{minimized.ContentType} {minimized.ContentEncoding}\n{description}");
    }

    /// <summary>One frame, written in random fragments; the engine must answer its id once and keep serving.</summary>
    private async Task<List<FuzzFinding>> ServeAsync(EngineSession session, EngineFrame frame, Random random)
    {
        var findings = new List<FuzzFinding>();
        await session.WriteAsync(frame.Text + "\n", random, context.CancellationToken);
        var line = await session.ReadLineAsync(TimeSpan.FromSeconds(20), context.CancellationToken);
        if (line is null)
        {
            if (session.Run.IsCompleted)
            {
                findings.Add(session.Run.Exception?.InnerException is { } error
                    ? OtlpOracle.Finding("engine", error, "the engine stopped on a frame the host sends; every tool call fails until it restarts")
                    : new FuzzFinding("engine", "engine:stopped", "the engine stopped without an error"));
            }
            else
            {
                findings.Add(new FuzzFinding("engine", "engine:no-reply", "no reply within 20 s"));
            }

            return findings;
        }

        using var reply = JsonDocument.Parse(line);
        var root = reply.RootElement;
        if (root.GetProperty("id").GetString() != frame.Id)
            findings.Add(new FuzzFinding("engine", "engine:id", $"reply for {root.GetProperty("id").GetString()} to {frame.Id}"));
        if (root.GetProperty("revision").GetString() != Nami.Api.Contracts.ContractRevision.Value)
            findings.Add(new FuzzFinding("engine", "engine:revision", line[..Math.Min(200, line.Length)]));
        if (root.GetProperty("is_error").GetBoolean() &&
            root.GetProperty("result").TryGetProperty("code", out var code) && code.GetString() == "internal_error")
            findings.Add(new FuzzFinding("engine", "engine:internal_error", "a host frame answered internal_error"));
        return findings;
    }

    private async Task SeedAsync(DuckDbStore store, TimeProvider clock)
    {
        var oracle = new OtlpOracle(clock);
        foreach (var seed in OtlpSeeds.Load(TestData.Nanos(s_now), null, false))
        {
            var message = seed.Parse();
            var findings = await oracle.RunAsync(OtlpMutator.Encode(seed.Signal, message, false), store, context.CancellationToken);
            Assert.IsEmpty(findings, string.Join("\n", findings.Select(static finding => finding.Detail)));
        }
    }

    private async Task<(List<string> TraceIds, List<string> Cursors, List<string> Snapshots)> HarvestAsync(NamiToolService service)
    {
        List<string> traceIds = [], cursors = [], snapshots = [];
        foreach (var tool in ToolInputMutator.Tools)
        {
            var input = tool switch
            {
                "traces_get" => "{\"since\":\"1970-01-01T00:00:00Z\",\"trace_id\":\"0af7651916cd43dd8448eb211c80319c\",\"max_bytes\":1024,\"fields\":[\"spans\"]}",
                "logs_search" => "{\"since\":\"1970-01-01T00:00:00Z\",\"query\":\"e\",\"max_bytes\":1024}",
                _ => "{\"since\":\"1970-01-01T00:00:00Z\",\"max_bytes\":1024}"
            };
            using var document = JsonDocument.Parse(input);
            var result = await service.InvokeAsync(tool, document.RootElement, "default", context.CancellationToken);
            if (result.Content.TryGetProperty("snapshot_id", out var snapshot)) snapshots.Add(snapshot.GetString()!);
            if (result.Content.TryGetProperty("next_cursor", out var cursor) && cursor.ValueKind == JsonValueKind.String)
                cursors.Add(cursor.GetString()!);
        }

        using var everything = JsonDocument.Parse("{\"since\":\"1970-01-01T00:00:00Z\"}");
        var all = await service.InvokeAsync("traces_slowest", everything.RootElement, "default", context.CancellationToken);
        if (all.Content.TryGetProperty("traces", out var traces))
            traceIds.AddRange(traces.EnumerateArray().Select(static trace => trace.GetProperty("trace_id").GetString()!));
        return (traceIds, cursors, snapshots);
    }
}
