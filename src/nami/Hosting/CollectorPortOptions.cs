using System.Net;
using Microsoft.Extensions.Options;
using Nami.Collector.Ingestion.Configuration;

namespace Nami.Hosting;

internal sealed partial class CollectorPortOptions
{
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
        {
            failures.Add(
                $"{CollectorConfigurationKeys.BindAddress} must be an IP address literal, but was '{options.BindAddress}'.");
        }

        return failures.Count is 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
