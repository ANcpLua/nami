using JetBrains.Annotations;
using static Nami.Tests.Capture.CapturedActivityAssertions;

namespace Nami.Tests.Policies;

/// <summary>
///     The test's traces capture exactly the named HTTP header and RPC metadata attributes, such as
///     <c>http.request.header.x-request-id</c>: each is present on a span and no other captured field appears.
///     Without names, no header or metadata field may be captured. Assert the values in the test.
/// </summary>
/// <param name="fields">Captured-field attribute keys, as instrumentation emits them.</param>
[PublicAPI]
public sealed class CapturesExactlyAttribute(params string[] fields) : TelemetryPolicyAttribute
{
    public IReadOnlyList<string> Fields { get; } = [.. fields];

    private protected override IEnumerable<string> Violations(TelemetryInvocation invocation)
    {
        foreach (var field in Fields.Where(field => !IsCapturedField(field)))
            yield return $"'{field}' is not an HTTP header or RPC metadata attribute.";
        var captured = invocation.Traces.SelectMany(trace => trace.Evidence.Spans)
            .Distinct<SpanRecord>(ReferenceEqualityComparer.Instance)
            .SelectMany(span => span.Attributes.Keys.Where(IsCapturedField).Select(key => (Key: key, Span: span)))
            .ToArray();
        foreach (var field in Fields.Where(field => IsCapturedField(field) && captured.All(item => item.Key != field)))
            yield return $"'{field}' was selected, but no span captured it.";
        foreach (var (key, span) in captured.Where(item => !Fields.Contains(item.Key, StringComparer.Ordinal)))
            yield return $"{Describe(span)} captured unselected '{key}'.";
    }
}
