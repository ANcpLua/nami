using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nami.Instrumentation.Instrumentation;

namespace Nami.Instrumentation.ErrorCapture;

public sealed class ExceptionCaptureMiddleware(RequestDelegate next, ILogger<ExceptionCaptureMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RecordException(ex);
            throw;
        }
    }

    private void RecordException(Exception ex)
    {
        if (Activity.Current is not { } activity) return;

        ActivityExceptionTelemetry.Record(activity, ex);

        logger.LogExceptionCaptured(ex);
    }
}

public static class GlobalExceptionHooks
{
    private const string LoggerCategory = "Nami.Instrumentation.ErrorCapture";

    private static int s_registered;

    public static void Register(ILoggerFactory loggerFactory)
    {
        if (Interlocked.Exchange(ref s_registered, 1) is not 0) return;

        var logger = loggerFactory.CreateLogger(LoggerCategory);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                logger.LogUnhandledException(ex, args.IsTerminating);
                RecordGlobalException(ex, "AppDomain.UnhandledException");
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            logger.LogUnobservedTaskException(args.Exception);
            RecordGlobalException(args.Exception, "TaskScheduler.UnobservedTaskException");
        };
    }

    internal static void RecordGlobalException(Exception ex, string source)
    {
        if (Activity.Current is { } activity)
        {
            TagActivity(activity, ex, source);
            return;
        }

        // No span to annotate: the failure gets a span of its own on the application scope, which
        // AddNami subscribes. It ends here, so it is exported; a span nobody stops never leaves the process.
        using var fallback = Spans.Work("UnhandledException");
        if (fallback is not null)
            TagActivity(fallback, ex, source);
    }

    private static void TagActivity(Activity activity, Exception ex, string source)
    {
        ActivityExceptionTelemetry.ApplyError(activity, ex);

        var tags = ActivityExceptionTelemetry.CreateTags(ex);
        tags.Add(ActivityExceptionTelemetry.ExceptionSource, source);
        activity.AddEvent(new ActivityEvent("exception", tags: tags));
    }
}

internal sealed class ExceptionHookRegistrar(ILoggerFactory loggerFactory) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        GlobalExceptionHooks.Register(loggerFactory);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal static partial class ExceptionCaptureLogMessages
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "[nami] Captured exception")]
    public static partial void LogExceptionCaptured(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Critical, Message = "[nami] Unhandled exception (IsTerminating={IsTerminating})")]
    public static partial void LogUnhandledException(this ILogger logger, Exception exception, bool isTerminating);

    [LoggerMessage(Level = LogLevel.Error, Message = "[nami] Unobserved task exception")]
    public static partial void LogUnobservedTaskException(this ILogger logger, Exception? exception);
}
