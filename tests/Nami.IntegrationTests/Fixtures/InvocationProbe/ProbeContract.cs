using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace Nami.IntegrationTests.Probes;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
internal static class ProbeContract
{
    internal const string FirstRow = "first";
    internal const string SecondRow = "second";
    internal const string RowMethod = "ProbeRows_EachDataRow_KeepsItsTraceAndOriginalExecutor";
    internal const string AssertionMethod = "Probe_AssertFail_KeepsTheOriginalAssertion";
    internal const string ExceptionMethod = "Probe_Throw_KeepsTheOriginalException";
    internal const string InconclusiveMethod = "Probe_Inconclusive_KeepsTheOriginalResult";
    internal const string AssertionMessage = "probe original assertion";
    internal const string ExceptionMessage = "probe original exception";
    internal const string InconclusiveMessage = "probe original inconclusive";
    internal const string ExecutorMarker = "original-executor";
    internal const string RowMarker = "probe-row";
    internal const string CasesFileName = "ProbeCases.cs";

    internal static string CasesSourcePath => Path.Combine(Path.GetDirectoryName(SourcePath())!, CasesFileName);

    private static string SourcePath([CallerFilePath] string path = "")
    {
        return path;
    }
}

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
internal static class PolicyContract
{
    internal const string FirstRow = "first.row";
    internal const string SecondRow = "second.row";
    internal const string Operation = "GET /probe";
    internal const string OriginalMessage = "probe original policy failure";
    internal const string SatisfiedMethod = "Policies_SatisfiedOnEachDataRow_KeepTheRowPassing";
    internal const string WrappedMethod = "NoDuplicateSpans_WrappedOperation_Fails";
    internal const string MissingMethod = "ConnectedTrace_NoEvidence_Fails";
    internal const string OriginalMethod = "ConnectedTrace_OriginalFailure_IsKept";
    internal const string UnverifiedMethod = "PlainTestClass_PolicyAttribute_IsRejected";
}

internal static class FixtureContract
{
    internal const string FirstRow = "first-fixture";
    internal const string SecondRow = "second-fixture";
    internal const string DirectoryMarker = "owned-fixture-directory";
    internal const string RowMarker = "owned-fixture-row";
    internal const string AssertionMethod = "Fixture_AssertFail_KeepsTheOriginalAssertion";
    internal const string ExceptionMethod = "Fixture_Exception_KeepsTheOriginalException";
    internal const string InconclusiveMethod = "Fixture_Inconclusive_KeepsTheOriginalResult";
    internal const string AssertionMessage = "fixture original assertion";
    internal const string ExceptionMessage = "fixture original exception";
    internal const string InconclusiveMessage = "fixture original inconclusive";
}
