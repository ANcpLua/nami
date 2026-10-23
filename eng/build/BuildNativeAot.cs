using System;
using Fallout.Common;
using Fallout.Common.Tooling;

namespace Nami.Build;

interface INativeAot : IHasSourcePaths
{
    Target NativeAot => d => d
        .Description("Publish and execute the collector NativeAOT smoke (needs a container daemon)")
        .OnlyWhenDynamic(() => ContainerDaemon.Required(IsServerBuild), ContainerDaemon.Condition)
        .Executes(() =>
        {
            var collectorSmoke = RootDirectory / "eng" / "scripts" / "collector-aot-smoke.sh";
            ProcessTasks.StartProcess(
                    "bash",
                    $"\"{collectorSmoke}\"",
                    RootDirectory,
                    logOutput: true)
                .AssertZeroExitCode();
        });
}

/// <summary>
/// The collector NativeAOT smoke and the Nami.Api.Sdk container scenario need a container daemon. On CI
/// they always run, so a missing daemon fails them; a machine without one skips them and the target
/// table names the reason.
/// </summary>
static class ContainerDaemon
{
    public const string Condition = "a container daemon runs (CI always runs this stage)";

    public static bool Required(bool isServerBuild) => isServerBuild || Running();

    static bool Running()
    {
        try
        {
            var process = ProcessTasks.StartProcess("docker", "version", logOutput: false, logInvocation: false);
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }
}
