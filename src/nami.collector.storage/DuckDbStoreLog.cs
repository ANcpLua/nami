using System;

namespace Nami.Collector.Storage;

internal static partial class DuckDbStoreLog
{
    [LoggerMessage(
        EventId = 4105,
        Level = LogLevel.Error,
        Message = "DuckDB storage shutdown failed")]
    public static partial void ShutdownFailed(
        ILogger logger,
        Exception error);

    [LoggerMessage(
        EventId = 4106,
        Level = LogLevel.Error,
        Message = "DuckDB storage worker stopped unexpectedly")]
    public static partial void StorageWorkerFailed(
        ILogger logger,
        Exception error);
}
