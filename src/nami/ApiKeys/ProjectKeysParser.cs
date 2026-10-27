using System.Diagnostics.CodeAnalysis;
using Nami.Collector.Ingestion.Configuration;

namespace Nami.ApiKeys;

/// <summary>
///     Reads NAMI_OTLP_PROJECT_KEYS, a JSON object mapping each project to its credentials. A failure
///     names the setting and what is wrong with it, never the credential it holds.
/// </summary>
internal static class ProjectKeysParser
{
    public static bool TryParse(
        string? json,
        out IReadOnlyList<ProjectApiKey> keys,
        [NotNullWhen(false)] out string? error)
    {
        try
        {
            keys = Parse(json);
            error = null;
            return true;
        }
        catch (InvalidOperationException invalid)
        {
            keys = [];
            error = invalid.Message;
            return false;
        }
    }

    private static List<ProjectApiKey> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            var document = JsonSerializer.Deserialize(json, CredentialJsonContext.Default.ProjectKeyDocument)
                ?? throw new InvalidOperationException($"{CollectorConfigurationKeys.ProjectKeys} must be a JSON object.");

            var keys = new List<ProjectApiKey>();
            var credentials = new HashSet<string>(StringComparer.Ordinal);
            foreach (var project in document)
            {
                if (string.IsNullOrWhiteSpace(project.Key) || project.Key != project.Key.Trim() ||
                    project.Value is null)
                {
                    throw new InvalidOperationException(
                        $"{CollectorConfigurationKeys.ProjectKeys} has an invalid or duplicate project.");
                }

                var count = 0;
                foreach (var entry in project.Value)
                {
                    var (value, roles) = ParseCredential(entry);
                    if (!credentials.Add(value))
                    {
                        throw new InvalidOperationException(
                            $"{CollectorConfigurationKeys.ProjectKeys} has an invalid or duplicate credential.");
                    }

                    keys.Add(new ProjectApiKey(project.Key, value, roles));
                    count++;
                }

                if (count is 0)
                {
                    throw new InvalidOperationException(
                        $"{CollectorConfigurationKeys.ProjectKeys} has a project without credentials.");
                }
            }

            return keys;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{CollectorConfigurationKeys.ProjectKeys} must be valid JSON.", ex);
        }
    }

    // Every credential declares its permissions; no implicit roles are granted.
    private static (string Key, ApiKeyRoles Roles) ParseCredential(CredentialDefinition entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Key))
            throw new InvalidOperationException($"{CollectorConfigurationKeys.ProjectKeys} has an empty credential.");

        var roles = ApiKeyRoles.None;
        foreach (var roleElement in entry.Roles)
        {
            if (!ApiKeyRolesNames.TryParse(roleElement, out var role) ||
                (roles & role) is not ApiKeyRoles.None)
            {
                throw new InvalidOperationException(
                    $"{CollectorConfigurationKeys.ProjectKeys} roles must be distinct values of ingest and read.");
            }

            roles |= role;
        }

        return roles is ApiKeyRoles.None
            ? throw new InvalidOperationException(
                $"{CollectorConfigurationKeys.ProjectKeys} has a credential without roles.")
            : (entry.Key, roles);
    }
}
