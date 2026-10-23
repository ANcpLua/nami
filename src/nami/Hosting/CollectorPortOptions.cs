using System.Net;
using Microsoft.Extensions.Options;
using Nami.Collector.Ingestion.Configuration;

namespace Nami.Collector.Hosting;

internal sealed class CollectorPortOptions
{
    private const int DefaultHttpPort = 5100;

    [ConfigurationKeyName(CollectorConfigurationKeys.BindAddress)]
    public string BindAddress { get; set; } = IPAddress.Loopback.ToString();

    // NAMI_PORT wins over the platform's PORT; either is the product API port.
    [ConfigurationKeyName(CollectorConfigurationKeys.Port)]
    [ConfiguredRange(CollectorConfigurationKeys.Port, 1, IPEndPoint.MaxPort)]
    public int? Port { get; set; }

    [ConfigurationKeyName(CollectorConfigurationKeys.PlatformPort)]
    public int? PlatformPort { get; set; }

    [ConfigurationKeyName(CollectorConfigurationKeys.OtlpPort)]
    [ConfiguredRange(CollectorConfigurationKeys.OtlpPort, 0, IPEndPoint.MaxPort)]
    public int OtlpHttp { get; set; } = 4318;

    [ConfigurationKeyName(CollectorConfigurationKeys.GrpcPort)]
    [ConfiguredRange(CollectorConfigurationKeys.GrpcPort, 0, IPEndPoint.MaxPort)]
    public int Grpc { get; set; } = 4317;

    public int Http => Port ?? PlatformPort ?? DefaultHttpPort;

    public IPAddress ParseBindAddress() => IPAddress.Parse(BindAddress);
}

[OptionsValidator]
internal sealed partial class CollectorPortOptionsValidator : IValidateOptions<CollectorPortOptions>;

/// <summary>The rules across members: the bind address parses and the effective product API port is usable.</summary>
internal sealed class CollectorPortOptionsRules : IValidateOptions<CollectorPortOptions>
{
    public ValidateOptionsResult Validate(string? name, CollectorPortOptions options)
    {
        List<string> failures = [];
        if (!IPAddress.TryParse(options.BindAddress, out _))
            failures.Add($"{CollectorConfigurationKeys.BindAddress} must be an IP address literal, but was '{options.BindAddress}'.");
        if (options.Port is null && options.PlatformPort is { } platform && platform is < 1 or > IPEndPoint.MaxPort)
            failures.Add($"{CollectorConfigurationKeys.PlatformPort} must be between 1 and {IPEndPoint.MaxPort}, but was '{platform}'.");
        return failures.Count is 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
