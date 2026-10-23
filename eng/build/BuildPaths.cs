using Fallout.Common.IO;
using Fallout.Common.Tooling;
using Fallout.Components;

namespace Nami.Build;


interface IHasSourcePaths : IHasSolution, IHasArtifacts
{
    new AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath ServicesDirectory => RootDirectory / "services";
    AbsolutePath PackagesDirectory => RootDirectory / "packages";
    AbsolutePath InternalDirectory => RootDirectory / "internal";
    AbsolutePath CollectorDirectory => ServicesDirectory / "nami.collector";
    AbsolutePath CollectorIngestionDirectory => ServicesDirectory / "nami.collector.ingestion";
    AbsolutePath CollectorStorageDirectory => ServicesDirectory / "nami.collector.storage";

    /// <summary>The collector's three projects. A check about "the collector" scans all of them.</summary>
    AbsolutePath[] CollectorProjectDirectories => [CollectorDirectory, CollectorIngestionDirectory, CollectorStorageDirectory];
    AbsolutePath NamiToolSmokeProject => RootDirectory / "eng" / "tools" / "NamiToolSmoke" / "NamiToolSmoke.csproj";

    /// <summary>The nami tool: the outer <c>nami</c> package and, packed per runtime, its <c>nami.&lt;rid&gt;</c> packages.</summary>
    AbsolutePath CliPackProject => PackagesDirectory / "Nami.Cli" / "Nami.Cli.csproj";

    AbsolutePath OrchestrationProject => PackagesDirectory / "Nami.Orchestration" / "Nami.Orchestration.csproj";
    AbsolutePath OrchestrationSmokeProject => RootDirectory / "eng" / "tools" / "NamiOrchestrationSmoke" / "NamiOrchestrationSmoke.csproj";

    /// <summary>The generated C# contracts, shipped as Nami.Api.Contracts at the release version.</summary>
    AbsolutePath ContractsProject => InternalDirectory / "nami.contracts" / "Nami.Api.Contracts.csproj";

    /// <summary>Projects with IsPackable=true — the packages nami actually ships.</summary>
    AbsolutePath[] ShippablePackProjects =>
    [
        CliPackProject,
        OrchestrationProject,
        PackagesDirectory / "Nami.Api.Sdk" / "Nami.Api.Sdk.csproj",
        ContractsProject
    ];

    /// <summary>Installs a bun package's dependencies exactly as its lockfile pins them.</summary>
    static void BunInstall(AbsolutePath directory) =>
        ProcessTasks.StartProcess("bun", "install --frozen-lockfile", directory, logOutput: true).AssertZeroExitCode();

    string[] ShippablePackageIds =>
    [
        "nami",
        "nami.linux-x64",
        "nami.linux-arm64",
        "nami.osx-x64",
        "nami.osx-arm64",
        "nami.win-x64",
        "nami.win-arm64",
        "Nami.Api.Sdk",
        "Nami.Orchestration",
        "Nami.Api.Contracts"
    ];
}
