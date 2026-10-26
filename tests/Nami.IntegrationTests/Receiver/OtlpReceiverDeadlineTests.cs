using Microsoft.Extensions.Time.Testing;

namespace Nami.IntegrationTests.Receiver;

/// <summary>Drives the OTLP receiver's evidence deadline on a fake clock.</summary>
/// <param name="testContext">The runner context supplies cancellation.</param>
[TestClass]
public sealed class OtlpReceiverDeadlineTests(TestContext testContext)
{
    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task AwaitAsync_EvidenceMissingAtDeadline_ThrowsTimeoutException()
    {
        var clock = new FakeTimeProvider();
        await using var receiver = await OtlpReceiver.StartAsync(OtlpReceiver.DefaultMaxPayloadBytes, clock,
            testContext.CancellationToken);
        var waiting =
            receiver.AwaitAsync(_ => false, OtlpReceiver.DefaultEvidenceTimeout, testContext.CancellationToken);
        clock.Advance(OtlpReceiver.DefaultEvidenceTimeout);
        _ = await Assert.ThrowsExactlyAsync<TimeoutException>(() => waiting);
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task AwaitAsync_EvidenceMissingJustBeforeDeadline_KeepsWaiting()
    {
        var clock = new FakeTimeProvider();
        await using var receiver = await OtlpReceiver.StartAsync(OtlpReceiver.DefaultMaxPayloadBytes, clock,
            testContext.CancellationToken);
        var waiting =
            receiver.AwaitAsync(_ => false, OtlpReceiver.DefaultEvidenceTimeout, testContext.CancellationToken);
        clock.Advance(OtlpReceiver.DefaultEvidenceTimeout - TimeSpan.FromTicks(1));
        Assert.IsFalse(waiting.IsCompleted);
    }
}
