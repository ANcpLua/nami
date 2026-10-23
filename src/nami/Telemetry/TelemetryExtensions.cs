using Microsoft.Extensions.Diagnostics.ExceptionSummarization;

namespace Nami.Collector.Telemetry;

internal static class TelemetryExtensions
{
    public static void AddNamiTelemetry(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        services.AddRedaction(static builder => builder.AddNamiRedactors());

        services.AddExceptionSummarizer(static builder =>
        {
            builder.AddHttpProvider();
        });

        services.AddLogging(static logging =>
        {
            logging.EnableEnrichment(static options =>
            {
                options.CaptureStackTraces = false;
                options.IncludeExceptionMessage = false;
                options.MaxStackTraceLength = 2048;
            });

            logging.EnableRedaction();
        });

        services.AddLogEnricher<NamiRequestEnricher>();

        services.AddApplicationLogEnricher(static options =>
        {
            options.ApplicationName = true;
            options.BuildVersion = true;
            options.EnvironmentName = true;
        });

        services.RegisterCheckpointNames(
            NamiLatencyNames.Checkpoints.DbQuery,
            NamiLatencyNames.Checkpoints.SpanIngest,
            NamiLatencyNames.Checkpoints.SpanStore,
            NamiLatencyNames.Checkpoints.SessionQuery,
            NamiLatencyNames.Checkpoints.GenAiExtract);

        services.RegisterMeasureNames(
            NamiLatencyNames.Measures.IngestionDuration,
            NamiLatencyNames.Measures.QueryDuration,
            NamiLatencyNames.Measures.StorageDuration);

        services.RegisterTagNames(NamiLatencyNames.Tags.SpanCount);

        services.AddRequestLatencyTelemetry();
        services.AddLatencyContext();

        services.AddAsyncState();
    }

    public static void UseNamiTelemetry(this IApplicationBuilder app)
    {
        var env = app.ApplicationServices.GetRequiredService<IWebHostEnvironment>();

        if (!env.IsEnvironment("Testing"))
        {
            app.UseRequestLatencyTelemetry();
        }
    }
}

internal static class NamiLoggingBuilderExtensions
{
    public static ILoggingBuilder AddNamiLogging(this ILoggingBuilder builder, IHostEnvironment environment)
    {
        builder.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.AddFilter("Microsoft.Hosting", LogLevel.Warning);
        builder.AddFilter("System.Net.Http", LogLevel.Warning);
        builder.AddFilter("Grpc", LogLevel.Warning);

        if (environment.IsDevelopment())
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddFilter("nami", LogLevel.Debug);
            builder.AddFilter("Microsoft.AspNetCore.Routing", LogLevel.Debug);
        }
        else
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddFilter("nami", LogLevel.Information);
        }

        return builder;
    }
}
