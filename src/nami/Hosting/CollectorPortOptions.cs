using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Nami.Collector.Ingestion.Configuration;

namespace Nami.Hosting;

internal sealed class CollectorPortOptions
{
    [ConfigurationKeyName(CollectorConfigurationKeys.BindAddress)]
    public string BindAddress { get; set; } = IPAddress.Loopback.ToString();

    [ConfigurationKeyName(CollectorConfigurationKeys.OtlpPort)]
    [ConfiguredRange(CollectorConfigurationKeys.OtlpPort, 0, IPEndPoint.MaxPort)]
    public int OtlpHttp { get; set; } = 4318;

    [ConfigurationKeyName(CollectorConfigurationKeys.GrpcPort)]
    [ConfiguredRange(CollectorConfigurationKeys.GrpcPort, 0, IPEndPoint.MaxPort)]
    public int Grpc { get; set; } = 4317;

    public IPAddress ParseBindAddress()
    {
        return IPAddress.Parse(BindAddress);
    }
}

[OptionsValidator]
internal sealed partial class CollectorPortOptionsValidator : IValidateOptions<CollectorPortOptions>;

/// <summary>The native engine binds only the configured OTLP interfaces.</summary>
internal sealed class CollectorPortOptionsRules : IValidateOptions<CollectorPortOptions>
{
    public ValidateOptionsResult Validate(string? name, CollectorPortOptions options)
    {
        List<string> failures = [];
        if (!IPAddress.TryParse(options.BindAddress, out _))
            failures.Add(
                $"{CollectorConfigurationKeys.BindAddress} must be an IP address literal, but was '{options.BindAddress}'.");
        return failures.Count is 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
