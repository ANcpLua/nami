using System;
using System.Linq;

namespace Nami.Collector.Ingestion;

internal static class OtlpConstants
{
    public const string ApiKeyHeaderName = "x-otlp-api-key";

    private static readonly string[] s_paths = ["/v1/traces", "/v1/logs", "/v1/metrics"];

    public static bool IsOtlpPath(string path) =>
        s_paths.Any(candidate => IsExactPathOrTrailingSlash(path, candidate));

    private static bool IsExactPathOrTrailingSlash(string path, string candidate) =>
        path.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
        (path.Length == candidate.Length + 1 &&
         path[^1] is '/' &&
         path.AsSpan(0, candidate.Length).Equals(candidate, StringComparison.OrdinalIgnoreCase));
}
