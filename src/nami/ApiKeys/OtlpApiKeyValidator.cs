namespace Nami.Collector.ApiKeys;

/// <summary>
/// Resolve the credential, and with it the project and roles, for both transports.
/// </summary>
internal static class OtlpApiKeyValidator
{
    public static ProjectApiKey? Resolve(string? candidate, OtlpApiKeyOptions options)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return null;

        // Every entry is compared, so the time taken does not reveal which credential matched.
        ProjectApiKey? match = null;
        foreach (var entry in options.Keys)
        {
            if (FixedTimeEquals(candidate, entry.ApiKey))
                match = entry;
        }

        return match;
    }

    private static bool FixedTimeEquals(string candidate, string expected)
    {
        if (candidate.Length != expected.Length)
            return false;

        var diff = 0;
        for (var i = 0; i < candidate.Length; i++)
            diff |= candidate[i] ^ expected[i];

        return diff is 0;
    }
}
