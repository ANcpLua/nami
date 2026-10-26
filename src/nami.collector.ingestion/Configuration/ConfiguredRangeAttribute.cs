using System.ComponentModel.DataAnnotations;

namespace Nami.Collector.Ingestion.Configuration;

/// <summary>
///     An inclusive integer range for a bound option whose failure names the configuration key and the
///     value it held, so a host that refuses to start says which setting to fix and why.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class ConfiguredRangeAttribute : ValidationAttribute
{
    public required string Key { get; init; }

    public required long Minimum { get; init; }

    public required long Maximum { get; init; }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext) =>
        value is null || (Convert.ToInt64(value, CultureInfo.InvariantCulture) is var number && number >= Minimum &&
                          number <= Maximum)
            ? ValidationResult.Success
            : new ValidationResult(
                $"{Key} must be between {Minimum} and {Maximum}, but was '{Convert.ToString(value, CultureInfo.InvariantCulture)}'.",
                validationContext.MemberName is { } member ? [member] : null);
}
