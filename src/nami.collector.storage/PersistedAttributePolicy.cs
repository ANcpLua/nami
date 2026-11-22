namespace Nami.Collector.Storage;

internal static class PersistedAttributePolicy
{
    // Every attribute is written under the key and in the type it was sent in, on every signal and carrier. Storage
    // drops no key: what a sender captures is decided at the source, and ingest has bounded and redacted the values.
    internal static string? SerializeAttributes(IReadOnlyDictionary<string, OtlpAttributeValue> attributes) =>
        Serialize(attributes);

    // One buffer, writer and sort array per thread: a request of 3,000 spans serializes 9,000 attribute sets, and a
    // new buffer, writer and sorted copy for each was a third of what the mapper allocated (ledger N48).
    private const int MaxRetainedBufferBytes = 64 * 1024;

    private static readonly Comparer<KeyValuePair<string, OtlpAttributeValue>> s_byKey =
        Comparer<KeyValuePair<string, OtlpAttributeValue>>.Create(static (left, right) =>
            string.CompareOrdinal(left.Key, right.Key));

    [ThreadStatic] private static ArrayBufferWriter<byte>? t_buffer;
    [ThreadStatic] private static Utf8JsonWriter? t_writer;

    private static string? Serialize(IReadOnlyDictionary<string, OtlpAttributeValue> attributes)
    {
        var count = attributes.Count;
        if (count is 0)
            return null;

        var persisted = ArrayPool<KeyValuePair<string, OtlpAttributeValue>>.Shared.Rent(count);
        try
        {
            var index = 0;
            foreach (var item in attributes)
                persisted[index++] = item;
            Array.Sort(persisted, 0, count, s_byKey);

            var buffer = t_buffer ??= new ArrayBufferWriter<byte>(1024);
            buffer.ResetWrittenCount();
            var writer = t_writer;
            if (writer is null)
                t_writer = writer = new Utf8JsonWriter(buffer);
            else
                writer.Reset(buffer);

            writer.WriteStartObject();
            for (var i = 0; i < count; i++)
            {
                writer.WritePropertyName(persisted[i].Key);
                WriteValue(writer, persisted[i].Value);
            }

            writer.WriteEndObject();
            writer.Flush();
            var json = Encoding.UTF8.GetString(buffer.WrittenSpan);
            if (buffer.Capacity > MaxRetainedBufferBytes)
            {
                // One large record must not keep its buffer for the life of the thread.
                t_buffer = null;
                t_writer = null;
            }

            return json;
        }
        catch
        {
            // A writer that stopped inside a value is not written to again.
            t_buffer = null;
            t_writer = null;
            throw;
        }
        finally
        {
            ArrayPool<KeyValuePair<string, OtlpAttributeValue>>.Shared.Return(persisted, clearArray: true);
        }
    }

    // Storage's own record and context write every persisted value; no contract converter does.
    private static void WriteValue(Utf8JsonWriter writer, OtlpAttributeValue value) =>
        JsonSerializer.Serialize(writer, StoredRecordMapper.ToStored(value),
            StorageJsonSerializerContext.Default.StoredAttributeValue);
}
