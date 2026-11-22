using Nami.Telemetry;
using Nami.Tools;

namespace Nami.Hosting;

/// <summary>What a collector is made of beyond its ingest path.</summary>
internal enum CollectorParts
{
    /// <summary>
    ///     Ingest alone, for a collector inside a test process: no tool service, because no engine protocol calls it,
    ///     and no export of nami's own telemetry, which would add the collector's own rows to the store.
    /// </summary>
    IngestOnly,

    /// <summary>The engine process: ingest, the tool service and the export of nami's own telemetry.</summary>
    Engine
}

internal static class CollectorComposition
{
    /// <summary>
    ///     Composes the collector on <paramref name="builder" /> and builds it: the services, the Kestrel listeners,
    ///     the middleware and the OTLP endpoints. <c>Program.cs</c> and the collectors the tests start all come from
    ///     this one method, so a step added here reaches each of them.
    /// </summary>
    /// <param name="builder">The host builder, with the collector's configuration.</param>
    /// <param name="parts">What the collector has beyond ingest.</param>
    /// <param name="replaceServices">
    ///     Registrations made after the collector's own and before the host is built. The last registration of a
    ///     service is the one resolved, so a test puts a store on its own clock here.
    /// </param>
    public static WebApplication BuildNamiCollector(this WebApplicationBuilder builder,
        CollectorParts parts = CollectorParts.Engine, Action<IServiceCollection>? replaceServices = null)
    {
        builder.Services.AddNamiCollectorCore(builder.Configuration);
        builder.Services.AddNamiCollectorStorage(builder.Configuration);
        builder.Services.AddNamiCollectorAuth(builder.Configuration);
        builder.Services.ConfigureNamiCollectorKestrel();
        builder.Services.HoldAspNetCoreLogAtWarning();
        if (parts == CollectorParts.Engine)
        {
            builder.Services.AddSingleton<NamiToolService>();
            builder.Services.AddNamiSelfTelemetry(builder.Configuration);
        }
        else
        {
            // The API-key middleware takes the credential of nami's own telemetry; without the export it is
            // registered alone and no request carries it.
            builder.Services.AddSingleton(new SelfTelemetryCredential());
        }

        replaceServices?.Invoke(builder.Services);
        var app = builder.Build();
        app.UseNamiCollectorMiddleware();
        app.MapNamiCollectorEndpoints();
        return app;
    }
}
