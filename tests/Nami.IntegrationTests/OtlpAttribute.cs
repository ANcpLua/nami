using OpenTelemetry.Proto.Common.V1;

namespace Nami.IntegrationTests;

/// <summary>OTLP attributes for request builders; import with <c>using static</c>.</summary>
internal static class OtlpAttribute
{
    /// <summary>A string attribute.</summary>
    internal static KeyValue Text(string key, string value) =>
        new() { Key = key, Value = new AnyValue { StringValue = value } };
}
