using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Nami.Tests;
using Nami.Tests.Policies;

namespace Nami.IntegrationTests.Probes;

[TracePerTest]
[ExportsCleanly]
public sealed class PolicyCases(TestContext context)
{
    [TestMethod]
    [ConnectedTrace]
    [NoDuplicateSpans]
    [DataRow(PolicyContract.FirstRow)]
    [DataRow(PolicyContract.SecondRow)]
    public Task Policies_SatisfiedOnEachDataRow_KeepTheRowPassing(string row) =>
        ExportAsync(source => source.CreateActivity(row, ActivityKind.Server)?.Start().Dispose());

    [TestMethod]
    [NoDuplicateSpans]
    public Task NoDuplicateSpans_WrappedOperation_Fails() => ExportAsync(source =>
    {
        using var native = source.CreateActivity(PolicyContract.Operation, ActivityKind.Server)?.Start();
        using var wrapper = source.CreateActivity(PolicyContract.Operation, ActivityKind.Server)?.Start();
    });

    [TestMethod]
    [ConnectedTrace]
    public void ConnectedTrace_NoEvidence_Fails()
    {
    }

    [TestMethod]
    [ConnectedTrace]
    public void ConnectedTrace_OriginalFailure_IsKept() => Assert.Fail(PolicyContract.OriginalMessage);

    private async Task ExportAsync(Action<ActivitySource> operation)
    {
        var name = "policy-" + Guid.NewGuid().ToString("N");
        _ = await TelemetryCapture.TraceAsync(capture =>
        {
            using var source = new ActivitySource(name);
            using var provider = Sdk.CreateTracerProviderBuilder().AddSource(name)
                .SetResourceBuilder(ResourceBuilder.CreateEmpty().AddService("policy-probe"))
                .AddOtlpCapture(capture.Endpoint).Build();
            operation(source);
            return Task.CompletedTask;
        }, trace => trace.Spans.Count > 0, cancellationToken: context.CancellationToken).ConfigureAwait(false);
    }
}

[TestClass]
public sealed class UnverifiedPolicyCases(TestContext context)
{
    [TestMethod]
    [ConnectedTrace]
    public async Task PlainTestClass_PolicyAttribute_IsRejected() => _ = await TelemetryCapture.TraceAsync(_ => Task.CompletedTask,
        _ => true, cancellationToken: context.CancellationToken).ConfigureAwait(false);
}
