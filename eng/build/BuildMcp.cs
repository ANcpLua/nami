using System;
using System.Collections.Generic;
using System.Linq;
using Fallout.Common;
using Fallout.Common.IO;
using Fallout.Common.Tooling;
using Fallout.Components;

namespace Nami.Build;

/// <summary>
/// The MCP server in <c>services/nami.mcp</c>, built with bun from its lockfile. Its seeded answer key and
/// its project-isolation smoke run against the Collector this build compiled, so a Collector change and
/// the MCP change that answers it are checked together.
/// </summary>
interface IMcp : IHasSourcePaths, IHasConfiguration
{
    AbsolutePath McpDirectory => ServicesDirectory / "nami.mcp";

    /// <summary>The Collector Compile built, under the artifacts layout's lower-case configuration pivot.</summary>
    AbsolutePath? CollectorAssembly =>
        (ArtifactsDirectory / "bin" / "nami.collector").GlobFiles("*/nami.collector.dll")
        .SingleOrDefault(file => file.Parent.Name.Equals(Configuration.ToString(), StringComparison.OrdinalIgnoreCase));

    Target McpBuild => d => d
        .Description("Install the locked MCP dependencies and build services/nami.mcp")
        .Executes(() =>
        {
            BunInstall(McpDirectory);
            Run("bun", "run build", McpDirectory);
        });

    Target McpTest => d => d
        .Description("Run the MCP's SDK boundary, lint, unit tests and smokes, and its answer key and project isolation against the Collector built here")
        .DependsOn(McpBuild)
        .DependsOn<ICompile>(static x => x.Compile)
        .Executes(() =>
        {
            Run("bun", "run verify:sdk", McpDirectory);
            Run("bun", "run lint", McpDirectory);
            Run("bun", "run test:built", McpDirectory);
            Run("python3", "-m unittest discover -s eng/auth0 -p test_*.py", RootDirectory);
            Run("python3", "-m unittest discover -s services/nami.mcp/eval -p test_*.py", RootDirectory);
            Run("bun", "run smoke", McpDirectory);
            Run("bun", "run smoke:quiet", McpDirectory);

            var assembly = CollectorAssembly
                ?? throw new InvalidOperationException($"No {Configuration} Collector build under artifacts/bin/nami.collector.");
            var collector = new Dictionary<string, string> { ["NAMI_COLLECTOR_DLL"] = assembly };
            Run("bun", "run test:answer-key", McpDirectory, collector);
            Run("bun", "run smoke:projects", McpDirectory, collector);
        });

    private static void Run(string tool, string arguments, AbsolutePath workingDirectory, IReadOnlyDictionary<string, string>? extra = null)
    {
        // A non-null environment replaces the process environment, so the extra variables are added to a copy of it.
        var environment = extra is null
            ? null
            : Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
                .ToDictionary(static entry => (string)entry.Key, static entry => (string?)entry.Value ?? string.Empty, StringComparer.Ordinal)
                .Concat(extra)
                .GroupBy(static pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.Last().Value, StringComparer.Ordinal);
        ProcessTasks.StartProcess(tool, arguments, workingDirectory, environment, logOutput: true).AssertZeroExitCode();
    }
}
