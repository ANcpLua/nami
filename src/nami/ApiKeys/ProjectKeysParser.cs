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
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException($"{CollectorConfigurationKeys.ProjectKeys} must be a JSON object.");

            var keys = new List<ProjectApiKey>();
            var projects = new HashSet<string>(StringComparer.Ordinal);
            var credentials = new HashSet<string>(StringComparer.Ordinal);
            foreach (var project in document.RootElement.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(project.Name) || project.Name != project.Name.Trim() ||
                    !projects.Add(project.Name) || project.Value.ValueKind != JsonValueKind.Array)
                    throw new InvalidOperationException(
                        $"{CollectorConfigurationKeys.ProjectKeys} has an invalid or duplicate project.");

                var count = 0;
                foreach (var entry in project.Value.EnumerateArray())
                {
                    var (value, roles) = ParseCredential(entry);
                    if (!credentials.Add(value))
                        throw new InvalidOperationException(
                            $"{CollectorConfigurationKeys.ProjectKeys} has an invalid or duplicate credential.");
                    keys.Add(new ProjectApiKey(project.Name, value, roles));
                    count++;
                }

                if (count is 0)
                    throw new InvalidOperationException(
                        $"{CollectorConfigurationKeys.ProjectKeys} has a project without credentials.");
            }

            return keys;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{CollectorConfigurationKeys.ProjectKeys} must be valid JSON.", ex);
        }
    }

    // Every credential declares its permissions; no implicit roles are granted.
    private static (string Key, ApiKeyRoles Roles) ParseCredential(JsonElement entry)
    {
        if (entry.ValueKind is not JsonValueKind.Object ||
            !entry.TryGetProperty("key", out var keyElement) || keyElement.ValueKind is not JsonValueKind.String ||
            string.IsNullOrWhiteSpace(keyElement.GetString()) ||
            !entry.TryGetProperty("roles", out var rolesElement) || rolesElement.ValueKind is not JsonValueKind.Array ||
            entry.EnumerateObject().Count() is not 2 ||
            entry.EnumerateObject().Any(static property => property.Name is not ("key" or "roles")))
        {
            throw new InvalidOperationException(
                $"{CollectorConfigurationKeys.ProjectKeys} credentials must be {{\"key\": string, \"roles\": [...]}} objects.");
        }

        var roles = ApiKeyRoles.None;
        foreach (var roleElement in rolesElement.EnumerateArray())
        {
            if (roleElement.ValueKind is not JsonValueKind.String ||
                !ApiKeyRoleNames.TryParse(roleElement.GetString(), out var role) ||
                (roles & role) is not ApiKeyRoles.None)
            {
                throw new InvalidOperationException(
                    $"{CollectorConfigurationKeys.ProjectKeys} roles must be distinct values of ingest, read, write and delete.");
            }

            roles |= role;
        }

        return roles is ApiKeyRoles.None
            ? throw new InvalidOperationException(
                $"{CollectorConfigurationKeys.ProjectKeys} has a credential without roles.")
            : (keyElement.GetString()!, roles);
    }
}
