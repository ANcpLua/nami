using System.Net;
using Microsoft.Extensions.Options;
using Nami.ApiKeys;
using Nami.Collector.Ingestion.Configuration;

namespace Nami.Hosting;

internal static class CollectorAuthExtensions
{
    public static void AddNamiCollectorAuth(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.AddOptions<OtlpCorsOptions>()
            .Bind(config)
            .ValidateOnStart();

        // ApiKey unless NAMI_OTLP_AUTH_MODE says otherwise: no environment name turns Unsecured on.
        // src/nami.mcp/src/main.ts repeats this default and the loopback rule below for /mcp.
        services.AddOptions<OtlpApiKeyOptions>()
            .Configure(static options => options.AuthMode = OtlpApiKeyOptions.ApiKeyMode)
            .Bind(config)
            .PostConfigure(static options => options.Keys =
                ProjectKeysParser.TryParse(options.ProjectKeys, out var keys, out _) ? keys : [])
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<OtlpApiKeyOptions>, OtlpApiKeyOptionsRules>();
        services.AddSingleton<IValidateOptions<OtlpApiKeyOptions>, UnsecuredLoopbackRule>();
    }
}

/// <summary>
///     Unsecured mode takes every request without a key, so the bind address is its only guard: the engine
///     refuses to start in it unless that address is loopback. The host refuses the same before it starts the engine.
/// </summary>
internal sealed class UnsecuredLoopbackRule(IOptions<CollectorPortOptions> ports) : IValidateOptions<OtlpApiKeyOptions>
{
    public ValidateOptionsResult Validate(string? name, OtlpApiKeyOptions options)
    {
        if (!string.Equals(options.AuthMode, OtlpApiKeyOptions.UnsecuredMode, StringComparison.OrdinalIgnoreCase))
            return ValidateOptionsResult.Success;

        // An address that is no IP literal is refused by the rules of CollectorPortOptions.
        var bound = ports.Value.BindAddress;
        return !IPAddress.TryParse(bound, out var address) || IPAddress.IsLoopback(address)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{CollectorConfigurationKeys.AuthMode}={OtlpApiKeyOptions.UnsecuredMode} takes requests without a key, so nami accepts it only on loopback, " +
                $"but {CollectorConfigurationKeys.BindAddress} is '{bound}'. " +
                $"Set {CollectorConfigurationKeys.BindAddress}=127.0.0.1 (or ::1) to stay {OtlpApiKeyOptions.UnsecuredMode} on this machine, " +
                $"or set {CollectorConfigurationKeys.AuthMode}={OtlpApiKeyOptions.ApiKeyMode} and {CollectorConfigurationKeys.ProjectKeys} to listen on '{bound}'.");
    }
}
