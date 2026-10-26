namespace Nami.Collector.Storage;

internal static class SpanChildStorage
{
    public static string? SerializeEvents(IReadOnlyList<SpanEventIngest> events)
    {
        if (events.Count is 0)
            return null;

        var list = events.Select(static e => new SpanEventJson
            {
                Name = e.Name,
                TimeUnixNano = e.TimeUnixNano,
                AttributesJson = PersistedAttributePolicy.SerializeAttributes(e.Attributes),
                DroppedAttributesCount = e.DroppedAttributesCount
            }).ToList();

        return JsonSerializer.Serialize(list, StorageJsonSerializerContext.Default.SpanEventJsonList);
    }

    public static string? SerializeLinks(IReadOnlyList<SpanLinkIngest> links)
    {
        if (links.Count is 0)
            return null;

        var list = links.Select(static l => new SpanLinkJson
            {
                TraceId = l.TraceId,
                SpanId = l.SpanId,
                AttributesJson = PersistedAttributePolicy.SerializeAttributes(l.Attributes),
                DroppedAttributesCount = l.DroppedAttributesCount,
                Flags = l.Flags,
                TraceState = l.TraceState
            }).ToList();

        return JsonSerializer.Serialize(list, StorageJsonSerializerContext.Default.SpanLinkJsonList);
    }

    public static List<SpanEventJson>? DeserializeEvents(string? json) =>
        string.IsNullOrEmpty(json)
            ? null
            : JsonSerializer.Deserialize(json, StorageJsonSerializerContext.Default.SpanEventJsonList);

    public static List<SpanLinkJson>? DeserializeLinks(string? json) =>
        string.IsNullOrEmpty(json)
            ? null
            : JsonSerializer.Deserialize(json, StorageJsonSerializerContext.Default.SpanLinkJsonList);
}
