using Fallout.Common;
using Fallout.Common.Tooling;
using Fallout.Components;

namespace Nami.Build;

#pragma warning disable CA1822 // Fallout discovers instance target properties.
sealed class Build : FalloutBuild, IHasConfiguration
{
    private static readonly string[] s_generatedDirectories = ["internal/nami.contracts/Nami", "contracts/json-schema", "contracts/openapi"];
    string Configuration => ((IHasConfiguration)this).Configuration;
    static string CpuLimit => $"-maxcpucount:{Environment.GetEnvironmentVariable("NAMI_BUILD_WORKERS") ?? "2"}";
    static void Run(string tool, string arguments, string? directory = null) =>
        ProcessTasks.StartProcess(tool, arguments, directory ?? RootDirectory.ToString(), logOutput: true).AssertZeroExitCode();
    static string BinaryLog(string name)
    {
        var directory = (RootDirectory / "artifacts" / "binlogs").ToString();
        Directory.CreateDirectory(directory);
        return $"-bl:\"{Path.Combine(directory, name + "-{}.binlog")}\"";
    }

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
        .Executes(() => Run("dotnet", $"build nami.slnx -c {Configuration} {CpuLimit} -nodeReuse:false -p:UseSharedCompilation=false {BinaryLog("compile")}"));
    public Target BuildDiagnostics => d => d.Description("Verify build and inspection diagnostic reporting")
        .DependsOn(Compile)
        .Executes(() => Run("bash", $"eng/test.sh --suite Integration --configuration {Configuration} --no-build --filter FullyQualifiedName~BuildDiagnosticsTests"));
    public Target InspectCode => d => d.Description("Inspect the entire solution and reject findings or incomplete analysis")
        .DependsOn(Compile)
        .Executes(() => Run("bash", $"eng/inspect.sh --configuration {Configuration} --no-build"));
    public Target Test => d => d.Description("Run the .NET verification suite")
        .DependsOn(Compile).DependsOn(PublishNative)
        .Executes(() => Run("bash", $"eng/test.sh --configuration {Configuration} --no-build --no-restore"));
    public Target PublishNative => d => d.Description("Compile the NativeAOT application")
        .DependsOn(Compile)
        .Executes(() => Run("dotnet", $"publish src/nami/nami.csproj -c {Configuration} -o artifacts/publish {CpuLimit} -nodeReuse:false -p:UseSharedCompilation=false {BinaryLog("publish")}"));
    public Target Conformance => d => d.Description("Run the end-to-end proof against the published native binary")
        .DependsOn(Compile).DependsOn(PublishNative)
        .Executes(() => Run("bash", $"eng/test.sh --suite EndToEnd --configuration {Configuration} --no-build --no-restore"));
    public Target Check => d => d.Description("Registry, contract parity, tests and NativeAOT conformance")
        .DependsOn(Registry).DependsOn(VerifyContracts).DependsOn(Test);
    public Target Publish => d => d.Description("Verify and publish the NativeAOT binary")
        .DependsOn(Check);
    public static int Main() => Execute<Build>(static x => x.Check);
}
