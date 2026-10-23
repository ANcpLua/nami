using Microsoft.Extensions.Options;
using Nami.Collector.Storage;

namespace Nami.Collector.Retention;

internal sealed partial class RetentionService(
    INamiStore store,
    IOptions<RetentionOptions> configured,
    TimeProvider timeProvider,
    ILogger<RetentionService> logger) : BackgroundService
{
    private const int DeleteBatchSize = 1000;

    private readonly RetentionOptions _options = configured.Value;

    private static readonly TimeSpan s_initialDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan s_shutdownCheckpointTimeout = TimeSpan.FromSeconds(30);

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

    internal async Task<RetentionCycleOutcome> RunCycleAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.IsEnabled)
            return RetentionCycleOutcome.Empty;

        var startedAt = Stopwatch.GetTimestamp();
        var fileBefore = store.GetStorageFileMetrics();
        long deletedLogs = 0;
        long deletedSpans = 0;
        long deletedMetricPoints = 0;
        long deletedMetricSeries = 0;
        long deletedAnnotations = 0;
        var deletedIdempotencyKeys = 0;
        var writeBatches = 0;

        try
        {
            var cutoff = NamiTimeConversions.ToUnixNanoUnsigned(
                timeProvider.GetUtcNow().AddDays(-_options.Days));

            while (true)
            {
                var deleted = await store.DeleteExpiredLogsBatchAsync(
                        cutoff,
                        DeleteBatchSize,
                        cancellationToken)
                    .ConfigureAwait(false);
                writeBatches++;
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
                writeBatches++;
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
                writeBatches++;
                deletedMetricPoints += deleted.Points;
                deletedMetricSeries += deleted.Series;
                if (deleted.Points < DeleteBatchSize)
                    break;
            }

            // An annotation points at telemetry by id, so it expires with the same window,
            // counted from its last change; saved queries are configuration and never expire.
            while (true)
            {
                var deleted = await store.DeleteExpiredAnnotationsBatchAsync(
                        cutoff,
                        DeleteBatchSize,
                        cancellationToken)
                    .ConfigureAwait(false);
                writeBatches++;
                deletedAnnotations += deleted;
                if (deleted < DeleteBatchSize)
                    break;
            }

            deletedIdempotencyKeys = await store.DeleteExpiredIdempotencyKeysAsync(
                    NamiTimeConversions.ToUnixNanoUnsigned(
                        timeProvider.GetUtcNow() - Hosting.ResourceHttp.s_idempotencyKeyLifetime),
                    cancellationToken)
                .ConfigureAwait(false);
            writeBatches++;
        }
        finally
        {
            if (deletedLogs + deletedSpans + deletedMetricPoints + deletedMetricSeries + deletedAnnotations +
                deletedIdempotencyKeys > 0)
                await CheckpointAfterDeletionAsync(cancellationToken).ConfigureAwait(false);
        }

        var duration = Stopwatch.GetElapsedTime(startedAt);
        var fileAfter = store.GetStorageFileMetrics();
        var result = new RetentionCycleOutcome(
            deletedLogs,
            deletedSpans,
            deletedMetricPoints,
            deletedMetricSeries,
            deletedAnnotations,
            deletedIdempotencyKeys,
            writeBatches,
            duration,
            fileBefore.ManagedStorageBytes,
            fileAfter.ManagedStorageBytes);

        if (result.TotalRows > 0)
        {
            LogCycleCompleted(
                logger,
                result.DeletedLogRows,
                result.DeletedSpanRows,
                result.DeletedMetricPoints,
                result.DeletedMetricSeries,
                result.Duration.TotalMilliseconds,
                result.FileSizeBeforeBytes,
                result.FileSizeAfterBytes);
        }

        if (result.DeletedAnnotations + result.DeletedIdempotencyKeys > 0)
            LogResourcesExpired(logger, result.DeletedAnnotations, result.DeletedIdempotencyKeys);

        return result;
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
        Message = "Retention cycle deleted {DeletedLogRows} logs, {DeletedSpanRows} spans, {DeletedMetricPoints} metric points, and {DeletedMetricSeries} metric series in {DurationMs} ms; managed storage changed from {FileSizeBeforeBytes} to {FileSizeAfterBytes} bytes")]
    private static partial void LogCycleCompleted(
        ILogger logger,
        long deletedLogRows,
        long deletedSpanRows,
        long deletedMetricPoints,
        long deletedMetricSeries,
        double durationMs,
        long fileSizeBeforeBytes,
        long fileSizeAfterBytes);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Information,
        Message = "Retention cycle expired {DeletedAnnotations} annotations and {DeletedIdempotencyKeys} idempotency keys")]
    private static partial void LogResourcesExpired(
        ILogger logger,
        long deletedAnnotations,
        long deletedIdempotencyKeys);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Error, Message = "Retention cycle failed")]
    private static partial void LogCycleFailed(ILogger logger, Exception exception);
}

internal readonly record struct RetentionCycleOutcome(
    long DeletedLogRows,
    long DeletedSpanRows,
    long DeletedMetricPoints,
    long DeletedMetricSeries,
    long DeletedAnnotations,
    long DeletedIdempotencyKeys,
    int WriteBatches,
    TimeSpan Duration,
    long FileSizeBeforeBytes,
    long FileSizeAfterBytes)
{
    public static RetentionCycleOutcome Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero, 0, 0);

    /// <summary>Telemetry rows deleted; the existing cycle log reports exactly these.</summary>
    public long TotalRows =>
        DeletedLogRows + DeletedSpanRows + DeletedMetricPoints + DeletedMetricSeries;
}
