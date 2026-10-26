using System.Runtime.CompilerServices;

namespace Nami.UnitTests;

[TestClass]
[TestCategory("Fast")]
public class RunnerExecutionTests
{
#if NAMI_NATIVE_TEST_RUNNER
    private const bool DynamicCodeExpected = false;
#else
    private const bool DynamicCodeExpected = true;
#endif

    [TestMethod]
    public void Should_MatchRuntimeCompilationMode_When_RunnerStarts()
    {
        Assert.AreEqual(DynamicCodeExpected, RuntimeFeature.IsDynamicCodeSupported);
        Assert.AreEqual(DynamicCodeExpected, RuntimeFeature.IsDynamicCodeCompiled);
    }

    [TestMethod]
    public void Should_RejectPolicyVerification_When_TestClassCannotBeResolved()
    {
        const string MissingClass = "Nami.UnitTests.MissingPolicyTestClass";
        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            TelemetryPolicyAttribute.RejectUnverifiedPolicies(MissingClass));
        Assert.AreEqual(RuntimeFeature.IsDynamicCodeSupported
                ? $"Cannot verify telemetry policies because the active test class '{MissingClass}' could not be resolved. Use [TracePerTest] for telemetry publication."
                : "Telemetry published outside a [TracePerTest] invocation cannot verify policies in a NativeAOT test runner. Use [TracePerTest] for telemetry publication.",
            error.Message);
    }
}
