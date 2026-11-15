using Microsoft.Extensions.Options;
using Nami.Collector.Storage;

namespace Nami.Retention;

internal sealed partial class RetentionService(
    INamiStore store,
    IOptions<RetentionOptions> configured,
    TimeProvider timeProvider,
    ILogger<RetentionService> logger) : BackgroundService
{
    private static readonly TimeSpan s_initialDelay = TimeSpan.FromSeconds(10);

    private readonly RetentionOptions _options = configured.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsEnabled)
            return;

        try
        {
            await Task.Delay(s_initialDelay, timeProvider, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogCycleFailed(logger, ex);
            }

            try
            {
                await Task.Delay(_options.Interval, timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.IsEnabled)
            return;

        var startedAt = Stopwatch.GetTimestamp();
        var fileBefore = store.GetStorageFileMetrics();
        var now = timeProvider.GetUtcNow();
        var cutoff = _options.Days > (now - DateTimeOffset.UnixEpoch).Days
            ? 0UL
            : NamiTimeConversions.ToUnixNanoUnsigned(now.AddDays(-_options.Days));
        var deleted = await store.DeleteExpiredAsync(cutoff, cancellationToken).ConfigureAwait(false);
        var duration = Stopwatch.GetElapsedTime(startedAt);
        var fileAfter = store.GetStorageFileMetrics();
        if (deleted.Logs + deleted.Spans + deleted.MetricPoints + deleted.MetricSeries > 0)
        {
            LogCycleCompleted(logger, deleted.Logs, deleted.Spans, deleted.MetricPoints, deleted.MetricSeries,
                duration.TotalMilliseconds, fileBefore.ManagedStorageBytes, fileAfter.ManagedStorageBytes);
        }
    }

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Information,
        Message =
            "Retention cycle deleted {DeletedLogRows} logs, {DeletedSpanRows} spans, {DeletedMetricPoints} metric points, and {DeletedMetricSeries} metric series in {DurationMs} ms; managed storage changed from {FileSizeBeforeBytes} to {FileSizeAfterBytes} bytes")]
    private static partial void LogCycleCompleted(
        ILogger logger,
        long deletedLogRows,
        long deletedSpanRows,
        long deletedMetricPoints,
        long deletedMetricSeries,
        double durationMs,
        long fileSizeBeforeBytes,
        long fileSizeAfterBytes);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Error, Message = "Retention cycle failed")]
    private static partial void LogCycleFailed(ILogger logger, Exception exception);
}
