using Microsoft.Extensions.Options;
using Nami.Collector.Ingestion.Configuration;

namespace Nami.ApiKeys;

internal sealed partial class OtlpApiKeyOptions
{
    internal const string ApiKeyMode = "ApiKey";
    internal const string UnsecuredMode = "Unsecured";

    public bool IsApiKeyMode =>
        string.Equals(AuthMode, ApiKeyMode, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
///     The mode is one of ApiKey and Unsecured, the credential JSON parses, and ApiKey mode has
///     credentials. A failure never repeats a credential.
/// </summary>
internal sealed class OtlpApiKeyOptionsRules : IValidateOptions<OtlpApiKeyOptions>
{
    public ValidateOptionsResult Validate(string? name, OtlpApiKeyOptions options)
    {
        if (!string.Equals(options.AuthMode, OtlpApiKeyOptions.ApiKeyMode, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(options.AuthMode, OtlpApiKeyOptions.UnsecuredMode, StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail(
                $"{CollectorConfigurationKeys.AuthMode} must be one of {OtlpApiKeyOptions.ApiKeyMode}, {OtlpApiKeyOptions.UnsecuredMode}, but was '{options.AuthMode}'.");
        }

        if (!ProjectKeysParser.TryParse(options.ProjectKeys, out _, out var error))
            return ValidateOptionsResult.Fail(error);

        return options is { IsApiKeyMode: true, Keys.Count: 0 }
            ? ValidateOptionsResult.Fail(
                $"{CollectorConfigurationKeys.AuthMode} is '{OtlpApiKeyOptions.ApiKeyMode}' but no project keys are configured. " +
                $"Set {CollectorConfigurationKeys.ProjectKeys} to a JSON object mapping project IDs to arrays of {{\"key\": string, \"roles\": [...]}} objects.")
            : ValidateOptionsResult.Success;
    }
}
