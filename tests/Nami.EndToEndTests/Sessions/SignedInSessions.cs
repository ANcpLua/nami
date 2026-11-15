namespace Nami.EndToEndTests.Sessions;

/// <summary>One tool call or command as the fixture recorded it, in time order.</summary>
internal sealed record ExpectedCall(string Name, bool Success);

/// <summary>
///     What nami must answer for one session of the signed-in capture. Tokens come from the CLI's own result
///     (<c>cli-results.json</c>), cost from the sum of <c>cost_usd_micros</c> that the fixture README records, tool calls,
///     commands and commits from the README's session table and the <c>tool_result</c> events. The duration bounds hold
///     the CLI's duration, the active time and the span from first to last record of the session.
/// </summary>
internal sealed record ExpectedSession(
    string Label,
    string Id,
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheCreationTokens,
    long CostUsdMicros,
    IReadOnlyList<ExpectedCall> ToolCalls,
    IReadOnlyList<ExpectedCall> Commands,
    IReadOnlyList<string> Commits,
    double MinimumDurationMs,
    double MaximumDurationMs)
{
    internal int ToolFailures => ToolCalls.Count(static call => !call.Success);
}

internal static class SignedInSessions
{
    internal const string Agent = "fixture-builder";
    internal const string Model = "claude-haiku-5-5";
    internal const string Effort = "medium";

    /// <summary>A window start before the capture (12:33:13 to 12:33:59 UTC on 15 Nov 2026).</summary>
    internal const string WindowStart = "2026-11-15T12:30:00Z";

    internal const string FirstCommit = "c0ffee0000000000000000000000000000000001";
    internal const string SecondCommit = "c0ffee0000000000000000000000000000000002";

    /// <summary>s1's passing <c>python3 -m unittest -q</c> before its commit, the template of every planted run.</summary>
    internal const string PassingTestRunOfS1 = "toolu_01FIXTURE000000000000004";

    private const string UnitTests = "python3 -m unittest -q";

    internal static readonly ExpectedSession S1 = new("s1", "00000000-0000-4000-8000-00000000000c",
        12, 1042, 106057, 8730, 3329,
        [
            new("Bash", false), new("Read", true), new("Edit", true), new("Bash", true), new("Bash", true)
        ],
        [
            new(UnitTests, false), new(UnitTests, true),
            new("git commit -am \"Fix add in calc.py\" -m \"Co-Authored-By: Claude Haiku 5.5 <scrubbed-email-2>\" && git log --oneline -1", true)
        ],
        [FirstCommit], 13000, 15500);

    /// <summary>Two of s2's ten tool calls and two of its nine requests belong to its background subagent.</summary>
    internal static readonly ExpectedSession S2 = new("s2", "00000000-0000-4000-8000-000000000018",
        18, 1697, 141738, 21711, 5762,
        [
            new("Agent", true), new("Read", true), new("Read", true), new("Read", true), new("Read", true),
            new("Edit", true), new("Edit", true), new("Edit", true), new("Bash", true), new("Bash", true)
        ],
        [
            new(UnitTests, true),
            new("git commit -am \"Add sub to calc.py\" -m \"Co-Authored-By: Claude Haiku 5.5 <scrubbed-email-2>\" && git log --oneline -1", true)
        ],
        [SecondCommit], 10000, 17500);

    /// <summary>s3 piped its test run into tail and made no commit.</summary>
    internal static readonly ExpectedSession S3 = new("s3", "00000000-0000-4000-8000-000000000029",
        4, 123, 32284, 4465, 1277,
        [new("Bash", true)],
        [new(UnitTests + " 2>&1 | tail -n 5", true)],
        [], 3000, 5000);

    internal static IReadOnlyList<ExpectedSession> All { get; } = [S1, S2, S3];

    internal static ExpectedSession ByLabel(string label) => All.Single(session => session.Label == label);
}
