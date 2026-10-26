using Microsoft.Extensions.Logging;

namespace Nami.Tests;

public partial class OtlpReceiver
{
    [LoggerMessage(LogLevel.Warning, "OTLP decoding warning: {Warning}")]
    static partial void LogOtlpDecodingWarningWarning(ILogger logger, string warning);
}
