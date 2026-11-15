using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Nami.IntegrationTests.Fuzz;

/// <summary>
///     How long a fuzz test runs. The gate runs each test as a smoke run: a fixed seed and a few dozen cases. A longer
///     round sets <c>NAMI_FUZZ_SEED</c>, <c>NAMI_FUZZ_ITERATIONS</c> and <c>NAMI_FUZZ_SECONDS</c>; <c>NAMI_FUZZ_CORPUS</c>
///     adds a directory of OTLP bodies to the seeds, and <c>NAMI_FUZZ_OUT</c> keeps every case in flight and one
///     reproducer per distinct finding there.
/// </summary>
internal sealed record FuzzSettings(int Seed, int Iterations, TimeSpan Budget, string? Corpus, string? Output, bool Strict)
{
    internal const int SmokeSeed = 20261115;

    internal static FuzzSettings Read(int smokeIterations) => new(
        Number("NAMI_FUZZ_SEED") ?? SmokeSeed,
        Number("NAMI_FUZZ_ITERATIONS") ?? smokeIterations,
        TimeSpan.FromSeconds(Number("NAMI_FUZZ_SECONDS") ?? 90),
        Environment.GetEnvironmentVariable("NAMI_FUZZ_CORPUS"),
        Environment.GetEnvironmentVariable("NAMI_FUZZ_OUT"),
        Number("NAMI_FUZZ_SECONDS") is not null);

    private static int? Number(string name) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}

/// <summary>Counts cases, bounds their time and allocation, and keeps one reproducer per distinct finding.</summary>
internal sealed class FuzzReport(string target, FuzzSettings settings, TestContext context)
{
    // Defects already filed and owned elsewhere: reported with their ledger id, never failing a run.
    private static readonly Dictionary<string, string> s_knownOpen = new(StringComparer.Ordinal)
    {
        // A repeated span id updates end_time_unix_nano but keeps the first start (StorageRows.g.cs ON CONFLICT).
        ["rule:stored span ends before it starts"] = "engine audit A5"
    };

    // A case that runs this long is a hang; the run stops because the case still owns the store.
    private static readonly TimeSpan s_hang = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan s_slow = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, (FuzzFinding Finding, string Note, int Hits, string? File)> _findings =
        new(StringComparer.Ordinal);

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    internal int Cases { get; private set; }

    internal bool Continue => !Stopped && Cases < settings.Iterations && _clock.Elapsed < settings.Budget;

    private bool Stopped { get; set; }

    /// <summary>Keeps the case in flight, so a case that ends the process (a stack overflow) is still on disk.</summary>
    internal void Begin(byte[] body, string note)
    {
        Cases++;
        if (settings.Output is not { } output) return;
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(output, target + "-current.bin"), body);
        File.WriteAllText(Path.Combine(output, target + "-current.txt"), note);
    }

    internal async Task<List<FuzzFinding>> MeasureAsync(Func<Task<List<FuzzFinding>>> run, long inputBytes)
    {
        var started = Stopwatch.GetTimestamp();
        var allocated = GC.GetTotalAllocatedBytes();
        List<FuzzFinding> findings;
        try
        {
            findings = await run().WaitAsync(s_hang, context.CancellationToken);
        }
        catch (TimeoutException)
        {
            Stopped = true;
            return [new FuzzFinding("hang", "hang", $"no answer within {s_hang.TotalSeconds} s")];
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var bytes = GC.GetTotalAllocatedBytes() - allocated;
        if (elapsed > s_slow)
            findings.Add(new FuzzFinding("slow", "slow", $"{elapsed.TotalSeconds:F1} s for {inputBytes} input bytes"));
        // Concurrent tests share the counter; only an allocation far beyond any copy of the input counts.
        if (bytes > Math.Max(1L << 30, inputBytes * 256))
            findings.Add(new FuzzFinding("allocation", "allocation", $"{bytes >> 20} MiB allocated for {inputBytes} input bytes"));
        return findings;
    }

    internal bool IsNew(FuzzFinding finding) => !_findings.ContainsKey(finding.Signature);

    /// <summary>Keeps a minimized reproducer next to the first case of a finding, in a round that keeps cases.</summary>
    internal void SaveMinimized(FuzzFinding finding, byte[] body, string description)
    {
        if (settings.Output is not { } output) return;
        Directory.CreateDirectory(output);
        var name = $"{target}-{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(finding.Signature)))[..12]}";
        File.WriteAllBytes(Path.Combine(output, name + ".min.bin"), body);
        File.WriteAllText(Path.Combine(output, name + ".min.txt"), $"{finding.Signature}\n\n{description}\n");
    }

    internal void Record(IEnumerable<FuzzFinding> findings, byte[] body, string note)
    {
        foreach (var finding in findings)
        {
            if (_findings.TryGetValue(finding.Signature, out var seen))
            {
                _findings[finding.Signature] = seen with { Hits = seen.Hits + 1 };
                continue;
            }

            string? file = null;
            if (settings.Output is { } output)
            {
                var name = $"{target}-{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(finding.Signature)))[..12]}";
                file = Path.Combine(output, name + ".bin");
                File.WriteAllBytes(file, body);
                File.WriteAllText(Path.Combine(output, name + ".txt"),
                    $"{finding.Signature}\n{note}\n\n{finding.Detail}\n");
            }

            _findings[finding.Signature] = (finding, note, 1, file);
        }
    }

    /// <summary>Fails with every distinct finding, each with the case that first showed it.</summary>
    internal void AssertClean()
    {
        var summary = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"{target}: seed {settings.Seed}, {Cases} cases in {_clock.Elapsed.TotalSeconds:F1} s, ")
            .Append(CultureInfo.InvariantCulture, $"{_findings.Count} distinct findings\n");
        foreach (var (signature, (finding, note, hits, file)) in _findings.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var known = s_knownOpen.TryGetValue(signature, out var ledger) ? $" known open: {ledger}" : "";
            summary.Append(CultureInfo.InvariantCulture, $"\n[{finding.Stage}] {signature} (x{hits}){known}\n  case: {note}\n");
            if (file is not null) summary.Append(CultureInfo.InvariantCulture, $"  reproducer: {file}\n");
            summary.Append("  ").Append(finding.Detail.Length > 1500 ? finding.Detail[..1500] + "..." : finding.Detail).Append('\n');
        }

        context.WriteLine(summary.ToString());
        // Time and allocation depend on the machine and on the tests running beside this one: a smoke run reports
        // them, a round with its own budget (NAMI_FUZZ_SECONDS) fails on them.
        Assert.IsEmpty(_findings.Values.Where(entry => !s_knownOpen.ContainsKey(entry.Finding.Signature) &&
                                                       (settings.Strict || entry.Finding.Stage is not ("slow" or "allocation"))),
            summary.ToString());
    }
}
