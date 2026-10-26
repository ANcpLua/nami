using Activity = System.Diagnostics.Activity;
using ActivityKind = System.Diagnostics.ActivityKind;
using ActivityStatusCode = System.Diagnostics.ActivityStatusCode;
using ActivityTraceId = System.Diagnostics.ActivityTraceId;
using ActivitySpanId = System.Diagnostics.ActivitySpanId;
using System.Collections.ObjectModel;
using JetBrains.Annotations;

namespace Nami.Tests.Capture;

/// <summary>Copies producer evidence before an Activity or its mutable tag arrays can be reused.</summary>
[PublicAPI]
public sealed record ActivitySnapshot(
    string Source,
    string Operation,
    ActivityKind Kind,
    ActivityStatusCode Status,
    ActivityTraceId Trace,
    ActivitySpanId Span,
    ActivitySpanId Parent,
    IReadOnlyDictionary<string, object?> Tags)
{
    public string? Path => OperationPath(Tags);

    public static ActivitySnapshot Capture(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        var tags = activity.TagObjects.DistinctBy(static tag => tag.Key, StringComparer.Ordinal)
            .ToDictionary(static tag => tag.Key,
                static tag => tag.Value is Array values ? values.Clone() : tag.Value, StringComparer.Ordinal);
        return new ActivitySnapshot(activity.Source.Name, activity.OperationName, activity.Kind, activity.Status,
            activity.TraceId, activity.SpanId, activity.ParentSpanId, new ReadOnlyDictionary<string, object?>(tags));
    }

    private static string? OperationPath(IReadOnlyDictionary<string, object?> tags)
    {
        if (tags.TryGetValue("grpc.method", out var method) && method is string grpcMethod)
            return grpcMethod;
        return tags.TryGetValue("url.full", out var url) && url is string address
            ? new Uri(address, UriKind.Absolute).AbsolutePath
            : null;
    }
}
