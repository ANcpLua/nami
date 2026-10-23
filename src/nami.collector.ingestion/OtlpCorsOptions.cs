using Microsoft.Extensions.Configuration;
using Nami.Collector.Ingestion.Configuration;

namespace Nami.Collector.Ingestion;

internal sealed class OtlpCorsOptions
{
    [ConfigurationKeyName(CollectorConfigurationKeys.CorsAllowedOrigins)]
    public string? AllowedOrigins { get; set; }

    [ConfigurationKeyName(CollectorConfigurationKeys.CorsAllowedHeaders)]
    public string? AllowedHeaders { get; set; }

    internal const int MaxAge = 86400;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(AllowedOrigins);

    public IEnumerable<string> GetOrigins() =>
        AllowedOrigins?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        ?? [];

    public IEnumerable<string> GetHeaders()
    {
        var defaults = new[] { "content-type", OtlpConstants.ApiKeyHeaderName };
        var custom = AllowedHeaders?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     ?? [];
        return defaults.Concat(custom).Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
