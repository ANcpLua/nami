namespace Nami.UnitTests;

/// <summary>In-memory span records of one shared trace, and captures that hold only spans.</summary>
internal static class TestSpan
{
    internal const string TraceId = "00112233445566778899aabbccddeeff";
    internal const string UnrelatedTraceId = "ffeeddccbbaa99887766554433221100";

    private static ResourceRecord Resource(string service)
    {
        return new ResourceRecord(new Dictionary<string, object?> { ["service.name"] = service });
    }

    public static SpanRecord Create(string service, string id, SpanKind kind = SpanKind.Internal,
        string? parent = null, string? store = null, string? server = null)
    {
        var attributes = new Dictionary<string, object?>();
        if (store is not null)
        {
            attributes["db.system.name"] = "memory";
            attributes["db.namespace"] = store;
        }

        if (server is not null)
        {
            attributes["server.address"] = server;
        }

        return new SpanRecord(TraceId, id, "operation")
        {
            Kind = kind,
            ParentSpanId = parent,
            Attributes = attributes,
            Resource = Resource(service)
        };
    }

    /// <summary>A capture of <paramref name="spans" /> without logs, metrics or export receipts.</summary>
    public static TelemetrySnapshot Only(params IEnumerable<SpanRecord> spans)
    {
        return new TelemetrySnapshot([.. spans], [], [], []);
    }

    public static TraceEvidence Trace(params SpanRecord[] spans)
    {
        return new TraceEvidence(TraceId, Only(spans));
    }
}
