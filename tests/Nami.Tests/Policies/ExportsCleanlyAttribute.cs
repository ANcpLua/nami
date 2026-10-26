using JetBrains.Annotations;

namespace Nami.Tests.Policies;

/// <summary>
///     Every OTLP export of the test was accepted completely: no rejected record, no export error and no receiver
///     warning, such as a coalesced duplicate attribute. Transport success alone does not show this.
/// </summary>
[PublicAPI]
public sealed class ExportsCleanlyAttribute : TelemetryPolicyAttribute
{
    private protected override IEnumerable<string> Violations(TelemetryInvocation invocation)
    {
        foreach (var capture in invocation.Captures)
        {
            if (capture.Receipts.Count == 0) yield return "The capture recorded no export.";
            foreach (var receipt in capture.Receipts)
            {
                if (receipt.Rejected > 0 || receipt.Error is not null)
                {
                    yield return
                        $"A {receipt.Transport} {receipt.Signal} export rejected {receipt.Rejected} record(s): "
                        + (receipt.Error ?? "no error message");
                }

                foreach (var warning in receipt.Warnings)
                    yield return $"A {receipt.Transport} {receipt.Signal} export: {warning}";
            }
        }
    }
}
