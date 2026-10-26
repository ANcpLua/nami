using JetBrains.Annotations;

namespace Nami.Tests.Policies;

/// <summary>
///     Each operation is exported once: no span identity repeats, no client, server, producer or consumer span
///     wraps a span of the same operation, and no two instrumentation scopes describe the same operation
///     concurrently. A total span count cannot show this.
/// </summary>
[PublicAPI]
public sealed class NoDuplicateSpansAttribute : TelemetryPolicyAttribute
{
    private protected override IEnumerable<string> Violations(TelemetryInvocation invocation)
    {
        var observations = invocation.Traces.SelectMany(trace => trace.Evidence.Spans)
            .Distinct<SpanRecord>(ReferenceEqualityComparer.Instance);
        foreach (var trace in observations.GroupBy(span => span.TraceId, StringComparer.Ordinal))
        {
            var spans = trace.ToArray();
            foreach (var (spanId, count) in spans.CountBy(span => span.SpanId, StringComparer.Ordinal)
                         .Where(pair => pair.Value > 1))
            {
                yield return
                    $"{Describe(spans.First(span => span.SpanId == spanId))} was exported {count} times as span {spanId}.";
            }

            var remote = spans.Where(span =>
                    span.Kind is SpanKind.Server or SpanKind.Client or SpanKind.Producer or SpanKind.Consumer)
                .ToArray();
            var byId = remote.ToLookup(span => span.SpanId, StringComparer.Ordinal);
            foreach (var span in remote)
            {
                if (span.ParentSpanId is not { } parentId)
                    continue;
                foreach (var parent in byId[parentId])
                {
                    if (SameOperation(parent, span))
                    {
                        yield return
                            $"{Describe(span)} repeats its parent {Describe(parent)}: the operation is instrumented twice.";
                    }
                }
            }

            foreach (var siblings in remote.GroupBy(span => (span.ParentSpanId, span.Kind, span.Name, span.Service)))
            {
                var ordered = siblings.ToArray();
                for (var i = 0; i < ordered.Length; i++)
                    for (var j = i + 1; j < ordered.Length; j++)
                    {
                        if (ordered[i].Scope?.Name != ordered[j].Scope?.Name
                            && ordered[i].StartTimeUnixNano < ordered[j].EndTimeUnixNano
                            && ordered[j].StartTimeUnixNano < ordered[i].EndTimeUnixNano)
                        {
                            yield return $"{Describe(ordered[i])} and {Describe(ordered[j])} describe the same operation "
                                         + "concurrently: the operation is instrumented twice.";
                        }
                    }
            }
        }
    }

    private static bool SameOperation(SpanRecord parent, SpanRecord child)
    {
        return parent.Kind == child.Kind && parent.Name == child.Name && parent.Service == child.Service;
    }
}
