using Nami.Tests.Infrastructure;
using static Nami.EndToEndTests.Sessions.SessionsContract;

namespace Nami.EndToEndTests.Sessions;

/// <summary>
///     Finish-line item 4. A test claim is verified only when the session ran the gate exactly (a foreground Bash
///     <c>tool_result</c> whose <c>full_command</c> is <c>./eng/test.sh</c> or <c>./eng/build.sh Check ...</c>, without a
///     pipe, <c>&amp;&amp;</c>, <c>;</c> or <c>|| true</c>) with success at the commit the claim names; contradicted when a
///     later run at that commit failed; unverified otherwise. A session's claims are its commits. Every case is the
///     signed-in capture with runs planted into s1 after its commit <c>c0ffee…01</c>: copies of its passing test run
///     with another command and outcome, built in memory. Test lane m2-acceptance until M2 lands.
/// </summary>
[TestClass]
[TestCategory("EndToEnd")]
[TestCategory("M2Acceptance")]
public sealed class ClaimCheckAcceptanceTests(TestContext context)
{
    private static readonly TimeSpan s_first = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan s_second = TimeSpan.FromSeconds(4);

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow("./eng/test.sh", DisplayName = "./eng/test.sh")]
    [DataRow("./eng/build.sh Check --Configuration Release", DisplayName = "./eng/build.sh Check --Configuration Release")]
    public async Task ClaimCheck_GateRunWithSuccessAtTheClaimedCommit_IsVerified(string gate)
    {
        await AssertClaimAsync(Verified, Run(gate, true, s_first, 101));
    }

    /// <summary>The planted failing run: s1 passes the gate at its commit, then fails it at the same commit.</summary>
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task ClaimCheck_PlantedLaterFailingRunAtTheSameCommit_ContradictsTheClaim()
    {
        await AssertClaimAsync(Contradicted, Run("./eng/test.sh", true, s_first, 101),
            Run("./eng/test.sh", false, s_second, 103));
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task ClaimCheck_FailingRunFollowedByAPassingRunAtTheSameCommit_IsVerified()
    {
        await AssertClaimAsync(Verified, Run("./eng/test.sh", false, s_first, 101),
            Run("./eng/test.sh", true, s_second, 103));
    }

    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow("./eng/test.sh 2>&1 | tail -n 5", DisplayName = "Piped into tail")]
    [DataRow("./eng/test.sh && echo done", DisplayName = "Chained with &&")]
    [DataRow("./eng/test.sh; echo done", DisplayName = "Chained with ;")]
    [DataRow("./eng/test.sh || true", DisplayName = "Failure masked with || true")]
    [DataRow("./eng/test.sh --suite Unit", DisplayName = "One suite only")]
    public async Task ClaimCheck_SucceedingCommandThatIsNotExactlyTheGate_LeavesTheClaimUnverified(string command)
    {
        await AssertClaimAsync(Unverified, Run(command, true, s_first, 101));
    }

    /// <summary>The gate passed on the working tree before the commit existed, so it did not run at the commit.</summary>
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task ClaimCheck_PassingGateRunBeforeTheCommit_LeavesTheClaimUnverified()
    {
        var bodies = ClaudeCodeCapture.WithCommand(ClaudeCodeCapture.SignedIn(), SignedInSessions.PassingTestRunOfS1,
            "./eng/test.sh");
        var expectedCommands = SignedInSessions.S1.Commands.ToArray();
        expectedCommands[1] = new ExpectedCall("./eng/test.sh", true);
        await AssertClaimAsync(Unverified, bodies, expectedCommands);
    }

    private static PlantedRun Run(string command, bool success, TimeSpan after, long sequence) =>
        new(command, success, after, sequence, "toolu_01PLANTED" + sequence.ToString("D13", System.Globalization.CultureInfo.InvariantCulture));

    private async Task AssertClaimAsync(string status, params PlantedRun[] runs)
    {
        var capture = ClaudeCodeCapture.SignedIn();
        var bodies = capture.Concat(runs.Select(run =>
            ClaudeCodeCapture.PlantRun(capture, SignedInSessions.PassingTestRunOfS1, run))).ToArray();
        IEnumerable<ExpectedCall> commands =
            [.. SignedInSessions.S1.Commands, .. runs.Select(static run => new ExpectedCall(run.Command, run.Success))];
        await AssertClaimAsync(status, bodies, [.. commands]);
    }

    private async Task AssertClaimAsync(string status, IReadOnlyList<OtlpBody> bodies, IReadOnlyList<ExpectedCall> commands)
    {
        var directory = context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");
        await using var host = await SessionTools.IngestAsync(directory, bodies, context.CancellationToken);
        await SessionTools.RequireSessionToolsAsync(host, context.CancellationToken);
        var result = await SessionTools.CallAsync(host, GetTool, SessionTools.Get(SignedInSessions.S1.Id),
            context.CancellationToken);
        var where = $"{GetTool} s1";
        var session = SessionTools.Member(result, SessionMember, where);
        // The planted runs reached nami as commands of s1, so the claim below is judged on them.
        SessionsGetAcceptanceTests.AssertCalls(commands, SessionTools.Member(session, Commands, where), Command,
            where + " commands");
        Assert.AreSequenceEqual(new[] { (SignedInSessions.FirstCommit, status) },
            SessionsGetAcceptanceTests.Claims(session, where), where + " test claim of " + SignedInSessions.FirstCommit);
    }
}
