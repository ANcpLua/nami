namespace Nami.Collector.Storage;

internal sealed class SpanEventJson
{
    public string Name { get; set; } = "";
    public ulong TimeUnixNano { get; set; }
    public string? AttributesJson { get; set; }
    public uint DroppedAttributesCount { get; set; }
}

internal sealed class SpanLinkJson
{
    public string TraceId { get; set; } = "";
    public string SpanId { get; set; } = "";
    public string? AttributesJson { get; set; }
    public uint DroppedAttributesCount { get; set; }
    public uint Flags { get; set; }
    public string? TraceState { get; set; }
}

internal static class SpanChildStorage
{
    public static string? SerializeEvents(IReadOnlyList<SpanEventIngest> events)
    {
        if (events.Count is 0)
            return null;

        var list = new List<SpanEventJson>(events.Count);
        foreach (var e in events)
        {
            list.Add(new SpanEventJson
            {
                Name = e.Name,
                TimeUnixNano = e.TimeUnixNano,
                AttributesJson = PersistedAttributePolicy.SerializeAttributes(e.Attributes),
                DroppedAttributesCount = e.DroppedAttributesCount
            });
        }

        return JsonSerializer.Serialize(list, StorageJsonSerializerContext.Default.SpanEventJsonList);
    }

    public static string? SerializeLinks(IReadOnlyList<SpanLinkIngest> links)
    {
        if (links.Count is 0)
            return null;

        var list = new List<SpanLinkJson>(links.Count);
        foreach (var l in links)
        {
            list.Add(new SpanLinkJson
            {
                TraceId = l.TraceId,
                SpanId = l.SpanId,
                AttributesJson = PersistedAttributePolicy.SerializeAttributes(l.Attributes),
                DroppedAttributesCount = l.DroppedAttributesCount,
                Flags = l.Flags,
                TraceState = l.TraceState
            });
        }

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
