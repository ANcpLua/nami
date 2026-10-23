using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Fallout.Common;
using Fallout.Common.IO;
using Fallout.Common.Tooling;
using Fallout.Common.Tools.DotNet;
using Fallout.Components;
using Serilog;

namespace Nami.Build;

interface IPack : IHasSourcePaths, IHasConfiguration
{
    AbsolutePath NuGetArtifactsDirectory => ArtifactsDirectory / "nuget";

    [Parameter("Release version PackRelease stamps (release.sh passes Version.props' NamiVersion)")]
    string? PackageVersion => TryGetValue(() => PackageVersion);

    [Parameter("Commit PackRelease stamps as RepositoryCommit and SourceRevisionId")]
    string? RepositoryCommit => TryGetValue(() => RepositoryCommit);

    /// <summary>Where PackRelease seals a release: <c>artifacts/release/&lt;version&gt;/</c>.</summary>
    AbsolutePath ReleaseDirectory => ArtifactsDirectory / "release" / PackageVersion!;

    Target Pack => d => d
        .Description("Pack the shippable NuGet packages into artifacts/nuget")
        .DependsOn<ICompile>(static x => x.Compile)
        .Produces(NuGetArtifactsDirectory / "*.nupkg")
        .Executes(() =>
        {
            NuGetArtifactsDirectory.CreateOrCleanDirectory();

            DotNetTasks.DotNetPack(s => s
                .SetConfiguration(Configuration)
                .SetOutputDirectory(NuGetArtifactsDirectory)
                .CombineWith(ShippablePackProjects, static (settings, project) => settings
                    .SetProject(project)));

            foreach (var package in NuGetArtifactsDirectory.GlobFiles("*.nupkg"))
                Log.Information("Packed: {Package}", package.Name);
        });

    Target PackSmoke => d => d
        .Description("Install the packed nami tool into a clean directory and exercise its product vertical, then build a standalone Nami.Orchestration consumer from the packed package")
        .DependsOn(Pack)
        .Executes(() =>
        {
            DotNetTasks.DotNetRun(s => s
                .SetProjectFile(NamiToolSmokeProject)
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .SetApplicationArguments(NuGetArtifactsDirectory));

            var orchestration = NuGetArtifactsDirectory.GlobFiles("Nami.Orchestration.*.nupkg").Single();
            var version = orchestration.NameWithoutExtension["Nami.Orchestration.".Length..];
            ProcessTasks.StartProcess(
                    "bash",
                    $"\"{RootDirectory / "eng" / "scripts" / "smoke-orchestration-package.sh"}\" {version} \"{NuGetArtifactsDirectory}\"",
                    RootDirectory,
                    logOutput: true)
                .AssertZeroExitCode();
        });

    /// <summary>
    /// The sealed release set: every NuGet package (the nami tool, its runtime packages, Nami.Api.Sdk,
    /// Nami.Orchestration and Nami.Api.Contracts) and the nami-mcp-server npm tarball, all at one version, plus the commit they
    /// were packed from and a SHA-512 manifest. release.sh publishes from this directory and never repacks it.
    /// </summary>
    Target PackRelease => d => d
        .Description("Pack every release artifact at one version into artifacts/release/<version>/ with a SHA-512 manifest")
        .Requires(() => PackageVersion, () => RepositoryCommit)
        .Executes(() =>
        {
            var mcpDirectory = ServicesDirectory / "nami.mcp";
            using (var manifest = JsonDocument.Parse(File.ReadAllText(mcpDirectory / "package.json")))
            {
                var mcpVersion = manifest.RootElement.GetProperty("version").GetString();
                if (mcpVersion != PackageVersion)
                    throw new InvalidOperationException(
                        $"services/nami.mcp/package.json version is {mcpVersion}, the release is {PackageVersion}: set it to {PackageVersion} and commit.");
            }

            ReleaseDirectory.CreateOrCleanDirectory();
            DotNetTasks.DotNetPack(s => s
                .SetConfiguration(Configuration)
                .SetVersion(PackageVersion)
                .SetProperty("RepositoryCommit", RepositoryCommit!)
                .SetProperty("SourceRevisionId", RepositoryCommit!)
                .SetOutputDirectory(ReleaseDirectory)
                .CombineWith(ShippablePackProjects, static (settings, project) => settings.SetProject(project)));

            BunInstall(mcpDirectory);
            ProcessTasks.StartProcess("bun", $"pm pack --destination \"{ReleaseDirectory}\"", mcpDirectory).AssertZeroExitCode();

            var expected = ShippablePackageIds.Select(id => $"{id}.{PackageVersion}.nupkg")
                .Append($"nami-mcp-server-{PackageVersion}.tgz")
                .Order(StringComparer.Ordinal)
                .ToArray();
            var packed = ReleaseDirectory.GlobFiles("*.nupkg", "*.tgz")
                .Select(static file => file.Name)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (!packed.SequenceEqual(expected, StringComparer.Ordinal))
                throw new InvalidOperationException(
                    $"Release set mismatch. Expected: {string.Join(", ", expected)}. Packed: {string.Join(", ", packed)}.");

            File.WriteAllText(ReleaseDirectory / "commit", RepositoryCommit + "\n");
            // shasum -a 512 -c format, written last: a directory with SHA512SUMS is a complete, sealed set.
            var sums = packed.Select(name =>
                $"{Convert.ToHexStringLower(SHA512.HashData(File.ReadAllBytes(ReleaseDirectory / name)))}  {name}");
            File.WriteAllLines(ReleaseDirectory / "SHA512SUMS.tmp", sums);
            File.Move(ReleaseDirectory / "SHA512SUMS.tmp", ReleaseDirectory / "SHA512SUMS");
            Log.Information("Sealed {Count} release artifacts of {Version} in {Directory}", packed.Length, PackageVersion, ReleaseDirectory);
        });
}
