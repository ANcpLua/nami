namespace Nami.Collector.Ingestion;

internal sealed partial class OtlpCorsOptions
{
    internal const int MaxAge = 86400;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(AllowedOrigins);

    public IEnumerable<string> GetOrigins() => SplitList(AllowedOrigins);

    public IEnumerable<string> GetHeaders() =>
        new[] { "content-type", OtlpConstants.ApiKeyHeaderName }.Concat(SplitList(AllowedHeaders))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static string[] SplitList(string? list) =>
        list?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
}
