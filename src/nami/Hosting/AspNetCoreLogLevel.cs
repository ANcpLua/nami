namespace Nami.Hosting;

/// <summary>
///     ASP.NET Core's own log and that of its gRPC server, the categories under <c>Microsoft.AspNetCore</c> and
///     <c>Grpc</c>, repeat what a sender chose below Warning: the request log names the Content-Type and the path
///     of every request at Information, routing the path at Debug, Kestrel the request line it could not read,
///     and the gRPC server the service and method it does not have, a content type and a <c>grpc-encoding</c> it
///     does not read. That is received text in nami's log. The engine writes Warning and above, but that is a
///     setting, and a lowered one (<c>Logging:LogLevel</c> in configuration or in the environment) would write
///     it. So these two families are held at Warning here, in code, whatever a setting says. Every other
///     category keeps the level its rule gives it.
/// </summary>
internal static class AspNetCoreLogLevel
{
    internal static readonly string[] Held = ["Microsoft.AspNetCore", "Grpc"];

    /// <summary>
    ///     Applied after every other rule has been read, and again when the configuration is read again. A logger
    ///     takes the rule of its own provider first and then the one with the longest category. So a rule at
    ///     Warning is added for each family and every provider that has a rule, and every rule below Warning,
    ///     which may be the one a logger of these families takes (a longer category, a wildcard), gets a filter
    ///     that holds these families and nothing else: the rule's level stands for every other category it names.
    /// </summary>
    public static void HoldAspNetCoreLogAtWarning(this IServiceCollection services) =>
        services.PostConfigure<LoggerFilterOptions>(static options =>
        {
            var providers = new HashSet<string?> { null };
            for (var i = 0; i < options.Rules.Count; i++)
            {
                var rule = options.Rules[i];
                providers.Add(rule.ProviderName);
                if (rule.LogLevel is not >= LogLevel.Warning)
                    options.Rules[i] = new LoggerFilterRule(rule.ProviderName, rule.CategoryName, rule.LogLevel, Holding(rule.Filter));
            }

            foreach (var provider in providers)
                foreach (var family in Held)
                    options.Rules.Add(new LoggerFilterRule(provider, family, LogLevel.Warning, null));
        });

    private static Func<string?, string?, LogLevel, bool> Holding(Func<string?, string?, LogLevel, bool>? filter) =>
        (provider, category, level) =>
            (level >= LogLevel.Warning || !IsHeld(category)) && (filter is null || filter(provider, category, level));

    private static bool IsHeld(string? category)
    {
        if (category is null) return false;
        foreach (var family in Held)
        {
            if (category.StartsWith(family, StringComparison.OrdinalIgnoreCase) &&
                (category.Length == family.Length || category[family.Length] is '.'))
                return true;
        }

        return false;
    }
}
