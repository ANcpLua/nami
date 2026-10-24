using Microsoft.Extensions.Options;
using Nami.Collector.Storage;

namespace Nami.Retention;

internal sealed partial class RetentionService(
    INamiStore store,
    IOptions<RetentionOptions> configured,
    TimeProvider timeProvider,
    ILogger<RetentionService> logger) : BackgroundService
{
    private const int DeleteBatchSize = 1000;

    private static readonly TimeSpan s_initialDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan s_shutdownCheckpointTimeout = TimeSpan.FromSeconds(30);

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
        long deletedLogs = 0;
        long deletedSpans = 0;
        long deletedMetricPoints = 0;
        long deletedMetricSeries = 0;
        long deletedMetadata = 0;

        try
        {
            var now = timeProvider.GetUtcNow();
            var cutoff = _options.Days > (now - DateTimeOffset.UnixEpoch).Days
                ? 0UL
                : NamiTimeConversions.ToUnixNanoUnsigned(now.AddDays(-_options.Days));

            while (true)
            {
                var deleted = await store.DeleteExpiredLogsBatchAsync(
                        cutoff,
                        DeleteBatchSize,
                        cancellationToken)
                    .ConfigureAwait(false);
                deletedLogs += deleted;
                if (deleted < DeleteBatchSize)
                    break;
            }

            while (true)
            {
                var deleted = await store.DeleteExpiredSpansBatchAsync(
                        cutoff,
                        DeleteBatchSize,
                        cancellationToken)
                    .ConfigureAwait(false);
                deletedSpans += deleted;
                if (deleted is 0)
                    break;
            }

            while (true)
            {
                var deleted = await store.DeleteExpiredMetricsBatchAsync(
                        cutoff,
                        DeleteBatchSize,
                        cancellationToken)
                    .ConfigureAwait(false);
                deletedMetricPoints += deleted.Points;
                deletedMetricSeries += deleted.Series;
                if (deleted.Points < DeleteBatchSize)
                    break;
            }

            deletedMetadata = await store.DeleteExpiredMetadataAsync(cutoff, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (deletedLogs + deletedSpans + deletedMetricPoints + deletedMetricSeries + deletedMetadata > 0)
                await CheckpointAfterDeletionAsync(cancellationToken).ConfigureAwait(false);
        }

        var duration = Stopwatch.GetElapsedTime(startedAt);
        var fileAfter = store.GetStorageFileMetrics();
        if (deletedLogs + deletedSpans + deletedMetricPoints + deletedMetricSeries > 0)
            LogCycleCompleted(logger, deletedLogs, deletedSpans, deletedMetricPoints, deletedMetricSeries,
                duration.TotalMilliseconds, fileBefore.ManagedStorageBytes, fileAfter.ManagedStorageBytes);
    }

    private async Task CheckpointAfterDeletionAsync(CancellationToken cancellationToken)
    {
        if (!cancellationToken.IsCancellationRequested)
        {
            await store.CheckpointAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        using var checkpointTimeout = new CancellationTokenSource(s_shutdownCheckpointTimeout);
        await store.CheckpointAsync(checkpointTimeout.Token).ConfigureAwait(false);
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
