using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;
using Nami.Collector.ApiKeys;
using Nami.Collector.Telemetry;

namespace Nami.Collector.Hosting;

internal static class CollectorMiddlewareExtensions
{
    public static WebApplication UseNamiCollectorMiddleware(this WebApplication app)
    {
        if (app.Services.GetRequiredService<IOptions<OtlpCorsOptions>>().Value.IsEnabled)
            app.UseMiddleware<OtlpCorsMiddleware>();

        app.UseMiddleware<CollectorApiKeyMiddleware>();

        app.UseExceptionHandler(static errorApp =>
        {
            errorApp.Run(WriteUnhandledExceptionAsync);
        });

        app.UseNamiTelemetry();

        return app;
    }

    internal static async Task WriteUnhandledExceptionAsync(HttpContext context)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Nami.Collector.ExceptionHandler");
        var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
        if (exceptionFeature?.Error is { } error)
        {
            ExceptionHandlerLog.UnhandledException(
                logger,
                error,
                HttpTelemetryNames.NormalizeMethod(context.Request.Method));
        }

        context.Response.Headers["X-Trace-Id"] = traceId;
        await ContractErrorResults.WriteInternalServerErrorAsync(
            context.Response,
            "collector.unhandled_exception").ConfigureAwait(false);
    }
}

internal static partial class ExceptionHandlerLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception on {Method}")]
    public static partial void UnhandledException(ILogger logger, Exception exception, string method);
}
