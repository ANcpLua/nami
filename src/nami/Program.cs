using Nami.Hosting;
using Nami.Telemetry;
using Nami.Tools;

var builder = WebApplication.CreateSlimBuilder(args.Where(static arg => arg != "--engine").ToArray());
builder.Logging.ClearProviders();
builder.Logging.AddConsole(static options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddNamiCollectorCore(builder.Configuration);
builder.Services.AddNamiCollectorStorage(builder.Configuration);
builder.Services.AddNamiCollectorAuth(builder.Configuration, builder.Environment);
builder.Services.ConfigureNamiCollectorKestrel();
builder.Services.AddSingleton<NamiToolService>();
builder.Services.AddNamiSelfTelemetry(builder.Configuration);
await using var app = builder.Build();
app.UseNamiCollectorMiddleware();
app.MapNamiCollectorEndpoints();
await app.StartAsync();
try { await EngineProtocol.RunAsync(app.Services, app.Lifetime.ApplicationStopping); }
finally { await app.StopAsync(); }
