namespace Nami.IntegrationTests.Probes;

internal static class BuildEvidenceContract
{
    internal const string FirstRow = "first-evidence";
    internal const string SecondRow = "second-evidence";
    internal const string DirectoryMarker = "build-evidence-workspace";
    internal const string RowMarker = "build-evidence-row";
    internal const string Source = "public class Consumer { public MissingType Value; }";
}
