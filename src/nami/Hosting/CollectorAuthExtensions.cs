using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nami.ApiKeys;

namespace Nami.Hosting;

internal static class CollectorAuthExtensions
{
    public static void AddNamiCollectorAuth(
        this IServiceCollection services,
        IConfiguration config,
        IHostEnvironment environment)
    {
        services.AddOptions<OtlpCorsOptions>()
            .Bind(config)
            .ValidateOnStart();

        services.AddOptions<OtlpApiKeyOptions>()
            .Configure(options => options.AuthMode = environment.IsDevelopment()
                ? OtlpApiKeyOptions.UnsecuredMode
                : OtlpApiKeyOptions.ApiKeyMode)
            .Bind(config)
            .PostConfigure(static options => options.Keys =
                ProjectKeysParser.TryParse(options.ProjectKeys, out var keys, out _) ? keys : [])
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<OtlpApiKeyOptions>, OtlpApiKeyOptionsRules>();

    }
}
