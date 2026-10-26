namespace Nami.Tests;

public sealed record ResourceEntityReference(string Type, IReadOnlyList<string> IdentityKeys)
{
    public IReadOnlyList<string> DescriptionKeys { get; init; } = [];
    public string? SchemaUrl { get; init; }
}

public sealed record ResourceRecord(IReadOnlyDictionary<string, object?> Attributes)
{
    public string? SchemaUrl { get; init; }
    public uint DroppedAttributesCount { get; init; }
    public IReadOnlyList<ResourceEntityReference> EntityReferences { get; init; } = [];
}

public sealed record InstrumentationScopeRecord
{
    public string? Name { get; init; }
    public string? Version { get; init; }
    public string? SchemaUrl { get; init; }
    public IReadOnlyDictionary<string, object?> Attributes { get; init; } = new Dictionary<string, object?>();
    public uint DroppedAttributesCount { get; init; }
}

public sealed record SpanEventRecord(string Name, ulong TimeUnixNano, IReadOnlyDictionary<string, object?> Attributes)
{
    public uint DroppedAttributesCount { get; init; }
}

public sealed record SpanLinkRecord(string TraceId, string SpanId, IReadOnlyDictionary<string, object?> Attributes)
{
    public string? TraceState { get; init; }
    public uint Flags { get; init; }
    public uint DroppedAttributesCount { get; init; }
}

public sealed record SpanRecord(string TraceId, string SpanId, string Name)
{
    public string? ParentSpanId { get; init; }
    public string? TraceState { get; init; }
    public uint Flags { get; init; }
    public SpanKind Kind { get; init; }
    public ulong StartTimeUnixNano { get; init; }
    public ulong EndTimeUnixNano { get; init; }
    public ActivityStatusCode Status { get; init; }
    public string? StatusDescription { get; init; }
    public ResourceRecord Resource { get; init; } = new(new Dictionary<string, object?>());
    public InstrumentationScopeRecord? Scope { get; init; }
    public IReadOnlyDictionary<string, object?> Attributes { get; init; } = new Dictionary<string, object?>();
    public IReadOnlyList<SpanEventRecord> Events { get; init; } = [];
    public IReadOnlyList<SpanLinkRecord> Links { get; init; } = [];
    public uint DroppedAttributesCount { get; init; }
    public uint DroppedEventsCount { get; init; }
    public uint DroppedLinksCount { get; init; }
    public string? Service => Resource.Attributes.GetValueOrDefault("service.name") as string;

    public string? GetString(string name)
    {
        return Attributes.GetValueOrDefault(name) as string;
    }

    public long? GetInt64(string name)
    {
        return Attributes.GetValueOrDefault(name) is long value ? value : null;
    }

    public bool? GetBoolean(string name)
    {
        return Attributes.GetValueOrDefault(name) is bool value ? value : null;
    }

    public IReadOnlyList<string>? GetStrings(string name)
    {
        if (Attributes.GetValueOrDefault(name) is not IEnumerable<object?> values) return null;
        var items = values.ToArray();
        return items.All(value => value is string) ? items.Cast<string>().ToArray() : null;
    }
}

public sealed record StoreIdentity(string? System, string? ServerAddress, long? ServerPort, string? Namespace);
