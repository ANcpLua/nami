using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using Nami.Instrumentation.ErrorCapture;
using ContractHealthCheckEntry = Nami.Api.Contracts.Health.HealthCheckEntry;
using ContractHealthReport = Nami.Api.Contracts.Health.HealthReport;
using ContractHealthStatus = Nami.Api.Contracts.Health.HealthStatus;

namespace Nami.Instrumentation.Instrumentation;

public static class NamiServiceDefaultsExtensions
{
    public static WebApplication MapNamiEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.Services.GetService<NamiOptions>() ?? new NamiOptions();

        app.MapHealthChecks(NamiEndpoints.Health,
            new HealthCheckOptions
            {
                Predicate = static check => check.Tags.Contains(NamiEndpoints.ReadyTag),
                ResponseWriter = WriteHealthReportJsonAsync
            });

        app.MapHealthChecks(NamiEndpoints.Alive,
            new HealthCheckOptions
            {
                Predicate = static check => check.Tags.Contains(NamiEndpoints.LiveTag),
                ResponseWriter = WriteHealthReportJsonAsync
            });

        app.UseMiddleware<ExceptionCaptureMiddleware>();

        if (options.EnableOpenApi)
        {
            app.MapOpenApi().CacheOutput();
        }

        return app;
    }

    private static Task WriteHealthReportJsonAsync(HttpContext context, HealthReport report)
    {
        var contract = new ContractHealthReport
        {
            Status = ToContractStatus(report.Status),
            ContractRevision = Api.Contracts.ContractRevision.Value,
            TotalDurationMs = report.TotalDuration.TotalMilliseconds,
            Entries = report.Entries.ToDictionary(
                static pair => pair.Key,
                static pair => new ContractHealthCheckEntry
                {
                    Status = ToContractStatus(pair.Value.Status),
                    Description = pair.Value.Description,
                    DurationMs = pair.Value.Duration.TotalMilliseconds
                },
                StringComparer.Ordinal)
        };

        context.Response.ContentType = "application/json; charset=utf-8";
        return Api.Contracts.ContractJson.SerializeAsync(context.Response.Body, contract, context.RequestAborted);
    }

    private static ContractHealthStatus ToContractStatus(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => ContractHealthStatus.Healthy,
        HealthStatus.Degraded => ContractHealthStatus.Degraded,
        _ => ContractHealthStatus.Unhealthy
    };


    private static void ConfigureServiceProvider<TBuilder>(TBuilder builder)
        where TBuilder : IHostApplicationBuilder =>
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true, ValidateScopes = true
        }));

    private static void ConfigureKestrel(IServiceCollection services) =>
        services.Configure<KestrelServerOptions>(static options =>
        {
            options.AddServerHeader = false;
        });

    private static void ConfigureJson(IServiceCollection services)
    {
        static void Json(JsonSerializerOptions json)
        {
            json.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            json.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
            json.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            json.RespectNullableAnnotations = true;
            json.RespectRequiredConstructorParameters = true;
        }

        services.Configure<JsonOptions>(static opt => Json(opt.JsonSerializerOptions));
        services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(static opt => Json(opt.SerializerOptions));
    }

    // Composition of the producer pipeline belongs to Nami.Telemetry.Hosting, which owns the OTel wiring, the
    // instrumentation inventory, the version-pinned external GenAI sources, and collector
    // discovery. This process is a customer of that package like any other application; the only
    // things it adds are the facts Nami.Telemetry.Hosting cannot know about it.
    internal static void ConfigureNamiTelemetry<TBuilder>(TBuilder builder, NamiOptions options)
        where TBuilder : IHostApplicationBuilder
    {
        // OTEL_SERVICE_NAME wins over the assembly name so two processes of the same project (e.g. a
        // collector plus its dedicated diagnostics collector) keep distinct service identities.
        var serviceName = builder.Configuration["OTEL_SERVICE_NAME"] is { Length: > 0 } configuredName
            ? configuredName
            : builder.Environment.ApplicationName;

        builder.AddNami(nami =>
        {
            nami.ServiceName = serviceName;
            nami.ServiceVersion = BuildVersion.ProductVersion;
            nami.EnableCollectorDiscovery = options.EnableAutoDiscovery;

            // This process is itself an OTLP destination. "No endpoint configured" must mean "do
            // not export", never the exporter's localhost default — that default is our own ingest
            // port, and exporting there would feed our output back into our input.
            nami.RequireConfiguredEndpoint = true;

            nami.AdditionalSources.Add(serviceName);

            // OTEL_SERVICE_NAME may rename the service, but apps following the assembly-name
            // ActivitySource convention must stay subscribed.
            if (!string.Equals(serviceName, builder.Environment.ApplicationName, StringComparison.Ordinal))
                nami.AdditionalSources.Add(builder.Environment.ApplicationName);

            foreach (var source in options.AdditionalActivitySources)
                nami.AdditionalSources.Add(source);

            foreach (var meter in options.AdditionalMeters)
                nami.AdditionalMeters.Add(meter);

            // Which endpoints are noise is this application's policy, not the producer's.
            nami.ConfigureTracing = static tracing => tracing.AddProcessor(new HealthProbeSpanFilter());
        });
    }

    private static void ConfigureHealthChecks<TBuilder>(TBuilder builder)
        where TBuilder : IHostApplicationBuilder =>
        builder.Services.AddHealthChecks()
            .AddCheck("self", static () => HealthCheckResult.Healthy(), [NamiEndpoints.LiveTag])
            .AddCheck("ready", static () => HealthCheckResult.Healthy(), [NamiEndpoints.ReadyTag]);

    private static void ConfigureHttpClients<TBuilder>(TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(static http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
            http.ConfigureHttpClient(static (sp, client) =>
            {
                var env = sp.GetRequiredService<IHostEnvironment>();
                client.DefaultRequestHeaders.UserAgent.Add(
                    new ProductInfoHeaderValue(env.ApplicationName, BuildVersion.ProductVersion));
            });
        });
    }

    extension<TBuilder>(TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        public TBuilder UseNami(Action<NamiOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);

            if (builder.Services.Any(static d => d.ServiceType == typeof(NamiServiceDefaultsMarker)))
                return builder;
            builder.Services.AddSingleton<NamiServiceDefaultsMarker>();

            var options = new NamiOptions();
            configure?.Invoke(options);

            builder.Services.TryAddSingleton(options);

            ConfigureServiceProvider(builder);
            ConfigureKestrel(builder.Services);
            ConfigureJson(builder.Services);

            ConfigureNamiTelemetry(builder, options);
            ConfigureHealthChecks(builder);
            ConfigureHttpClients(builder);

            // OpenAPI registers EndpointMetadataApiDescriptionProvider, which needs EndpointDataSource —
            // a service only the web host (WebApplicationBuilder) provides. Skip it on generic hosts
            // (console/worker apps) so ValidateOnBuild doesn't throw.
            if (options.EnableOpenApi && builder is WebApplicationBuilder)
                builder.Services.AddOpenApi();

            builder.Services.AddValidation();
            builder.Services.AddProblemDetails();

            builder.Services.AddHostedService<ExceptionHookRegistrar>();

            return builder;
        }
    }
}

public sealed class NamiOptions
{
    public bool EnableOpenApi { get; set; } = true;

    public bool EnableAutoDiscovery { get; set; } = true;

    public List<string> AdditionalActivitySources { get; } = [];

    public List<string> AdditionalMeters { get; } = [];
}

internal sealed class NamiServiceDefaultsMarker;
