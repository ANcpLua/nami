namespace Nami.Collector.Storage;

/// <summary>The persisted JSON columns more than one storage mapper writes, through <see cref="StorageJsonSerializerContext" />.</summary>
internal static class StorageJson
{
    /// <summary>A resource's entity references as stored JSON, or null when the resource has none.</summary>
    internal static string? SerializeResourceEntityRefs(IReadOnlyList<ResourceEntityRefIngestionRecord> entityRefs) =>
        entityRefs.Count is 0
            ? null
            : JsonSerializer.Serialize(entityRefs, StorageJsonSerializerContext.Default.ResourceEntityRefIngestionRecordReadOnlyList);
}
