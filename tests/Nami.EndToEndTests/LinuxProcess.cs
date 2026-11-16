using System.Globalization;
using Nami.Tests.Infrastructure;

namespace Nami.EndToEndTests;

/// <summary>Process facts from <c>/proc</c>, for tests that watch the host's engine child.</summary>
internal static class LinuxProcess
{
    private static readonly bool s_available = Directory.Exists("/proc/self");

    /// <summary>
    ///     Every fact here comes from <c>/proc</c>. On a machine without it (macOS) a test that asks for one is
    ///     skipped with this reason instead of failing on a missing directory.
    /// </summary>
    private static void Require()
    {
        if (!s_available) Assert.Inconclusive("reads /proc; Linux only");
    }

    /// <summary>The single running child of <paramref name="parent" /> whose executable is <paramref name="executable" />.</summary>
    internal static int ChildOf(int parent, string executable)
    {
        Require();
        var target = Path.GetFullPath(executable);
        var matches = new List<int>();
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)) continue;
            try
            {
                if (ParentOf(pid) == parent && new FileInfo($"/proc/{pid}/exe").LinkTarget == target) matches.Add(pid);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return matches.Count == 1
            ? matches[0]
            : throw new InvalidOperationException($"Expected one {target} child of {parent}; found {matches.Count}.");
    }

    internal static int ParentOf(int pid)
    {
        Require();
        // The command name in field 2 may contain spaces; the fields after its closing parenthesis do not.
        var stat = File.ReadAllText($"/proc/{pid}/stat");
        var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
        return int.Parse(fields[1], CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     The processes that still run with <paramref name="directory" /> as their working directory, without
    ///     <paramref name="owner" />. A host started there hands the directory on to its engine and to anything
    ///     else it starts, so this finds what a host left behind whatever its parent is now. A zombie no longer runs.
    /// </summary>
    internal static int[] RunningIn(string directory, int owner)
    {
        Require();
        var target = Path.GetFullPath(directory);
        var found = new List<int>();
        foreach (var entry in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(entry), NumberStyles.None, CultureInfo.InvariantCulture, out var pid) ||
                pid == owner) continue;
            try
            {
                if (new FileInfo($"/proc/{pid}/cwd").LinkTarget != target) continue;
                var stat = File.ReadAllText($"/proc/{pid}/stat");
                if (stat[stat.LastIndexOf(')') + 2] != 'Z') found.Add(pid);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return [.. found];
    }

    /// <summary>
    ///     Fails unless nothing but <paramref name="owner" /> runs in <paramref name="directory" /> within the
    ///     deadline; a process that was just killed needs a moment to end. What is left is named and then killed.
    /// </summary>
    internal static async Task AssertNothingLeftInAsync(string directory, int owner, TimeSpan deadline,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(deadline);
        int[] left;
        while ((left = RunningIn(directory, owner)).Length > 0)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        var names = new List<string>();
        foreach (var pid in left)
        {
            try
            {
                names.Add($"{pid} {new FileInfo($"/proc/{pid}/exe").LinkTarget}");
                using var process = Process.GetProcessById(pid);
                process.Kill();
            }
            catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException)
            {
                // It ended between the search and here.
            }
        }

        Assert.IsEmpty(names, "The host left processes behind: " + string.Join(", ", names));
    }

    internal static async Task SignalAsync(int pid, string signal, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync("kill", ["-" + signal, pid.ToString(CultureInfo.InvariantCulture)], null,
            cancellationToken);
        Assert.AreEqual(0, result.ExitCode, result.Error);
    }
}
