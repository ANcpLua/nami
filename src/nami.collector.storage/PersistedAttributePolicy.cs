namespace Nami.Collector.Storage;

internal static class PersistedAttributePolicy
{
    // Every attribute is written under the key and in the type it was sent in, on every signal and carrier. Storage
    // drops no key: what a sender captures is decided at the source, and ingest has bounded and redacted the values.
    internal static string? SerializeScopeAttributes(IReadOnlyDictionary<string, OtlpAttributeValue> attributes) =>
        Serialize(attributes);

    internal static string? SerializeAttributes(IReadOnlyDictionary<string, OtlpAttributeValue> attributes) =>
        Serialize(attributes);

    private static string? Serialize(IReadOnlyDictionary<string, OtlpAttributeValue> attributes)
    {
        var persisted = attributes
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
