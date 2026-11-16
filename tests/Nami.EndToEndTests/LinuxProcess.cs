using System.Globalization;
using Nami.Tests.Infrastructure;

namespace Nami.EndToEndTests;

/// <summary>Process facts from <c>/proc</c>, for tests that watch the host's engine child.</summary>
internal static class LinuxProcess
{
    /// <summary>The single running child of <paramref name="parent" /> whose executable is <paramref name="executable" />.</summary>
    internal static int ChildOf(int parent, string executable)
    {
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

    private static int ParentOf(int pid)
    {
        // The command name in field 2 may contain spaces; the fields after its closing parenthesis do not.
        var stat = File.ReadAllText($"/proc/{pid}/stat");
        var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
        return int.Parse(fields[1], CultureInfo.InvariantCulture);
    }

    /// <summary>True once the process is gone or a zombie that no longer runs.</summary>
    internal static bool Gone(int pid)
    {
        try
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            return stat[stat.LastIndexOf(')') + 2] == 'Z';
        }
        catch (IOException)
        {
            return true;
        }
    }

    internal static async Task SignalAsync(int pid, string signal, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync("kill", ["-" + signal, pid.ToString(CultureInfo.InvariantCulture)], null,
            cancellationToken);
        Assert.AreEqual(0, result.ExitCode, result.Error);
    }
}
