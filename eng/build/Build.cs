using Fallout.Common;
using Fallout.Common.Tooling;
using Fallout.Components;
using System.Xml.Linq;

namespace Nami.Build;

#pragma warning disable CA1822 // Fallout discovers instance target properties.
sealed class Build : FalloutBuild, IHasConfiguration
{
    private static readonly string[] s_generatedDirectories = ["internal/nami.contracts/Nami", "contracts/json-schema", "contracts/openapi"];
    string Configuration => ((IHasConfiguration)this).Configuration;
    static void Run(string tool, string arguments, string? directory = null) =>
        ProcessTasks.StartProcess(tool, arguments, directory ?? RootDirectory.ToString(), logOutput: true).AssertZeroExitCode();

    public Target Registry => d => d.Description("Validate the pinned registry and verify deterministic generated conventions")
        .Executes(() =>
        {
            Run("bash", "registry/generate.sh --check");
            Run("cargo", "build --manifest-path crates/nami-semconv/Cargo.toml --locked --offline --target-dir artifacts/cargo");
        });
    public Target Contracts => d => d.Description("Generate C# contracts and the six tools' JSON Schemas")
        .Executes(() => { Run("bun", "install --frozen-lockfile", (RootDirectory / "contracts").ToString()); Run("bun", "run compile", (RootDirectory / "contracts").ToString()); });
    public Target VerifyContracts => d => d.Description("Verify committed contract generation is current")
        .Executes(() =>
        {
            var before = Snapshot();
            Run("bun", "install --frozen-lockfile", (RootDirectory / "contracts").ToString());
            Run("bun", "run compile", (RootDirectory / "contracts").ToString());
            var after = Snapshot();
            if (!before.SequenceEqual(after)) throw new InvalidOperationException("Generated contracts changed. Run Contracts and commit the outputs.");
        });
    static string[] Snapshot() => s_generatedDirectories
        .SelectMany(p => Directory.Exists(RootDirectory / p) ? Directory.EnumerateFiles(RootDirectory / p, "*", SearchOption.AllDirectories) : [])
        .Order(StringComparer.Ordinal).Select(p => p + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p)))).ToArray();
    public Target Compile => d => d.Description("Build the Nami solution")
        .Executes(() => Run("dotnet", $"build nami.slnx -c {Configuration} -nodeReuse:false -p:UseSharedCompilation=false"));
    public Target Test => d => d.Description("Run the .NET verification suite")
        .DependsOn(Compile)
        .Executes(() =>
        {
            var results = RootDirectory / "artifacts" / "test-results";
            if (Directory.Exists(results)) Directory.Delete(results, recursive: true);
            Run("dotnet", $"test --solution nami.slnx --configuration {Configuration} --no-build --no-restore --max-parallel-test-modules 1 --report-trx --results-directory \"{results}\"");
            XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
            var executions = Directory.EnumerateFiles(results, "*.trx", SearchOption.AllDirectories)
                .Select(XDocument.Load).SelectMany(document => document.Descendants(ns + "UnitTestResult")).ToArray();
            if (executions.Length == 0 || executions.Any(result => result.Attribute("outcome")?.Value is not "Passed"))
                throw new InvalidOperationException("The gate requires discovered, passing test executions; inspect artifacts/test-results.");
        });
    public Target PublishNative => d => d.Description("Compile the NativeAOT application")
        .DependsOn(Compile)
        .Executes(() => Run("dotnet", $"publish src/nami/nami.csproj -c {Configuration} -o artifacts/publish -nodeReuse:false -p:UseSharedCompilation=false"));
    public Target Conformance => d => d.Description("Run the end-to-end proof against the published native binary")
        .DependsOn(Test).DependsOn(PublishNative)
        .Executes(() =>
        {
            Run("dotnet", $"publish tests/Nami.Conformance/Nami.Conformance.csproj -c {Configuration} -o artifacts/conformance -nodeReuse:false -p:UseSharedCompilation=false");
            var executable = RootDirectory / "artifacts" / "conformance" / (OperatingSystem.IsWindows() ? "Nami.Conformance.exe" : "Nami.Conformance");
            var application = RootDirectory / "artifacts" / "publish" / (OperatingSystem.IsWindows() ? "nami.exe" : "nami");
            Run(executable, $"\"{application}\"");
        });
    public Target Check => d => d.Description("Registry, contract parity, tests and NativeAOT conformance")
        .DependsOn(Registry).DependsOn(VerifyContracts).DependsOn(Conformance);
    public Target Publish => d => d.Description("Verify and publish the NativeAOT binary")
        .DependsOn(Check);
    public static int Main() => Execute<Build>(static x => x.Check);
}
