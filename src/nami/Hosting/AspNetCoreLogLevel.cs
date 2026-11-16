namespace Nami.Hosting;

/// <summary>
///     ASP.NET Core's own log, the categories under <c>Microsoft.AspNetCore</c>, repeats what a sender chose below
///     Warning: the request log names the Content-Type and the path of every request at Information, routing the
///     path at Debug, and Kestrel the request line it could not read. That is received text in nami's log. The
///     engine writes Warning and above, but that is a setting, and a lowered one (<c>Logging:LogLevel</c> in
///     configuration or in the environment) would write it. So these categories are held at Warning here, in
///     code, whatever a setting says.
/// </summary>
internal static class AspNetCoreLogLevel
{
    internal const string Category = "Microsoft.AspNetCore";

    /// <summary>
    ///     Applied after every other rule has been read, and again when the configuration is read again. A logger
    ///     takes the rule of its own provider first and then the one with the longest category, so a rule here is
    ///     added for every provider that has one, and a rule that names these categories more closely is raised
    ///     to Warning itself.
    /// </summary>
    public static void HoldAspNetCoreLogAtWarning(this IServiceCollection services) =>
        services.PostConfigure<LoggerFilterOptions>(static options =>
        {
            var providers = new HashSet<string?> { null };
            for (var i = 0; i < options.Rules.Count; i++)
            {
                var rule = options.Rules[i];
                providers.Add(rule.ProviderName);
                if (rule.CategoryName is { } category && CanName(category) && rule.LogLevel is not >= LogLevel.Warning)
                    options.Rules[i] = new LoggerFilterRule(rule.ProviderName, category, LogLevel.Warning, rule.Filter);
            }

            foreach (var provider in providers)
                options.Rules.Add(new LoggerFilterRule(provider, Category, LogLevel.Warning, null));
        });

    // A rule names one of these categories when its category lies under them or, with its wildcard, can.
    private static bool CanName(string category)
    {
        var wildcard = category.IndexOf('*', StringComparison.Ordinal);
        var prefix = wildcard < 0 ? category : category[..wildcard];
        return prefix.StartsWith(Category, StringComparison.OrdinalIgnoreCase) ||
               (wildcard >= 0 && Category.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
