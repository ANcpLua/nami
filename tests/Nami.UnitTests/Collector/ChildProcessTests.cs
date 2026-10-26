namespace Nami.UnitTests.Collector;

[TestClass]
[TestCategory("Fast")]
public class ChildProcessTests
{
    [TestMethod]
    public async Task Should_ObserveCancellationBeforeStarting_When_TheTokenIsAlreadyCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        _ = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            ChildProcess.RunAsync(new ProcessStartInfo("missing-child-process"), "unused child", cancellation.Token));
    }

    [TestMethod]
    public async Task Should_RejectAnUnboundedShutdown_When_TheDeadlineIsNotPositive()
    {
        _ = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            ChildProcess.RunAsync(new ProcessStartInfo("missing-child-process"), "unused child", CancellationToken.None,
                new ChildProcessOptions { ShutdownTimeout = TimeSpan.Zero }));
    }
}
