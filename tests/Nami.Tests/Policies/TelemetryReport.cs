using System.Globalization;
using System.Text;

namespace Nami.Tests.Policies;

/// <summary>Renders published traces as span trees for policy failures and their evidence file.</summary>
internal static class TelemetryReport
{
    private const int OutlineLines = 40;

    internal static string Outline(TelemetryInvocation invocation)
    {
        var lines = invocation.Traces.SelectMany(trace => Tree(trace, false)).ToArray();
        return string.Join(Environment.NewLine, lines.Length <= OutlineLines
            ? lines
            : lines.Take(OutlineLines)
                .Append(FormattableString.Invariant(
                    $"... {lines.Length - OutlineLines} more lines in the evidence file.")));
    }

    internal static string Render(TelemetryInvocation invocation, IReadOnlyList<string> violations)
    {
        var text = new StringBuilder();
        text.AppendLine("Nami telemetry capture");
        foreach (var violation in violations) text.AppendLine(violation);
        foreach (var trace in invocation.Traces)
        {
            text.AppendLine();
            foreach (var line in Tree(trace, true)) text.AppendLine(line);
        }

        foreach (var capture in invocation.Captures)
        {
            text.AppendLine().AppendLine(CultureInfo.InvariantCulture,
                $"capture: {capture.Spans.Count} span(s), {capture.Logs.Count} log(s), "
                + $"{capture.Metrics.Count} metric(s), {capture.Receipts.Count} export(s)");
            foreach (var receipt in capture.Receipts)
            {
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"  export {receipt.Transport} {receipt.Signal}: accepted {receipt.Accepted}, rejected {receipt.Rejected}{(receipt.Error is null ? "" : $", error {receipt.Error}")}{string.Concat(receipt.Warnings.Select(warning => $", warning {warning}"))}");
            }

            foreach (var log in capture.Logs)
            {
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"  log {log.SeverityText}({log.SeverityNumber}) {Service(log.Resource)} {log.Scope?.Name ?? "-"} "
                    + $"{(string.IsNullOrEmpty(log.EventName) ? Format(log.Body) : log.EventName)} trace {log.TraceId ?? "-"} span {log.SpanId ?? "-"}");
            }

            foreach (var metric in capture.Metrics)
            {
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"  metric {metric.Name} [{metric.Unit}] {Service(metric.Resource)} {metric.Scope?.Name ?? "-"}");
            }
        }

        return text.ToString();
    }

    internal static string Service(ResourceRecord resource)
    {
        return resource.Attributes.TryGetValue("service.name", out var name) && name is string service
            ? service
            : "no service";
    }

    internal static string Format(object? value)
    {
        return value switch
        {
            null => "null",
            string text => $"\"{text}\"",
            bool flag => flag ? "true" : "false",
            byte[] bytes => "base64:" + Convert.ToBase64String(bytes),
            IReadOnlyDictionary<string, object?> map => "{" +
                                                        string.Join(", ",
                                                            map.Select(pair => $"{pair.Key}: {Format(pair.Value)}")) +
                                                        "}",
            IReadOnlyList<object?> items => "[" + string.Join(", ", items.Select(Format)) + "]",
            IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };
    }

    private static List<string> Tree(PublishedTrace trace, bool details)
    {
        var spans = trace.Evidence.Spans;
        var ids = spans.Select(span => span.SpanId).ToHashSet(StringComparer.Ordinal);
        var occurrences = spans.Index().ToArray();
        var counts = spans.CountBy(span => span.SpanId, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var children = occurrences.ToLookup(item => item.Item.ParentSpanId ?? "", StringComparer.Ordinal);
        var visited = new HashSet<int>();
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        var lines = new List<string>
            { $"trace {trace.Evidence.TraceId}, test root {trace.RootSpanId}, {spans.Count} span(s)" };
        foreach (var root in occurrences.Where(item =>
                         string.IsNullOrEmpty(item.Item.ParentSpanId) || !ids.Contains(item.Item.ParentSpanId))
                     .OrderBy(item => item.Item.StartTimeUnixNano))
            Walk(root.Index, 1);
        foreach (var unrooted in occurrences.OrderBy(item => item.Item.StartTimeUnixNano))
        {
            if (visited.Contains(unrooted.Index)) continue;
            lines.Add("  unrooted component (cyclic parentage):");
            Walk(unrooted.Index, 2);
        }

        return lines;

        void Walk(int index, int depth)
        {
            var pending = new Stack<(int Index, int Depth)>();
            pending.Push((index, depth));
            while (pending.TryPop(out var next))
            {
                if (!visited.Add(next.Index)) continue;
                var span = spans[next.Index];
                var indent = new string(' ', next.Depth * 2);
                var parent = string.IsNullOrEmpty(span.ParentSpanId) ? ""
                    : span.ParentSpanId == trace.RootSpanId ? "  (under the test root)"
                    : ids.Contains(span.ParentSpanId) ? "" : $"  (parent {span.ParentSpanId} was not captured)";
                var occurrence = counts[span.SpanId] > 1 ? $"  observation {next.Index + 1}" : "";
                lines.Add(
                    $"{indent}{span.Name}  {span.Kind}  {span.Service ?? "no service"}  {span.Scope?.Name ?? "no scope"}  "
                    + $"span {span.SpanId}{occurrence}{(span.Status == ActivityStatusCode.Error ? "  ERROR " + span.StatusDescription : "")}{parent}");
                if (details)
                {
                    lines.Add(
                        $"{indent}  parent {span.ParentSpanId ?? "-"}, time {span.StartTimeUnixNano} to {span.EndTimeUnixNano}, "
                        + $"status {span.Status}: {Format(span.StatusDescription)}");
                    lines.Add(
                        $"{indent}  scope version {Format(span.Scope?.Version)}, schema {Format(span.Scope?.SchemaUrl)}");
                    lines.AddRange(span.Attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => $"{indent}  {pair.Key} = {Format(pair.Value)}"));
                    lines.AddRange(span.Resource.Attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => $"{indent}  resource {pair.Key} = {Format(pair.Value)}"));
                    if (span.Scope is { } scope)
                    {
                        lines.AddRange(scope.Attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                            .Select(pair => $"{indent}  scope {pair.Key} = {Format(pair.Value)}"));
                    }

                    lines.AddRange(span.Events.Select(spanEvent =>
                        $"{indent}  event {spanEvent.Name} at {spanEvent.TimeUnixNano} {Format(spanEvent.Attributes)}"));
                }

                if (!expanded.Add(span.SpanId)) continue;
                foreach (var child in children[span.SpanId].OrderByDescending(item => item.Item.StartTimeUnixNano)
                             .ThenByDescending(item => item.Index))
                    pending.Push((child.Index, next.Depth + 1));
            }
        }
    }
}
