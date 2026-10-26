using JetBrains.Annotations;
using Nami.Api.Contracts.Common;
using ContractAttribute = Nami.Api.Contracts.Common.Attribute;

namespace Nami.Tests;

[PublicAPI]
public static class CapturedAttributeAssertions
{
    public static void HasCapturedValues(this Assert _, IReadOnlyList<ContractAttribute> attributes, string key,
        params string[] expected)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(expected);
        var matches = attributes.Where(attribute => attribute.Key == key).ToArray();
        Assert.HasCount(1, matches, $"Expected one captured attribute '{key}'.");
        var values = Assert.IsInstanceOfType<AttributeValue.ArrayValue>(matches[0].Value,
            $"Captured attribute '{key}' must be an array.").Value;
        var actual = values.Select(value => Assert.IsInstanceOfType<AttributeValue.StringValue>(value,
            $"Captured attribute '{key}' must contain strings.").Value).ToArray();
        Assert.AreSequenceEqual(expected, actual, $"Captured attribute '{key}'.");
    }
}
