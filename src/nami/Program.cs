using Nami.Collector.Hosting;
using Nami.Collector.Mcp;
using Nami.Collector.Storage;
using Nami.Collector.Telemetry;
using Nami.Tools;
using Nami.Api.Contracts;
using Nami.Api.Contracts.Health;

var stdio = args.Contains("--stdio", StringComparer.Ordinal);
var builder = WebApplication.CreateSlimBuilder(args.Where(a => a != "--stdio").ToArray());
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddNamiCollectorCore(builder.Configuration);
builder.Services.AddNamiCollectorStorage(builder.Configuration);
builder.Services.AddNamiCollectorAuth(builder.Configuration, builder.Environment);
builder.Services.ConfigureNamiCollectorKestrel();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<NamiToolService>();
builder.Services.AddNamiMcp(stdio);
builder.Services.AddNamiSelfTelemetry(builder.Configuration);

var app = builder.Build();
_ = app.Services.GetRequiredService<INamiStore>();
app.UseNamiCollectorMiddleware();
app.MapNamiCollectorEndpoints();
app.MapGet("/health", static async (INamiStore store, CancellationToken ct) =>
{
    var started = Stopwatch.GetTimestamp();
    var healthy = true;
    try { await store.GetStorageStatsAsync(ProjectScope.DefaultProjectId, ct: ct).ConfigureAwait(false); }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
    catch (Exception) { healthy = false; }
    var duration = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    var status = healthy ? HealthStatus.Healthy : HealthStatus.Unhealthy;
    var report = new HealthReport
    {
        Status = status, TotalDurationMs = duration, ContractRevision = ContractRevision.Value,
        Entries = new Dictionary<string, HealthCheckEntry>
        {
            ["duckdb"] = new() { Status = status, DurationMs = duration,
                Description = healthy ? null : "Storage is unavailable." }
        }
    };
    return Results.Text(ContractJson.Serialize(report).GetRawText(), "application/json", statusCode: healthy ? 200 : 503);
});
if (!stdio) app.MapNamiMcp();
await app.RunAsync().ConfigureAwait(false);

public partial class Program;
