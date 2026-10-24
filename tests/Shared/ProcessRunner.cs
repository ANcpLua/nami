using System.Diagnostics;

namespace Nami.Tests.Infrastructure;

internal sealed record ProcessResult(int ExitCode, string Output, string Error);

internal static class ProcessRunner
{
    internal static string RepositoryRoot
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "nami.slnx"))) return dir.FullName;
            throw new DirectoryNotFoundException("Cannot locate nami.slnx above the test executable.");
        }
    }

    internal static async Task<ProcessResult> RunAsync(string file, IEnumerable<string> args,
        string? directory, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, WorkingDirectory = directory ?? RepositoryRoot };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Process did not start.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try { await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } }
        return new(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }
}
