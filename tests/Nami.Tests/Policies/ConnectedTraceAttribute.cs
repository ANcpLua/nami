using JetBrains.Annotations;

namespace Nami.Tests.Policies;

/// <summary>
///     Every exported span of the test's traces descends from the test root: each parent is a captured span of the
///     same trace or the root itself. Detects lost propagation and spans parented to unexported activities.
/// </summary>
[PublicAPI]
public sealed class ConnectedTraceAttribute : TelemetryPolicyAttribute
{
    private protected override IEnumerable<string> Violations(TelemetryInvocation invocation)
    {
        foreach (var trace in invocation.Traces)
        {
            var spans = trace.Evidence.Spans;
            if (spans.Count == 0)
            {
                yield return $"Trace {trace.Evidence.TraceId} exported no span.";
                continue;
            }

            var known = spans.Select(span => span.SpanId).Append(trace.RootSpanId).ToHashSet(StringComparer.Ordinal);
            var cyclic = CyclicAncestry(spans);
            foreach (var span in spans)
            {
                if (TelemetryId.IsAbsent(span.ParentSpanId))
                {
                    if (span.SpanId != trace.RootSpanId)
                        yield return $"{Describe(span)} starts a second root in trace {trace.Evidence.TraceId}.";
                }
                else if (!known.Contains(span.ParentSpanId))
                {
                    yield return $"{Describe(span)} names parent {span.ParentSpanId}, which is neither a captured span "
                                 + "nor the test root.";
                }
                else if (cyclic.Contains(span.ParentSpanId))
                    yield return $"{Describe(span)} has cyclic parentage in trace {trace.Evidence.TraceId}.";
            }
        }
    }

    private static HashSet<string> CyclicAncestry(IReadOnlyList<SpanRecord> spans)
    {
        var ids = spans.Select(span => span.SpanId).ToHashSet(StringComparer.Ordinal);
        var parents = spans.GroupBy(span => span.SpanId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(span => span.ParentSpanId)
                .OfType<string>().Where(ids.Contains).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        var remaining = parents.ToDictionary(pair => pair.Key, pair => pair.Value.Count, StringComparer.Ordinal);
        var children = parents.SelectMany(pair => pair.Value.Select(parent => (Parent: parent, Child: pair.Key)))
            .ToLookup(edge => edge.Parent, edge => edge.Child, StringComparer.Ordinal);
        var terminal = new Queue<string>(remaining.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        while (terminal.TryDequeue(out var id))
        {
            foreach (var child in children[id])
            {
                if (--remaining[child] == 0)
                    terminal.Enqueue(child);
            }
        }

        // Removing terminal ancestry leaves exactly the identities that can reach a cycle, including alternate parents.
        return remaining.Where(pair => pair.Value > 0).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
    }
}
