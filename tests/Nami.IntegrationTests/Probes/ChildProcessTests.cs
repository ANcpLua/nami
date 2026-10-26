using System.IO.Pipes;
using System.Text;

namespace Nami.IntegrationTests.Probes;

[TestClass]
public sealed class ChildProcessTests(TestContext context) : IAsyncDisposable
{
    private readonly StringBuilder _observed = new();
    private Process? _descendant;

    public async ValueTask DisposeAsync()
    {
        if (_descendant is null) return;
        using (_descendant)
        {
            if (_descendant.HasExited) return;
            _descendant.Kill(true);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10), TimeProvider.System);
            await _descendant.WaitForExitAsync(deadline.Token);
        }
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task OwnedProcess_LongTemporaryDirectory_ConnectsItsControlPipe()
    {
        var directory = Path.Combine(context.TestTempDirectory!, new string('p', 80));
        Directory.CreateDirectory(directory);
        var name = OwnedProcess.CreatePipeName(directory);
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        var accepted = server.WaitForConnectionAsync(context.CancellationToken);
        await client.ConnectAsync(context.CancellationToken);
        await accepted;
        Assert.IsTrue(server.IsConnected);
        Assert.IsTrue(client.IsConnected);
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task ChildProcess_NonzeroExit_RetainsBothOutputStreams()
    {
        var result = await ChildProcess.RunAsync(PowerShell(
                "[Console]::Out.WriteLine('process-output'); [Console]::Error.WriteLine('process-error'); exit 7"),
            "output probe", context.CancellationToken);
        Assert.AreEqual(7, result.ExitCode);
        Assert.Contains("process-output", result.Output);
        Assert.Contains("process-error", result.Error);
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task ChildProcess_CancelledAfterObservedReadiness_JoinsAndRetainsDiagnostics()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new StringBuilder();
        var execution = ChildProcess.RunAsync(PowerShell(
                "[Console]::Error.WriteLine('process-error'); [Console]::Out.WriteLine('process-ready'); Start-Sleep -Seconds 600"),
            "cancellation probe", cancellation.Token, new ChildProcessOptions
            {
                StandardOutputReceived = chunk =>
                {
                    if (output.Append(chunk).ToString().Contains("process-ready", StringComparison.Ordinal))
                        ready.TrySetResult();
                }
            });
        await ready.Task.WaitAsync(context.CancellationToken);
        await cancellation.CancelAsync();
        var failure = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => execution);
        Assert.Contains("process-ready", failure.Message);
        Assert.Contains("process-error", failure.Message);
        Assert.IsInstanceOfType<int>(failure.Data["ExitCode"]);
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task ChildProcess_ExecutionDeadlineExpires_TerminatesAndRetainsOutput()
    {
        var failure = await Assert.ThrowsExactlyAsync<TimeoutException>(() => ChildProcess.RunAsync(PowerShell(
                "[Console]::Out.WriteLine('deadline-output'); [Console]::Error.WriteLine('deadline-error'); Start-Sleep -Seconds 600"),
            "deadline probe", context.CancellationToken,
            new ChildProcessOptions { ExecutionTimeout = TimeSpan.FromSeconds(5) }));
        Assert.Contains("execution deadline", failure.Message);
        Assert.Contains("deadline-output", failure.Message);
        Assert.Contains("deadline-error", failure.Message);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task ChildProcess_LeaderExitsBeforeDescendant_TerminatesTheDescendant(bool inheritOutput)
    {
        var marker = Path.Combine(context.TestTempDirectory!, "descendant-ready.txt");
        var childScript = $"[IO.File]::WriteAllText('{Quote(marker)}', $PID.ToString()); Start-Sleep -Seconds 600";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(childScript));
        var script = "$start = [Diagnostics.ProcessStartInfo]::new('pwsh'); $start.UseShellExecute = $false; "
                     + $"$start.RedirectStandardOutput = ${(!inheritOutput).ToString().ToLowerInvariant()}; "
                     + $"$start.RedirectStandardError = ${(!inheritOutput).ToString().ToLowerInvariant()}; "
                     + $"$start.Arguments = '-NoProfile -NonInteractive -EncodedCommand {encoded}'; "
                     + "$child = [Diagnostics.Process]::Start($start); "
                     + $"while (![IO.File]::Exists('{Quote(marker)}')) {{ Start-Sleep -Milliseconds 10 }}; "
                     + "[Console]::Out.WriteLine('descendant:' + $child.Id); exit 0";
        var result = await ChildProcess.RunAsync(PowerShell(script), "descendant probe", context.CancellationToken,
            new ChildProcessOptions
            {
                ExecutionTimeout = TimeSpan.FromSeconds(10),
                StandardOutputReceived = ObserveDescendant
            });
        Assert.AreEqual(0, result.ExitCode);
        Assert.Contains("descendant:", result.Output);
        ObserveDescendant(result.Output);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5), TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, deadline.Token);
        if (_descendant is not null) await _descendant.WaitForExitAsync(linked.Token);
        Assert.IsTrue(_descendant is null || _descendant.HasExited);
    }

    private void ObserveDescendant(string output)
    {
        var captured = _observed.Append(output).ToString();
        var marker = captured.IndexOf("descendant:", StringComparison.Ordinal);
        if (marker < 0 || _descendant is not null) return;
        var end = captured.IndexOf('\n', marker);
        if (end < 0) return;
        var value = captured[(marker + "descendant:".Length)..end].Trim();
        if (!int.TryParse(value, out var id)) return;
        try
        {
            _descendant = Process.GetProcessById(id);
        }
        catch (ArgumentException)
        {
        }
    }

    private static string Quote(string text)
    {
        return text.Replace("'", "''", StringComparison.Ordinal);
    }

    private static ProcessStartInfo PowerShell(string script)
    {
        var start = new ProcessStartInfo("pwsh");
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", script })
            start.ArgumentList.Add(argument);
        return start;
    }
}
