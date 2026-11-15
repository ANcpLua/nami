using Nami.Api.Contracts.Common;

namespace Nami.Collector.Storage;

/// <summary>
///     Reads the attribute and log body columns that <see cref="PersistedAttributePolicy" /> and
///     <see cref="IngestionStorageMapper" /> write. Readers get contract values: the stored encoding, which tags
///     numbers, bytes and maps with <c>type</c>, stays inside storage.
/// </summary>
internal static class StoredAttributeJson
{
    /// <summary>A column that holds one stored value, such as a log body; null when the column is empty.</summary>
    internal static AttributeValue? Value(string? json) =>
        json is null
            ? null
            : StoredAttributeProjection.ToContract(
                JsonSerializer.Deserialize(json, StorageJsonSerializerContext.Default.StoredAttributeValue));

    /// <summary>A column of stored attributes, in ordinal key order; empty when the column is empty.</summary>
    internal static IReadOnlyList<KeyValuePair<string, AttributeValue?>> Attributes(string? json)
    {
        if (json is null) return [];
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return [];
        return
        [
            .. document.RootElement.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal)
                .Select(static property => KeyValuePair.Create(property.Name, Decode(property.Value)))
        ];
    }

    /// <summary>One attribute of a column of stored attributes; null when the record does not carry it.</summary>
    internal static AttributeValue? Attribute(string? json, string key)
    {
        if (json is null) return null;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Object &&
               document.RootElement.TryGetProperty(key, out var value)
            ? Decode(value)
            : null;
    }

    private static AttributeValue? Decode(JsonElement value) =>
        StoredAttributeProjection.ToContract(value.Deserialize(StorageJsonSerializerContext.Default.StoredAttributeValue));
}
