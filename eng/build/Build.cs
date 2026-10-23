using System;
using System.Linq;
using Fallout.Common;
using Fallout.Common.IO;
using Fallout.Common.Tooling;
using Fallout.Common.Tools.DotNet;
using Fallout.Components;

namespace Nami.Build;

interface INamiTests : IHasSolution, IHasConfiguration
{
    Target Test => d => d
        .Description("Run every .NET test project in the solution using the completed build")
        .DependsOn<ICompile>(static x => x.Compile)
        .Executes(() => DotNetTasks.DotNet(
            $"test --solution {Solution} --configuration {Configuration} --no-build --no-restore --max-parallel-test-modules 1",
            workingDirectory: RootDirectory));
}

/// <summary>
/// Build, test and pack nami. <c>./eng/build.sh --help</c> lists the targets. <c>Check</c> runs on
/// every push and pull request, <c>CheckFull</c> on <c>main</c> and before a release.
/// </summary>
sealed class Build : FalloutBuild,
    ICompile,
    INamiTests,
    INativeAot,
    IVerify,
    IApiSdk,
    IPack,
    IMcp,
    IContracts
{
    public Target Clean => d => d
        .Before<IRestore>(static x => x.Restore)
        .Executes(() =>
        {
            var buildScriptDirectory = (RootDirectory / "eng" / "build")
                .ToString()
                .Replace('\\', '/') + "/";
            var pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            RootDirectory.GlobDirectories("**/bin", "**/obj")
                .Where(directory =>
                {
                    var path = directory.ToString().Replace('\\', '/') + "/";
                    return !path.Contains("/node_modules/", StringComparison.Ordinal)
                           // The running build, its lazy dependencies, and its assets graph live here.
                           && !path.StartsWith(buildScriptDirectory, pathComparison);
                })
                .DeleteDirectories();
            From<IHasArtifacts>().ArtifactsDirectory.CreateOrCleanDirectory();
        });

    // Compile builds with the AOT analyzers off; PackSmoke, ApiSdk and NativeAot see IL2026/IL3050.
    public Target OrchestrationSmoke => d => d
        .Description("Exercise the reusable orchestrator with real .NET services, failure, cancellation and timeout")
        .DependsOn<ICompile>(static x => x.Compile)
        .Executes(() => DotNetTasks.DotNetRun(s => s
            .SetProjectFile(From<IHasSourcePaths>().OrchestrationSmokeProject)
            .SetConfiguration(From<IHasConfiguration>().Configuration)
            .EnableNoBuild()));

    public Target Check => d => d
        .Description("Generated contracts current, compile, .NET tests, the orchestration smoke, the contract and storage checks, and the MCP build and tests")
        .DependsOn<IContracts>(static x => x.VerifyContractsCurrent)
        .DependsOn<ICompile>(static x => x.Compile)
        .DependsOn<INamiTests>(static x => x.Test)
        .DependsOn(OrchestrationSmoke)
        .DependsOn<IVerify>(static x => x.Verify)
        .DependsOn<IMcp>(static x => x.McpTest);

    public Target CheckFull => d => d
        .Description("Check plus the collector NativeAOT smoke, the packed-tool smoke and the Nami.Api.Sdk proof")
        .DependsOn(Check)
        .DependsOn<INativeAot>(static x => x.NativeAot)
        .DependsOn<IPack>(static x => x.PackSmoke)
        .DependsOn<IApiSdk>(static x => x.ApiSdk);

    AbsolutePath IHasArtifacts.ArtifactsDirectory => RootDirectory / "artifacts";

    Configure<DotNetBuildSettings> ICompile.CompileSettings => s => s
        .SetDeterministic(IsServerBuild)
        .SetContinuousIntegrationBuild(IsServerBuild)
        .SetProperty("NamiAot", false);

    T From<T>() where T : IFalloutBuild => (T)(object)this;

    public static int Main() => Execute<Build>(static x => ((ICompile)x).Compile);
}
