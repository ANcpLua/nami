using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Nami.Tests.Policies;

/// <summary>A trace published to the running invocation, with the Collector's stored spans when it was read back.</summary>
internal sealed record PublishedTrace(TraceEvidence Evidence, string RootSpanId);

/// <summary>Collects the traces published during one [TracePerTest] invocation and applies its declared policies.</summary>
internal sealed class TelemetryInvocation : IDisposable
{
    private const int ReportedViolations = 25;
    private static readonly AsyncLocal<TelemetryInvocation?> s_active = new();
    private readonly TelemetryInvocation? _previous;
    private readonly ConcurrentQueue<PublishedTrace> _traces = new();

    private TelemetryInvocation(IReadOnlyList<TelemetryPolicyAttribute> policies, TelemetryInvocation? previous)
    {
        Policies = policies;
        _previous = previous;
    }

    private IReadOnlyList<TelemetryPolicyAttribute> Policies { get; }
    internal IReadOnlyList<PublishedTrace> Traces => [.. _traces];

    /// <summary>Distinct receiver snapshots in publication order; several traces can share one snapshot.</summary>
    internal IReadOnlyList<TelemetrySnapshot> Captures =>
    [
        .. _traces.Select(trace => trace.Evidence.Capture)
            .Distinct<TelemetrySnapshot>(ReferenceEqualityComparer.Instance)
    ];

    public void Dispose()
    {
        s_active.Value = _previous;
    }

    internal static TelemetryInvocation Begin(IReadOnlyList<TelemetryPolicyAttribute> policies)
    {
        var invocation = new TelemetryInvocation(policies, s_active.Value);
        s_active.Value = invocation;
        return invocation;
    }

    internal static void Publish(PublishedTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        if (s_active.Value is { } invocation) invocation._traces.Enqueue(trace);
        else TelemetryPolicyAttribute.RejectUnverifiedPolicies();
    }

    internal IReadOnlyList<string> Violations()
    {
        return
        [
            .. Policies.SelectMany(policy => policy.Check(this).Select(violation => $"[{policy.Name}] {violation}"))
        ];
    }

    /// <summary>Turns each passing result into a failure when a declared policy is violated; other outcomes stay.</summary>
    internal void Verify(IReadOnlyList<TestResult> results, IEvidenceStore evidenceStore)
    {
        if (Policies.Count == 0 || results.All(result => result.Outcome != UnitTestOutcome.Passed)) return;
        var violations = Violations();
        if (violations.Count == 0) return;
        var evidence = evidenceStore.Save(TelemetryReport.Render(this, violations));
        var message = new StringBuilder($"Telemetry policy violated ({violations.Count}):");
        foreach (var violation in violations.Take(ReportedViolations))
            message.AppendLine().Append("- ").Append(violation);
        if (violations.Count > ReportedViolations)
        {
            message.AppendLine().Append(CultureInfo.InvariantCulture,
                $"- {violations.Count - ReportedViolations} more in the evidence file.");
        }

        message.AppendLine().Append(TelemetryReport.Outline(this)).AppendLine().Append("Evidence: ").Append(evidence);
        foreach (var result in results.Where(result => result.Outcome == UnitTestOutcome.Passed))
        {
            result.Outcome = UnitTestOutcome.Failed;
            result.TestFailureException = new AssertFailedException(message.ToString());
            result.ResultFiles = [.. result.ResultFiles ?? [], evidence];
        }
    }
}
