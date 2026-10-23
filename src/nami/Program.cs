using Microsoft.Extensions.Options;
using Nami.Collector;
using Nami.Collector.ApiKeys;
using Nami.Collector.Hosting;
using Nami.Collector.Telemetry;
using Nami.Instrumentation.Instrumentation;

Console.WriteLine($"[nami] Process starting at {TimeProvider.System.GetUtcNow():O}");

var builder = WebApplication.CreateSlimBuilder(args);

builder.UseNami(options =>
{
    // OpenAPI is off: the product API contract is the TypeSpec in contracts/ and flows in via
    // Nami.Api.Contracts. The collector is not a contract/client-generation source.
    options.EnableOpenApi = false;
    options.EnableAutoDiscovery = false;
    options.AdditionalMeters.Add(NamiTelemetry.ServiceName);
});

builder.Services.AddNamiCollectorCore(builder.Configuration);
builder.Services.AddNamiCollectorStorage(builder.Configuration);
builder.Services.AddNamiCollectorAuth(builder.Configuration, builder.Environment);
builder.Services.AddNamiCollectorTelemetry(builder.Environment);
builder.Services.ConfigureNamiCollectorKestrel();

var app = builder.Build();
var ports = app.Services.GetRequiredService<IOptions<CollectorPortOptions>>().Value;
CollectorSelfExportGuard.ThrowIfSelfExporting(app.Configuration, ports);

app.UseNamiCollectorMiddleware();

app.MapNamiCollectorEndpoints();
CollectorHealthGuard.ThrowIfHealthSurfaceUnwired(app);

StartupBanner.Print(
    ports,
    app.Services.GetRequiredService<IOptions<OtlpCorsOptions>>().Value,
    app.Services.GetRequiredService<IOptions<OtlpApiKeyOptions>>().Value);

app.Lifetime.ApplicationStarted.Register(() =>
    Console.WriteLine($"[nami] Application started and listening on port {ports.Http}"));

await app.RunAsync().ConfigureAwait(false);

