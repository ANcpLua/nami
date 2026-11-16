using Microsoft.Extensions.Options;
using Nami.Collector.Ingestion.Configuration;

namespace Nami.Hosting;

[OptionsValidator]
internal sealed partial class CollectorStopOptionsValidator : IValidateOptions<CollectorStopOptions>;

/// <summary>
///     The stop budget is written in decimal digits and nothing else, for the engine as for the host that
///     supervises it (<c>src/nami.mcp/src/main.ts</c>): both read the same variable, so both refuse the same
///     values. The configuration binder alone also takes a sign and white space around the number.
/// </summary>
internal sealed class CollectorStopOptionsRules(IConfiguration configuration) : IValidateOptions<CollectorStopOptions>
{
    public ValidateOptionsResult Validate(string? name, CollectorStopOptions options)
    {
        var text = configuration[CollectorConfigurationKeys.StopBudgetSeconds];
        return text is null || (text.Length > 0 && text.All(char.IsAsciiDigit))
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{CollectorConfigurationKeys.StopBudgetSeconds} must be a whole number of seconds from 1 to 3600 in decimal digits, but was '{text}'.");
    }
}
