using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Google.Protobuf;
using OpenTelemetry.Proto.Common.V1;

namespace Nami.Collector.Ingestion;

internal static partial class OtlpConverter
{
    // Identity precedes lossy persistence policy. Otherwise denied resource/scope/point
    // attributes merge unrelated streams. Only the digest reaches storage, never these values.
    // https://github.com/open-telemetry/opentelemetry-specification/blob/v1.61.0/specification/metrics/data-model.md#opentelemetry-protocol-data-model
    private static string MetricAttributeIdentity(IEnumerable<KeyValue> attributes) =>
        Convert.ToHexStringLower(SHA256.HashData(CanonicalAttributes(attributes).ToByteArray()));

    private static KeyValueList CanonicalAttributes(IEnumerable<KeyValue> attributes)
    {
        var values = new SortedDictionary<string, AnyValue>(StringComparer.Ordinal);
        foreach (var attribute in attributes)
        {
            if (IsUnresolvedProfilingKey(attribute)) continue;
            if (string.IsNullOrWhiteSpace(attribute.Key))
                throw new InvalidDataException("OTLP attributes require nonempty, unique keys.");
            var value = CanonicalIdentityValue(attribute.Value);
            if (!values.TryAdd(attribute.Key, value) && !values[attribute.Key].Equals(value))
                throw new InvalidDataException("OTLP attributes require unique keys; duplicate values conflict.");
        }

        var result = new KeyValueList();
        foreach (var (key, value) in values)
            result.Values.Add(new KeyValue { Key = key, Value = value });
        return result;
    }

    private static AnyValue CanonicalIdentityValue(AnyValue? value)
    {
        if (value is null) return new AnyValue();
        switch (value.ValueCase)
        {
            case AnyValue.ValueOneofCase.StringValue: return new AnyValue { StringValue = value.StringValue };
            case AnyValue.ValueOneofCase.BoolValue: return new AnyValue { BoolValue = value.BoolValue };
            case AnyValue.ValueOneofCase.IntValue: return new AnyValue { IntValue = value.IntValue };
            case AnyValue.ValueOneofCase.DoubleValue: return new AnyValue { DoubleValue = value.DoubleValue };
            case AnyValue.ValueOneofCase.BytesValue: return new AnyValue { BytesValue = value.BytesValue };
            case AnyValue.ValueOneofCase.KvlistValue:
                return new AnyValue { KvlistValue = CanonicalAttributes(value.KvlistValue.Values) };
            case AnyValue.ValueOneofCase.ArrayValue:
                var array = new OpenTelemetry.Proto.Common.V1.ArrayValue();
                array.Values.Add(value.ArrayValue.Values.Select(CanonicalIdentityValue));
                return new AnyValue { ArrayValue = array };
            default: return new AnyValue();
        }
    }
}
