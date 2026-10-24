using Microsoft.Extensions.Options;
using Nami.Collector.Ingestion.Configuration;

namespace Nami.ApiKeys;

internal sealed class OtlpApiKeyOptions
{
    internal const string ApiKeyMode = "ApiKey";
    internal const string UnsecuredMode = "Unsecured";

    // Omitted, the mode is the host environment's: Unsecured in Development, ApiKey elsewhere.
    [ConfigurationKeyName(CollectorConfigurationKeys.AuthMode)]
    public string AuthMode { get; set; } = UnsecuredMode;

    // The raw JSON of NAMI_OTLP_PROJECT_KEYS; Keys is its parsed form.
    [ConfigurationKeyName(CollectorConfigurationKeys.ProjectKeys)]
    public string? ProjectKeys { get; set; }

    // Each credential belongs to exactly one project. Multiple credentials can rotate within a project.
    public IReadOnlyList<ProjectApiKey> Keys { get; internal set; } = [];

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

internal sealed record ProjectApiKey(string ProjectId, string ApiKey, ApiKeyRoles Roles);

/// <summary>
///     What a credential may do (NAMI-A2). The roles are orthogonal: a write key that must also read
///     the resources it changes carries <see cref="Read" /> as well, so a leaked ingest key can never
///     change or delete anything.
/// </summary>
[Flags]
internal enum ApiKeyRoles
{
    None = 0,

    /// <summary>OTLP export over HTTP (<c>/v1/*</c>) and gRPC.</summary>
    Ingest = 1,

    /// <summary>Every <c>GET</c> under <c>/api/v1</c>, plus running a saved query.</summary>
    Read = 2,

    /// <summary>Creating and updating annotations and saved queries.</summary>
    Write = 4,

    /// <summary>Deleting annotations and saved queries. Telemetry is never deleted through the API.</summary>
    Delete = 8
}

internal static class ApiKeyRoleNames
{
    private const string Ingest = "ingest";
    private const string Read = "read";
    private const string Write = "write";
    private const string Delete = "delete";

    public static bool TryParse(string? value, out ApiKeyRoles role)
    {
        role = value switch
        {
            Ingest => ApiKeyRoles.Ingest,
            Read => ApiKeyRoles.Read,
            Write => ApiKeyRoles.Write,
            Delete => ApiKeyRoles.Delete,
            _ => ApiKeyRoles.None
        };
        return role is not ApiKeyRoles.None;
    }
}
