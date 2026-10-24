using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nami.Collector.Storage;

internal static class PersistedAttributePolicy
{
    // Instrumentation metadata is still producer-controlled: it follows the same privacy
    // denials as signal and resource attributes, including the exact exception exemptions.
    internal static string? SerializeScopeAttributes(IReadOnlyDictionary<string, OtlpAttributeValue> attributes) =>
        Serialize(attributes, AttributeKeySets.IsPersistable);

    // Application attributes retain their native names and types on every signal.
    internal static string? SerializeAttributes(IReadOnlyDictionary<string, OtlpAttributeValue> attributes) =>
        Serialize(attributes, AttributeKeySets.IsPersistable);

    internal static string? SerializeSpanAttributes(
        IReadOnlyDictionary<string, OtlpAttributeValue> attributes,
        SpanHotAttributeProjection projection) =>
        SerializeSpanAttributesCore(attributes, projection);

    private static string? SerializeSpanAttributesCore(
        IReadOnlyDictionary<string, OtlpAttributeValue> attributes,
        SpanHotAttributeProjection projection)
    {
        var persisted = attributes
            .Where(static item => AttributeKeySets.IsPersistable(item.Key))
            .Where(item =>
                item.Key != CollectorSemanticAttributeCatalog.GenAiOperationName ||
                projection.GenAiOperationName is not null)
            .Where(item =>
                item.Key != CollectorSemanticAttributeCatalog.GenAiOutputType ||
                projection.GenAiOutputType is not null)
            .OrderBy(static item => item.Key, StringComparer.Ordinal)
            .ToArray();

        if (persisted.Length is 0)
            return null;

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in persisted)
            {
                writer.WritePropertyName(key);
                if (key == CollectorSemanticAttributeCatalog.GenAiOperationName)
                    writer.WriteStringValue(projection.GenAiOperationName);
                else if (key == CollectorSemanticAttributeCatalog.GenAiOutputType)
                    writer.WriteStringValue(projection.GenAiOutputType);
                else
                    WriteValue(writer, value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string? Serialize(
        IReadOnlyDictionary<string, OtlpAttributeValue> attributes,
        Func<string, bool> shouldPersist)
    {
        var persisted = attributes
            .Where(item => shouldPersist(item.Key))
            .OrderBy(static item => item.Key, StringComparer.Ordinal)
            .ToArray();

        if (persisted.Length is 0)
            return null;

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in persisted)
            {
                writer.WritePropertyName(key);
                WriteValue(writer, value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // Storage's own record and context write every persisted value; no contract converter does.
    private static void WriteValue(Utf8JsonWriter writer, OtlpAttributeValue value) =>
        JsonSerializer.Serialize(writer, StoredRecordMapper.ToStored(value),
            StorageJsonSerializerContext.Default.StoredAttributeValue);
}
