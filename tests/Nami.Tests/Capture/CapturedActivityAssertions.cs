using ActivityContext = System.Diagnostics.ActivityContext;
using ActivityTraceId = System.Diagnostics.ActivityTraceId;
using ActivitySpanId = System.Diagnostics.ActivitySpanId;
using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using JetBrains.Annotations;

namespace Nami.Tests.Capture;

/// <summary>Producer invariants usable inside a NativeAOT child without a test runner.</summary>
[PublicAPI]
public static class CapturedActivityAssertions
{
    public static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void RequireNativeAot()
    {
        Require(!RuntimeFeature.IsDynamicCodeSupported, "The capture fixture must execute as NativeAOT.");
    }

    public static string[] GetStringValues(IReadOnlyDictionary<string, object?> attributes, string key)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var present = attributes.TryGetValue(key, out var value);
        return present && value is string[] values
            ? values
            : throw new InvalidOperationException(
                $"Captured attribute '{key}': expected CLR string[]; actual {(present ? Describe(value) : "<missing>")}.");
    }

    public static void RequireStringValues(IReadOnlyDictionary<string, object?> attributes, string key,
        params string[] expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var actual = GetStringValues(attributes, key);
        Require(actual.SequenceEqual(expected, StringComparer.Ordinal),
            $"Captured attribute '{key}': expected string[] {FormatValues(expected)}; actual {Describe(actual)}.");
    }

    public static void RequireParentContext(ActivitySnapshot span, ActivityContext parent)
    {
        Require(parent.TraceId != default && parent.SpanId != default,
            "The expected parent must have trace and span IDs.");
        Require(span.Trace == parent.TraceId,
            $"'{span.Operation}' trace changed: expected {parent.TraceId}, actual {span.Trace}.");
        Require(span.Parent == parent.SpanId,
            $"'{span.Operation}' parent changed: expected {parent.SpanId}, actual {span.Parent}.");
        Require(span.Span != default && span.Span != parent.SpanId,
            $"'{span.Operation}' must have its own nonzero span ID; actual {span.Span}.");
    }

    public static void RequireUniqueSpanIdentities(IEnumerable<ActivitySnapshot> spans)
    {
        var identities = new HashSet<(ActivityTraceId, ActivitySpanId)>();
        foreach (var span in spans)
        {
            Require(span.Trace != default && span.Span != default && identities.Add((span.Trace, span.Span)),
                $"'{span.Operation}' has a zero or duplicate trace/span identity: {span.Trace}/{span.Span}.");
        }
    }

    /// <summary>
    ///     The one definition of a captured HTTP header or RPC metadata attribute, including the registry's
    ///     rpc.grpc.*.metadata names, which were renamed to rpc.*.metadata and are still emitted.
    /// </summary>
    public static bool IsCapturedField(string key)
    {
        return key.StartsWith("http.request.header.", StringComparison.Ordinal) ||
               key.StartsWith("http.response.header.", StringComparison.Ordinal) ||
               key.StartsWith("rpc.request.metadata.", StringComparison.Ordinal) ||
               key.StartsWith("rpc.response.metadata.", StringComparison.Ordinal) ||
               key.StartsWith("rpc.grpc.request.metadata.", StringComparison.Ordinal) ||
               key.StartsWith("rpc.grpc.response.metadata.", StringComparison.Ordinal);
    }

    public static void RequireCaptureStopped(IReadOnlyList<ActivitySnapshot> spans, string source)
    {
        Require(spans.Count(span => span.Source == source) == 1,
            $"Expected one native '{source}' span after provider disposal.");
        foreach (var span in spans)
        {
            Require(!span.Tags.Keys.Any(IsCapturedField),
                $"Capture subscription outlived its provider: '{span.Source}'/'{span.Operation}'.");
        }

        RequireUniqueSpanIdentities(spans);
    }

    private static string Describe(object? value)
    {
        return value switch
        {
            null => "null",
            string text => $"String: {FormatValue(text)}",
            IEnumerable values => $"{value.GetType().Name}: {FormatValues(values)}",
            _ => $"{value.GetType().Name}: {FormatValue(value)}"
        };
    }

    private static string FormatValues(IEnumerable values)
    {
        return $"[{string.Join(", ", values.Cast<object?>().Select(FormatValue))}]";
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "null",
            string text => $"\"{JsonEncodedText.Encode(text)}\"",
            IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "null"
        };
    }
}
