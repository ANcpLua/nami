using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using Nami.Tests.Policies;

namespace Nami.Tests;

/// <summary>
///     Gives every invocation and data row its own native trace, then verifies the
///     <see cref="TelemetryPolicyAttribute" /> declarations of the method and class against the traces that
///     <see cref="TelemetryCapture" /> published during it.
/// </summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TracePerTestAttribute : TestClassAttribute
{
    public const string ActivityName = "nami.test";

    public override TestMethodAttribute GetTestMethodAttribute(TestMethodAttribute testMethodAttribute)
    {
        ArgumentNullException.ThrowIfNull(testMethodAttribute);
        return new TracePerTestMethodAttribute(testMethodAttribute);
    }
}

internal sealed class TracePerTestMethodAttribute(
    TestMethodAttribute inner,
    [CallerFilePath] string file = "",
    [CallerLineNumber] int line = -1) : TestMethodAttribute(file, line)
{
    public override async Task<TestResult[]> ExecuteAsync(ITestMethod testMethod)
    {
        ArgumentNullException.ThrowIfNull(testMethod);
        using var root = TelemetryActivity.Start();
        using var invocation = TelemetryInvocation.Begin(TelemetryPolicyAttribute.DeclaredOn(testMethod.MethodInfo));
        var results = await inner.ExecuteAsync(testMethod).ConfigureAwait(false);
        invocation.Verify(results, TempFileEvidenceStore.Instance);
        return results;
    }
}
