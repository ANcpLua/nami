using JetBrains.Annotations;
using Nami.SemanticConventions;

namespace Nami.Tests.Policies;

/// <summary>
///     No span of the test's traces ends with error status or records an exception event, and no captured log
///     reaches Error severity. Logs are checked across the complete captures, because connection-level errors
///     carry no trace context; spans outside the test's traces, such as fixture health probes, are not.
/// </summary>
[PublicAPI]
public sealed class NoErrorTelemetryAttribute : TelemetryPolicyAttribute
{
    private const int ErrorSeverity = 17;
    private const string ExceptionEvent = "exception";

    private protected override IEnumerable<string> Violations(TelemetryInvocation invocation)
    {
        foreach (var span in invocation.Traces.SelectMany(trace => trace.Evidence.Spans)
                     .Distinct<SpanRecord>(ReferenceEqualityComparer.Instance))
        {
            if (span.Status == ActivityStatusCode.Error)
            {
                yield return $"{Describe(span)} ended with error status"
                             + (string.IsNullOrEmpty(span.StatusDescription) ? "." : $": {span.StatusDescription}");
            }

            foreach (var exception in span.Events.Where(item => item.Name == ExceptionEvent))
            {
                yield return $"{Describe(span)} recorded {Value(exception.Attributes, ExceptionAttributes.Type)}: "
                             + Value(exception.Attributes, ExceptionAttributes.Message);
            }
        }

        foreach (var log in invocation.Captures.SelectMany(capture => capture.Logs)
                     .Where(log => log.SeverityNumber >= ErrorSeverity))
        {
            yield return
                $"{TelemetryReport.Service(log.Resource)} logged {log.SeverityText} from {log.Scope?.Name ?? "no scope"}: "
                + (string.IsNullOrEmpty(log.EventName) ? TelemetryReport.Format(log.Body) : log.EventName);
        }
    }

    private static string Value(IReadOnlyDictionary<string, object?> attributes, string key)
    {
        return attributes.TryGetValue(key, out var value) ? TelemetryReport.Format(value) : "unknown";
    }
}
