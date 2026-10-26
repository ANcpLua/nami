namespace Nami.Tests.Infrastructure;

internal sealed record ProcessResult(int ExitCode, string Output, string Error);

internal static class ProcessRunner
{
    internal static string RepositoryRoot
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "nami.slnx")))
                    return dir.FullName;
            }

            throw new DirectoryNotFoundException("Cannot locate nami.slnx above the test executable.");
        }
    }

    internal static async Task<ProcessResult> RunAsync(string file, IEnumerable<string> args,
        string? directory, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var start = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, WorkingDirectory = directory ?? RepositoryRoot
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        if (environment is not null)
        {
            foreach (var pair in environment)
                start.Environment[pair.Key] = pair.Value;
        }

        var result = await ChildProcess.RunAsync(start, file, cancellationToken).ConfigureAwait(false);
        return new ProcessResult(result.ExitCode, result.Output, result.Error);
    }
}
