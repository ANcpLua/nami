using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Nami.Tests;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace Nami.IntegrationTests.Probes;

[TracePerTest]
public sealed class ProbeCases(TestContext context)
{
    [ProbeMethod]
    [DataRow(ProbeContract.FirstRow)]
    [DataRow(ProbeContract.SecondRow)]
    public void ProbeRows_EachDataRow_KeepsItsTraceAndOriginalExecutor(string rowLabel)
    {
        var activity = Activity.Current;
        Assert.IsNotNull(activity);
        Assert.AreEqual(TracePerTestAttribute.ActivityName, activity.OperationName);
        context.WriteLine($"{ProbeContract.RowMarker}|{rowLabel}|{activity.TraceId.ToHexString()}");
    }

    [TestMethod]
    public void Probe_AssertFail_KeepsTheOriginalAssertion() => Assert.Fail(ProbeContract.AssertionMessage);

    [TestMethod]
    public void Probe_Throw_KeepsTheOriginalException() => throw new InvalidOperationException(ProbeContract.ExceptionMessage);

    [TestMethod]
    public void Probe_Inconclusive_KeepsTheOriginalResult() => Assert.Inconclusive(ProbeContract.InconclusiveMessage);
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class ProbeMethodAttribute(
    [CallerFilePath] string path = "", [CallerLineNumber] int line = -1) : TestMethodAttribute(path, line)
{
    [Experimental("MSTESTEXP")]
    public override async Task<TestResult[]> ExecuteAsync(ITestMethod testMethod)
    {
        TestContext.Current!.WriteLine(ProbeContract.ExecutorMarker);
        return await base.ExecuteAsync(testMethod).ConfigureAwait(false);
    }
}
