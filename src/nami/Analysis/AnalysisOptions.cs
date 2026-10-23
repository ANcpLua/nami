using Microsoft.Extensions.Options;
using Nami.Collector.Ingestion.Configuration;

namespace Nami.Collector.Analysis;

internal sealed class AnalysisOptions
{
    internal const int MinimumSigningKeyBytes = 32;

    // Omitted, a local collector persists an independent random key beside its database.
    [ConfigurationKeyName(CollectorConfigurationKeys.AnalysisSigningKey)]
    public string? SigningKey { get; set; }

    public bool HasSigningKey => !string.IsNullOrWhiteSpace(SigningKey);
}

/// <summary>A configured signing key has at least 32 UTF-8 bytes; the failure states its length, never the key.</summary>
internal sealed class AnalysisOptionsRules : IValidateOptions<AnalysisOptions>
{
    public ValidateOptionsResult Validate(string? name, AnalysisOptions options) =>
        options.HasSigningKey && Encoding.UTF8.GetByteCount(options.SigningKey!) is var bytes && bytes < AnalysisOptions.MinimumSigningKeyBytes
            ? ValidateOptionsResult.Fail(
                $"{CollectorConfigurationKeys.AnalysisSigningKey} must contain at least {AnalysisOptions.MinimumSigningKeyBytes} bytes, but has {bytes}.")
            : ValidateOptionsResult.Success;
}
