using Nami.Hosting;

var builder = WebApplication.CreateSlimBuilder([.. args.Where(static arg => arg != "--engine")]);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(static options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
await using var app = builder.BuildNamiCollector();
await app.StartAsync();
try
{
    await EngineProtocol.RunAsync(app.Services, app.Lifetime.ApplicationStopping);
}
finally
{
    await app.StopAsync();
}
