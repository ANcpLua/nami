using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;

namespace Nami.Tests;

/// <summary>Runs a command with bounded execution, redirected output and ownership of its descendants.</summary>
internal static class ChildProcess
{
    public static async Task<(int ExitCode, string Output, string Error)> RunAsync(ProcessStartInfo start,
        string description,
        CancellationToken cancellationToken, ChildProcessOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(start);
        options ??= new ChildProcessOptions();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.ShutdownTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.ExecutionTimeout, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.UseShellExecute = false;
        using var execution = new CancellationTokenSource(options.ExecutionTimeout, options.TimeProvider);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, execution.Token);
        OwnedProcess process;
        try
        {
            process = await OwnedProcess.StartAsync(start, lifetime.Token, options.TimeProvider).ConfigureAwait(false);
        }
        catch (OperationCanceledException failure) when (execution.IsCancellationRequested &&
                                                         !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"{description} exceeded its execution deadline of {options.ExecutionTimeout} during startup.",
                failure);
        }

        await using var ownership = process;
        using var shutdown = new CancellationTokenSource(Timeout.InfiniteTimeSpan, options.TimeProvider);
        var output = new OutputCapture(process.StandardOutput, options.StandardOutputReceived, shutdown.Token);
        var error = new OutputCapture(process.StandardError, null, shutdown.Token);
        Exception? executionFailure = null;
        try
        {
            await process.WaitForExitAsync(lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            executionFailure = exception;
        }

        var cleanupFailures = new List<Exception>();
        shutdown.CancelAfter(options.ShutdownTimeout);
        try
        {
            await process.KillAsync(shutdown.Token).ConfigureAwait(false);
            await Task.WhenAll(output.Completion, error.Completion).WaitAsync(shutdown.Token).ConfigureAwait(false);
            if (!output.Ended || !error.Ended)
            {
                cleanupFailures.Add(new TimeoutException($"{description} did not close its output streams."
                                                         + Diagnostics(process, output, error)));
            }
        }
        catch (OperationCanceledException exception) when (shutdown.IsCancellationRequested)
        {
            cleanupFailures.Add(new TimeoutException(
                $"{description} did not finish shutdown and output capture within {options.ShutdownTimeout}."
                + Diagnostics(process, output, error), exception));
        }
        catch (Exception exception)
        {
            cleanupFailures.Add(exception);
        }

        try
        {
            await process.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (!cleanupFailures.Contains(exception)) cleanupFailures.Add(exception);
        }

        if (executionFailure is OperationCanceledException cancellation)
        {
            executionFailure = execution.IsCancellationRequested && !cancellationToken.IsCancellationRequested
                ? new TimeoutException($"{description} exceeded its execution deadline of {options.ExecutionTimeout}."
                                       + Diagnostics(process, output, error), cancellation)
                : new OperationCanceledException($"{description} was cancelled and reaped."
                                                 + Diagnostics(process, output, error), cancellation, cancellationToken)
                {
                    Data = { ["ProcessId"] = process.Id, ["ExitCode"] = process.ExitCode }
                };
        }

        if (executionFailure is not null) cleanupFailures.Insert(0, executionFailure);
        switch (cleanupFailures.Count)
        {
            case 1:
                ExceptionDispatchInfo.Capture(cleanupFailures[0]).Throw();
                break;
            case > 1:
                throw new AggregateException($"{description} execution and cleanup failed.", cleanupFailures);
        }

        return (process.ExitCode, output.Text, error.Text);
    }

    private static string Diagnostics(OwnedProcess process, OutputCapture output, OutputCapture error)
    {
        return
            $" Process: {process.Id}; exit code: {(process.HasExited ? process.ExitCode.ToString(CultureInfo.InvariantCulture) : "still running")}"
            + $"\nstdout:\n{Tail(output.Text)}\nstderr:\n{Tail(error.Text)}";
    }

    private static string Tail(string text)
    {
        return string.Join('\n', text.Split('\n').TakeLast(30));
    }

    private sealed class OutputCapture
    {
        private readonly StringBuilder _text = new();

        internal OutputCapture(StreamReader reader, Action<string>? received, CancellationToken cancellationToken)
        {
            Completion = ReadAsync(reader, received, cancellationToken);
        }

        internal Task Completion { get; }
        internal bool Ended { get; private set; }

        internal string Text
        {
            get
            {
                lock (_text) return _text.ToString();
            }
        }

        private async Task ReadAsync(StreamReader reader, Action<string>? received, CancellationToken cancellationToken)
        {
            var buffer = new char[2048];
            try
            {
                while (await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) is var count &&
                       count > 0)
                {
                    var chunk = new string(buffer, 0, count);
                    lock (_text) _text.Append(chunk);
                    received?.Invoke(chunk);
                }

                Ended = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }
}

internal sealed class ChildProcessOptions
{
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
    public TimeSpan ExecutionTimeout { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public Action<string>? StandardOutputReceived { get; init; }
}
