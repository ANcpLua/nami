using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using Fallout.Common;
using Fallout.Common.IO;
using Fallout.Common.Tooling;
using Fallout.Common.Tools.DotNet;
using Fallout.Components;
using Serilog;
using HttpAttributes = Nami.Telemetry.SemanticConventions.Attributes.Http.HttpAttributes;
using NamiAttributes = Nami.Telemetry.SemanticConventions.Incubating.Attributes.Nami.NamiAttributes;
using SessionAttributes = Nami.Telemetry.SemanticConventions.Incubating.Attributes.Session.SessionAttributes;

namespace Nami.Build;

/// <summary>
/// The proof that Nami.Api.Sdk builds the API it claims to: the sample is the API, and every check
/// the SDK's own repository ran from a shell script runs here instead. Nine stages, in order —
/// build and generator tests, committed contract unchanged, every compile-time generator ran,
/// the HTTP scenario on the managed host, a Native AOT publish with no managed files beside the
/// binary, the same scenario on the native host, the container image, a consumer built from
/// the packed SDK producing a byte-identical contract, and the session: a real collector, the
/// native host exporting to it, and an agent's `baggage: session.id=…` header answered by the
/// collector's read API.
///
/// The scenario is written against the .NET HTTP and XML stacks rather than curl/jq/xmllint:
/// the gate must run wherever the build runs, and three shell tools that have to be installed
/// first are three ways for it to be skipped instead of failed.
/// </summary>
[ParameterPrefix("ApiSdk")]
interface IApiSdk : IHasSourcePaths, IHasConfiguration
{
    [Parameter("Runtime identifier for the Nami.Api.Sdk Native AOT stage (default: the host)")]
    string? NativeRuntime => TryGetValue(() => NativeRuntime);

    AbsolutePath ApiSdkDirectory => PackagesDirectory / "Nami.Api.Sdk";

    AbsolutePath ApiSdkProject => ApiSdkDirectory / "Nami.Api.Sdk.csproj";

    AbsolutePath SampleDirectory => RootDirectory / "samples" / "nami.sample";

    AbsolutePath SampleProject => SampleDirectory / "nami.sample.csproj";

    AbsolutePath SampleDockerfile => SampleDirectory / "Dockerfile";

    AbsolutePath SampleContract => SampleDirectory / "openapi" / "nami.sample.json";

    AbsolutePath ApiSdkArtifactsDirectory => ArtifactsDirectory / "api-sdk";

    AbsolutePath ApiSdkNativeDirectory => ApiSdkArtifactsDirectory / "native";

    AbsolutePath ApiSdkFeedDirectory => ApiSdkArtifactsDirectory / "feed";

    /// <summary>
    /// Outside the repository on purpose. The consumer must reach the SDK through the package
    /// alone; built anywhere under this tree it would inherit Directory.Build.props,
    /// Directory.Packages.props and nuget.config, which is exactly the help it is proving it
    /// does not need.
    /// </summary>
    AbsolutePath ApiSdkConsumerDirectory => Path.Combine(Path.GetTempPath(), "nami-api-sdk-consumer");

    AbsolutePath SampleIntermediateDirectory => RootDirectory / "artifacts" / "obj" / "nami.sample";

    AbsolutePath ApiIntermediateDirectory => RootDirectory / "artifacts" / "obj" / "Nami.Api";

    Target ApiSdk => d => d
        .Description("Run every Nami.Api.Sdk gate: build, contract, generators, managed, native, container, package, session")
        .DependsOn(ApiSdkBuildAndTest)
        .DependsOn(ApiSdkContractIsCommitted)
        .DependsOn(ApiSdkGeneratorsRan)
        .DependsOn(ApiSdkManagedScenario)
        .DependsOn(ApiSdkNativePublish)
        .DependsOn(ApiSdkNativeScenario)
        .DependsOn(ApiSdkContainerScenario)
        .DependsOn(ApiSdkPackagedConsumer)
        .DependsOn(ApiSdkSessionScenario)
        .Executes(() => Log.Information("Nami.Api.Sdk: nine stages green"));

    /// <summary>Stage 1: the SDK, the sample and the generator's own proof build and pass.</summary>
    Target ApiSdkBuildAndTest => d => d
        .Unlisted()
        // The sample and SDK tests belong to the solution. Share its build and test
        // targets with Check so each runs once, including when ApiSdk is invoked alone.
        .DependsOn<INamiTests>(static x => x.Test)
        .Executes(() =>
        {
            Directory.CreateDirectory(ApiSdkArtifactsDirectory);

            Log.Information("Nami.Api.Sdk: sample builds, generator and runtime tests pass");
        });

    /// <summary>
    /// Stage 2: the committed OpenAPI document is what the build produces. Compared against a copy
    /// taken before the build rather than against git, so it also holds for an uncommitted file.
    /// </summary>
    Target ApiSdkContractIsCommitted => d => d
        .Unlisted()
        .DependsOn(ApiSdkBuildAndTest)
        .Executes(() =>
        {
            if (!SampleContract.FileExists())
                throw new FileNotFoundException("The committed OpenAPI contract is missing", SampleContract);

            var before = SampleContract.ReadAllText();

            DotNetTasks.DotNetBuild(s => s
                .SetProjectFile(SampleProject)
                .SetConfiguration(Configuration));

            var after = SampleContract.ReadAllText();
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{RootDirectory.GetRelativePathTo(SampleContract)} changed during the build. " +
                    "The document is a committed build artifact: commit the result.");
            }

            Log.Information("Nami.Api.Sdk: committed contract equals the built contract");
        });

    /// <summary>
    /// Stage 3: every compile-time generator the SDK turns on actually ran. Scoped per project,
    /// because Nami.Api runs the OpenAPI generators too and has nothing of its own to record.
    /// </summary>
    Target ApiSdkGeneratorsRan => d => d
        .Unlisted()
        .DependsOn(ApiSdkContractIsCommitted)
        .Executes(() =>
        {
            (AbsolutePath Root, string Description, string FileName, string Marker)[] expectations =
            [
                (SampleIntermediateDirectory, "validation resolver", "ValidatableInfoResolver.g.cs", "CreateTodoRequest"),
                (SampleIntermediateDirectory, "request delegates", "GeneratedRouteBuilderExtensions.g.cs", "MapPost"),
                (SampleIntermediateDirectory, "OpenAPI comment cache", "OpenApiXmlCommentSupport.generated.cs", "Creates a todo."),
                (SampleIntermediateDirectory, "XML writer", "Nami_Sample_Todo.GenerateXml.g.cs", "WriteXml(writer, \"todo\", null)"),
                (SampleIntermediateDirectory, "JSON context", "AppJsonSerializerContext.CreateTodoRequest.g.cs", "CreateTodoRequest"),
                (SampleIntermediateDirectory, "public Program", "PublicTopLevelProgram.Generated.g.cs", "public partial class Program"),
                (ApiIntermediateDirectory, "problem JSON context", "NamiProblemJsonContext.HttpValidationProblemDetails.g.cs", "HttpValidationProblemDetails"),
            ];

            var offenders = new List<string>();
            foreach (var (root, description, fileName, marker) in expectations)
            {
                var file = root.GlobFiles($"**/{fileName}").FirstOrDefault();
                if (file is null)
                {
                    offenders.Add($"generated {description} is missing ({fileName} under {RootDirectory.GetRelativePathTo(root)})");
                    continue;
                }

                if (!file.ReadAllText().Contains(marker, StringComparison.Ordinal))
                    offenders.Add($"generated {description} does not contain '{marker}': {file}");
            }

            if (offenders.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, offenders));

            Log.Information("Nami.Api.Sdk: all {Count} compile-time generators produced their output", expectations.Length);
        });

    /// <summary>Stage 4: the HTTP scenario against the managed host.</summary>
    Target ApiSdkManagedScenario => d => d
        .Unlisted()
        .DependsOn(ApiSdkGeneratorsRan)
        .Executes(() =>
        {
            // The artifacts layout writes bin/<project>/<configuration>/, lowercased, with no
            // framework segment; the configuration is matched case-insensitively for that reason.
            var outputRoot = RootDirectory / "artifacts" / "bin" / "nami.sample";
            var host = outputRoot
                .GlobFiles("**/nami.sample", "**/nami.sample.exe")
                .FirstOrDefault(file =>
                    !file.ToString().Contains("/publish/", StringComparison.Ordinal) &&
                    file.Parent.Name.Equals(Configuration.ToString(), StringComparison.OrdinalIgnoreCase));

            if (host is null)
                throw new FileNotFoundException($"The managed sample host was not found under {outputRoot}");

            ApiSdkScenario.Run(host, "managed", SampleContract);
        });

    /// <summary>
    /// Stage 5: a Native AOT publish that really is native — the executable is a Mach-O or ELF
    /// image and no managed deployment file sits beside it.
    /// </summary>
    Target ApiSdkNativePublish => d => d
        .Unlisted()
        .DependsOn(ApiSdkManagedScenario)
        .Executes(() =>
        {
            var runtime = NativeRuntime ?? RuntimeInformation.RuntimeIdentifier;
            Directory.CreateDirectory(ApiSdkNativeDirectory);

            DotNetTasks.DotNetPublish(s => s
                .SetProject(SampleProject)
                .SetConfiguration(Configuration)
                .SetRuntime(runtime)
                .EnableSelfContained()
                .SetOutput(ApiSdkNativeDirectory));

            var executable = ApiSdkNativeDirectory / "nami.sample";
            if (!executable.FileExists())
                executable = ApiSdkNativeDirectory / "nami.sample.exe";
            if (!executable.FileExists())
                throw new FileNotFoundException("The Native AOT publish produced no nami.sample executable", executable);

            ApiSdkScenario.AssertNativeImage(executable);

            var managed = ApiSdkNativeDirectory
                .GlobFiles("*.dll", "*.deps.json", "*.runtimeconfig.json")
                .Select(static file => file.Name)
                .ToList();
            if (managed.Count > 0)
            {
                throw new InvalidOperationException(
                    "Managed deployment files sit beside the native executable: " + string.Join(", ", managed));
            }

            Log.Information("Nami.Api.Sdk: {Runtime} native publish carries only the executable", runtime);
        });

    /// <summary>Stage 6: the same scenario against the native host.</summary>
    Target ApiSdkNativeScenario => d => d
        .Unlisted()
        .DependsOn(ApiSdkNativePublish)
        .Executes(() =>
        {
            var executable = ApiSdkNativeDirectory / "nami.sample";
            if (!executable.FileExists())
                executable = ApiSdkNativeDirectory / "nami.sample.exe";

            ApiSdkScenario.Run(executable, "native", SampleContract);
        });

    /// <summary>
    /// Stage 7: the container image, built Native AOT inside the SDK image and served from a
    /// runtime-deps image that carries no .NET runtime at all. Skipped without a container daemon
    /// outside CI (<see cref="ContainerDaemon"/>).
    /// </summary>
    Target ApiSdkContainerScenario => d => d
        .Unlisted()
        .DependsOn(ApiSdkNativeScenario)
        .OnlyWhenDynamic(() => ContainerDaemon.Required(IsServerBuild), ContainerDaemon.Condition)
        .Executes(() =>
        {
            var tag = $"nami.sample:verify-{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}";
            var container = string.Empty;
            try
            {
                ApiSdkScenario.Docker($"build --quiet --tag {tag} --file \"{SampleDockerfile}\" \"{RootDirectory}\"", RootDirectory);
                var port = ApiSdkScenario.FreeLoopbackPort();
                container = ApiSdkScenario
                    .Docker(
                        $"run --detach --publish 127.0.0.1:{port}:8080 --env ASPNETCORE_ENVIRONMENT=Development {tag}",
                        RootDirectory)
                    .Trim();

                ApiSdkScenario.RunAgainst(new Uri($"http://127.0.0.1:{port}"), "container", SampleContract, () => { });
            }
            finally
            {
                if (container.Length > 0)
                    ApiSdkScenario.DockerQuiet($"rm --force {container}");
                ApiSdkScenario.DockerQuiet($"rmi --force {tag}");
            }
        });

    /// <summary>
    /// Stage 8: a consumer that reaches the SDK only through the package — no imports, no generator
    /// reference, no central package management — builds the identical contract from the same sources.
    /// </summary>
    Target ApiSdkPackagedConsumer => d => d
        .Unlisted()
        .DependsOn(ApiSdkContainerScenario)
        .Executes(() =>
        {
            var version = SampleVersion();
            ApiSdkFeedDirectory.CreateOrCleanDirectory();
            ApiSdkConsumerDirectory.CreateOrCleanDirectory();

            DotNetTasks.DotNetPack(s => s
                .SetProject(ApiSdkProject)
                .SetConfiguration(Configuration)
                .SetOutputDirectory(ApiSdkFeedDirectory));

            var package = ApiSdkFeedDirectory / $"Nami.Api.Sdk.{version}.nupkg";
            if (!package.FileExists())
                throw new FileNotFoundException($"Nami.Api.Sdk.{version}.nupkg was not produced", package);

            var consumer = ApiSdkConsumerDirectory / "nami.sample";
            consumer.CreateDirectory();
            foreach (var name in ApiSdkScenario.s_consumerSourceFiles)
                (SampleDirectory / name).Copy(consumer / name);

            (consumer / "nami.sample.csproj").WriteAllText(
                $"""
                 <Project Sdk="Nami.Api.Sdk/{version}">
                     <PropertyGroup>
                         <TargetFramework>net10.0</TargetFramework>
                         <ImplicitUsings>enable</ImplicitUsings>
                         <RootNamespace>Nami.Sample</RootNamespace>
                     </PropertyGroup>
                 </Project>

                 """);

            (consumer / "nuget.config").WriteAllText(
                $"""
                 <?xml version="1.0" encoding="utf-8"?>
                 <configuration>
                   <packageSources>
                     <clear />
                     <add key="nami-local" value="{ApiSdkFeedDirectory}" />
                     <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                   </packageSources>
                   <packageSourceMapping>
                     <clear />
                     <packageSource key="nami-local">
                       <package pattern="Nami.Api.Sdk" />
                     </packageSource>
                     <packageSource key="nuget.org">
                       <package pattern="*" />
                     </packageSource>
                   </packageSourceMapping>
                 </configuration>

                 """);

            // This stage's package is not the published one: same id, same version, different content. Rather
            // than delete it out of the machine's global packages folder afterwards — a folder a sibling
            // checkout may be restoring from at the same moment — the consumer gets a packages folder of its
            // own for the run. Nothing outside this directory is written, so there is nothing to clean up
            // anywhere else and nothing for a concurrent restore to lose.
            var packages = ApiSdkConsumerDirectory / "packages";
            packages.CreateOrCleanDirectory();

            try
            {
                // -warnaserror: this is the only build in the repository that compiles the SDK's linked and
                // generated sources without any of this repository's settings, so it is where a warning the SDK
                // emits into someone else's compilation shows up. A consumer with TreatWarningsAsErrors has to
                // be able to build against the package.
                DotNetTasks.DotNetBuild(s => s
                    .SetProjectFile(consumer / "nami.sample.csproj")
                    .SetConfiguration(Configuration)
                    .SetProcessEnvironmentVariable("NUGET_PACKAGES", packages)
                    .SetProcessAdditionalArguments("--disable-build-servers", "-warnaserror"));

                var generated = consumer.GlobFiles("**/Nami_Sample_Todo.GenerateXml.g.cs").FirstOrDefault();
                if (generated is null)
                    throw new InvalidOperationException("The packaged generator did not run in the consumer");

                foreach (var assembly in ApiSdkScenario.s_packagedAssemblies)
                {
                    if (consumer.GlobFiles($"bin/**/{assembly}").Count == 0)
                        throw new InvalidOperationException($"The packaged {assembly} was not referenced by the consumer");
                }

                ApiSdkScenario.AssertSameContract(
                    SampleContract.ReadAllText(),
                    (consumer / "openapi" / "nami.sample.json").ReadAllText(),
                    "the consumer built from the Nami.Api.Sdk package");

                // The revision is the digest of the document the consumer carries, and it copied the sample's.
                // Two projects, two builds, one contract: the same value or the SDK is hashing something local.
                var consumerRevision = ReadGeneratedContractRevision(consumer);
                ApiSdkHost.Expect(
                    string.Equals(consumerRevision, ApiSdkSession.ContractRevisionValue(SampleContract), StringComparison.Ordinal),
                    $"the packaged consumer compiled the sample's contract revision (saw '{consumerRevision}')");

                AssertStaleContractFailsTheBuild(consumer, packages);
            }
            finally
            {
                packages.DeleteDirectory();
            }

            Log.Information(
                "Nami.Api.Sdk: <Project Sdk=\"Nami.Api.Sdk/{Version}\"> builds the identical contract and revision, and a stale one fails the build",
                version);
        });

    /// <summary>
    /// Stage 9: the API observes itself and an agent is its first consumer. A real collector, the native
    /// sample exporting to it, three requests carrying one <c>baggage: session.id=…</c> header, and the
    /// collector's own read API answering with exactly those requests — their routes, the validation 400,
    /// the XML response, and the contract revision the binary was compiled against.
    /// </summary>
    /// <remarks>
    /// The sample carries no telemetry line of its own; everything asserted here comes from
    /// <c>AddNamiApi</c>. The collector is the one in this repository, started on free loopback ports so a
    /// developer's own <c>nami up</c> stack on 4318 is neither needed nor disturbed.
    /// </remarks>
    Target ApiSdkSessionScenario => d => d
        .Unlisted()
        .DependsOn(ApiSdkPackagedConsumer)
        .Executes(() =>
        {
            var collector = (RootDirectory / "artifacts" / "bin" / "nami.collector")
                .GlobFiles("**/nami.collector", "**/nami.collector.exe")
                .FirstOrDefault(file =>
                    !file.ToString().Contains("/publish/", StringComparison.Ordinal) &&
                    file.Parent.Name.Equals(Configuration.ToString(), StringComparison.OrdinalIgnoreCase));
            if (collector is null)
                throw new FileNotFoundException("The collector host was not found under artifacts/bin/nami.collector");

            var native = ApiSdkNativeDirectory / "nami.sample";
            if (!native.FileExists())
                native = ApiSdkNativeDirectory / "nami.sample.exe";
            if (!native.FileExists())
                throw new FileNotFoundException("The native sample host is missing; run ApiSdkNativePublish first", native);

            var data = ApiSdkArtifactsDirectory / "session";
            data.CreateOrCleanDirectory();

            ApiSdkSession.Run(collector, native, data, SampleContract);
        });


    /// <summary>The revision the consumer's own build compiled in, read from the source the SDK generated for it.</summary>
    private static string ReadGeneratedContractRevision(AbsolutePath consumer)
    {
        var generated = consumer.GlobFiles("**/NamiApiSdkBuild.g.cs").FirstOrDefault()
                        ?? throw new FileNotFoundException($"The SDK generated no NamiApiSdkBuild.g.cs under {consumer}");
        var match = Regex.Match(
            generated.ReadAllText(),
            @"ContractRevision\s*=\s*""(?<revision>[^""]*)""",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));

        return match.Success ? match.Groups["revision"].Value : string.Empty;
    }

    /// <summary>
    /// The SDK enforces contract-is-committed for every consumer, not only for the sample: change an endpoint
    /// and the build fails with NAMISDK0001 rather than shipping a binary that names the previous contract.
    /// </summary>
    /// <remarks>
    /// On a copy of the consumer rather than on the consumer itself. Mutating and restoring in place leaves the
    /// project's incremental state describing a build that no longer exists, and the assertion then measures
    /// MSBuild's memory instead of the SDK's rule.
    /// </remarks>
    private void AssertStaleContractFailsTheBuild(AbsolutePath consumer, AbsolutePath packages)
    {
        var stale = consumer.Parent / "nami.sample.stale";
        stale.CreateOrCleanDirectory();
        var sources = consumer.GlobFiles("**/*")
            .Where(static file => !file.ToString().Contains("/obj/", StringComparison.Ordinal)
                                  && !file.ToString().Contains("/bin/", StringComparison.Ordinal));
        foreach (var file in sources)
        {
            var destination = stale / consumer.GetRelativePathTo(file);
            destination.Parent.CreateDirectory();
            file.Copy(destination, ExistsPolicy.FileOverwrite);
        }

        var endpoints = stale / "TodoEndpoints.cs";
        endpoints.WriteAllText(endpoints.ReadAllText().Replace(
            "<summary>Lists every todo.</summary>",
            "<summary>Lists every todo, and this summary is not in the committed contract.</summary>",
            StringComparison.Ordinal));

        // System.Diagnostics rather than the build's process helpers: a non-zero exit is the assertion here,
        // and every one of them treats it as a failure of the gate.
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = stale,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(stale / "nami.sample.csproj");
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add(Configuration);
        startInfo.ArgumentList.Add("--disable-build-servers");
        startInfo.Environment["NUGET_PACKAGES"] = packages;

        string output;
        int exitCode;
        using (var build = Process.Start(startInfo)
                           ?? throw new InvalidOperationException("Could not start the stale consumer's build"))
        {
            output = build.StandardOutput.ReadToEnd() + build.StandardError.ReadToEnd();
            build.WaitForExit();
            exitCode = build.ExitCode;
        }

        ApiSdkHost.Expect(exitCode != 0, "a changed contract fails the consumer's build");
        ApiSdkHost.Expect(output.Contains("NAMISDK0001", StringComparison.Ordinal),
            $"the failure names NAMISDK0001{Environment.NewLine}{output}");

        stale.DeleteDirectory();
        Log.Information("Nami.Api.Sdk: a consumer whose contract changed fails its own build with NAMISDK0001");
    }

    private string SampleVersion()
    {
        var versionProps = XDocument.Load(RootDirectory / "Version.props");
        return versionProps.Descendants("NamiVersion").Single().Value.Trim();
    }
}

/// <summary>The HTTP proof, identical for the managed host, the native host, and the container.</summary>
/// <summary>
/// What both halves of the proof need from a child process: a scrubbed environment, its output captured without
/// racing the reader, and a stop that does not depend on the process being cooperative.
/// </summary>
internal sealed class ApiSdkHost : IDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _log = new();

    private ApiSdkHost(Process process) => _process = process;

    internal bool HasExited => _process.HasExited;

    /// <summary>The output so far, read under the same lock the reader writes with.</summary>
    internal string Snapshot()
    {
        lock (_log) return _log.ToString();
    }

    /// <summary>
    /// Starts <paramref name="executable"/> with an environment scrubbed of every OTEL_ and NAMI_ variable, then
    /// exactly <paramref name="environment"/>. The gate's own shell decides nothing about the run: an inherited
    /// OTEL_RESOURCE_ATTRIBUTES, OTEL_TRACES_SAMPLER or OTEL_BSP_* would satisfy or skew the assertions.
    /// </summary>
    internal static ApiSdkHost Start(
        AbsolutePath executable,
        IReadOnlyDictionary<string, string>? environment = null,
        string? urls = null)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = executable.Parent,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var name in startInfo.Environment.Keys
                     .Where(static key =>
                         key.StartsWith("OTEL_", StringComparison.OrdinalIgnoreCase) ||
                         key.StartsWith("NAMI_", StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            startInfo.Environment.Remove(name);
        }

        foreach (var (key, value) in environment ?? new Dictionary<string, string>(StringComparer.Ordinal))
            startInfo.Environment[key] = value;

        if (urls is not null)
        {
            startInfo.ArgumentList.Add("--urls");
            startInfo.ArgumentList.Add(urls);
        }

        var process = Process.Start(startInfo)
                      ?? throw new InvalidOperationException($"Could not start {executable}");
        var host = new ApiSdkHost(process);
        process.OutputDataReceived += (_, e) => host.Append(e.Data);
        process.ErrorDataReceived += (_, e) => host.Append(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return host;
    }

    private void Stop()
    {
        if (_process.HasExited)
            return;

        _process.Kill(entireProcessTree: true);
        _process.WaitForExit();
    }

    public void Dispose()
    {
        Stop();
        _process.Dispose();
    }

    private void Append(string? line)
    {
        if (line is null) return;
        lock (_log) _log.AppendLine(line);
    }

    /// <summary>Every assertion in the proof, so a failure reads the same wherever it came from.</summary>
    internal static void Expect(bool condition, string what)
    {
        if (!condition)
            throw new InvalidOperationException($"{what} — it did not");
    }
}

internal static class ApiSdkScenario
{
    private static readonly TimeSpan s_startupTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The sample sources a packaged consumer is rebuilt from, verbatim — the committed contract included.
    /// Without it the consumer would be a project building its API for the first time, which is the one case
    /// where there is no revision to compile in.
    /// </summary>
    internal static readonly string[] s_consumerSourceFiles =
        ["Program.cs", "AppJsonSerializerContext.cs", "appsettings.json", "openapi/nami.sample.json",
            "CreateTodoRequest.cs", "Todo.cs", "TodoEndpoints.cs", "TodoStore.cs"];

    /// <summary>The assemblies the package must put on a consumer's reference list.</summary>
    internal static readonly string[] s_packagedAssemblies = ["Nami.Xml.dll", "Nami.Api.dll"];

    public static void Run(AbsolutePath executable, string label, AbsolutePath contract)
    {
        var port = FreeLoopbackPort();
        using var host = ApiSdkHost.Start(
            executable,
            // The template maps /openapi/v1.json in Development only, and the scenario compares that document.
            new Dictionary<string, string>(StringComparer.Ordinal) { ["ASPNETCORE_ENVIRONMENT"] = "Development" },
            $"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}");

        try
        {
            RunAgainst(new Uri($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}"), label, contract, () =>
            {
                if (host.HasExited)
                    throw new InvalidOperationException($"The {label} host exited before it served:{Environment.NewLine}{host.Snapshot()}");
            });
        }
        catch
        {
            Log.Error("{Label} host output:{NewLine}{Output}", label, Environment.NewLine, host.Snapshot());
            throw;
        }
    }

    public static void RunAgainst(Uri baseAddress, string label, AbsolutePath contract, Action assertAlive)
    {
        using var client = new HttpClient();
        client.BaseAddress = baseAddress;
        client.Timeout = TimeSpan.FromSeconds(30);
        WaitUntilServing(client, label, assertAlive);

        AssertValidationProblem(client, label);
        var id = AssertCreate(client, label);
        AssertJsonReadBack(client, label, id);
        AssertXmlDocument(client, label, id);
        AssertUnknownIsNotFound(client, label);
        AssertServedContract(client, label, contract);
    }

    public static int FreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static string Docker(string arguments, AbsolutePath workingDirectory)
    {
        var process = ProcessTasks.StartProcess("docker", arguments, workingDirectory, logOutput: false);
        process.AssertZeroExitCode();
        return string.Join(Environment.NewLine, process.Output.Select(static line => line.Text));
    }

    public static void DockerQuiet(string arguments)
    {
        try
        {
            ProcessTasks.StartProcess("docker", arguments, logOutput: false, logInvocation: false).WaitForExit();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.Debug(exception, "docker {Arguments} failed while cleaning up", arguments);
        }
    }

    /// <summary>A native image, not an apphost with a managed payload: the file starts with a Mach-O or ELF magic.</summary>
    public static void AssertNativeImage(AbsolutePath executable)
    {
        Span<byte> magic = stackalloc byte[4];
        using (var stream = File.OpenRead(executable))
        {
            if (stream.Read(magic) != magic.Length)
                throw new InvalidOperationException($"{executable} is too short to be an executable image");
        }

        var value = BitConverter.ToUInt32(magic);
        var isMachO = value is 0xFEEDFACF or 0xCFFAEDFE or 0xFEEDFACE or 0xCEFAEDFE;
        var isElf = magic[0] is 0x7F && magic[1] is (byte)'E' && magic[2] is (byte)'L' && magic[3] is (byte)'F';
        var isPortableExecutable = magic[0] is (byte)'M' && magic[1] is (byte)'Z';

        if (!isMachO && !isElf && !isPortableExecutable)
            throw new InvalidOperationException($"{executable} is not a native executable image");
    }

    public static void AssertSameContract(string committed, string produced, string what)
    {
        var expected = Canonicalize(committed);
        var actual = Canonicalize(produced);
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The OpenAPI document from {what} differs from the committed contract." +
                $"{Environment.NewLine}committed:{Environment.NewLine}{expected}" +
                $"{Environment.NewLine}produced:{Environment.NewLine}{actual}");
        }
    }

    private static void WaitUntilServing(HttpClient client, string label, Action assertAlive)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < s_startupTimeout)
        {
            assertAlive();
            try
            {
                using var response = client.GetAsync("/todos").GetAwaiter().GetResult();
                return;
            }
            catch (HttpRequestException)
            {
                Thread.Sleep(100);
            }
        }

        throw new InvalidOperationException($"The {label} host did not start serving within {s_startupTimeout}");
    }

    private static void AssertValidationProblem(HttpClient client, string label)
    {
        using var response = Post(client, "/todos", """{"title":"no"}""");
        Expect(label, response.StatusCode is HttpStatusCode.BadRequest, "an invalid POST returns 400");
        Expect(label, response.Content.Headers.ContentType?.MediaType is "application/problem+json",
            "the validation problem is application/problem+json");

        var problem = ReadJson(response);
        var titleErrors = problem?["errors"]?["Title"]?.AsArray();
        Expect(label, titleErrors?.Count is 1, "the problem details carry exactly one errors.Title entry");
        Log.Information("  {Label}: generated DataAnnotations validation -> 400 application/problem+json", label);
    }

    private static string AssertCreate(HttpClient client, string label)
    {
        using var response = Post(client, "/todos", """{"title":"Read obj/generated","dueBy":"2026-09-05"}""");
        Expect(label, response.StatusCode is HttpStatusCode.Created, "a valid POST returns 201");

        var id = ReadJson(response)?["id"]?.GetValue<int>().ToString(CultureInfo.InvariantCulture);
        Expect(label, id is not null, "the created todo carries an id");
        Expect(label, response.Headers.Location?.ToString().EndsWith($"/todos/{id}", StringComparison.Ordinal) is true,
            $"the 201 carries a Location pointing at /todos/{id}");
        Log.Information("  {Label}: validated create -> 201 + Location /todos/{Id}", label, id);
        return id!;
    }

    private static void AssertJsonReadBack(HttpClient client, string label, string id)
    {
        using var response = client.GetAsync($"/todos/{id}").GetAwaiter().GetResult();
        Expect(label, response.StatusCode is HttpStatusCode.OK, "the created todo reads back");
        Expect(label, ReadJson(response)?["title"]?.GetValue<string>() is "Read obj/generated",
            "the JSON read-back returns the stored title");
        Log.Information("  {Label}: JSON round-trip (source-generated context) -> 200 Todo", label);
    }

    private static void AssertXmlDocument(HttpClient client, string label, string id)
    {
        using var response = client.GetAsync($"/todos/{id}/xml").GetAwaiter().GetResult();
        Expect(label, response.Content.Headers.ContentType?.MediaType is "application/xml",
            "the XML endpoint answers application/xml");

        var document = XDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        Expect(label, document.Root?.Attribute("id")?.Value == id, "the XML document carries the id as an attribute");
        Expect(label, document.Root?.Element("due-by")?.Value is "2026-09-05", "the XML document carries due-by as a date");
        Log.Information("  {Label}: generated XmlWriter document -> 200 application/xml", label);
    }

    private static void AssertUnknownIsNotFound(HttpClient client, string label)
    {
        using var response = client.GetAsync("/todos/999").GetAwaiter().GetResult();
        Expect(label, response.StatusCode is HttpStatusCode.NotFound, "an unknown todo returns 404");
        Log.Information("  {Label}: unknown todo -> 404", label);
    }

    private static void AssertServedContract(HttpClient client, string label, AbsolutePath contract)
    {
        using var response = client.GetAsync("/openapi/v1.json").GetAwaiter().GetResult();
        Expect(label, response.StatusCode is HttpStatusCode.OK, "the OpenAPI document is served");
        var served = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        AssertSameContract(contract.ReadAllText(), served, $"the {label} host");

        var document = JsonNode.Parse(served)!;
        var post = document["paths"]!["/todos"]!["post"]!;
        Expect(label, post["summary"]?.GetValue<string>() is "Creates a todo.", "the XML-comment summary reaches the document");
        Expect(label, post["responses"]!["400"]!["content"]!.AsObject().First().Key is "application/problem+json",
            "the 400 response is described as application/problem+json");

        var xmlResponse = document["paths"]!["/todos/{id}/xml"]!["get"]!["responses"]!["200"]!["content"]!;
        Expect(label, xmlResponse.AsObject().First().Key is "application/xml",
            "the XML response is described as application/xml");
        Expect(label, xmlResponse["application/xml"]!["schema"]!["$ref"]?.GetValue<string>() is "#/components/schemas/TodoXml",
            "the application/xml response is described by the generated TodoXml schema");

        var todoXml = document["components"]!["schemas"]!["TodoXml"]!;
        Expect(label, todoXml["xml"]!["name"]?.GetValue<string>() is "todo", "TodoXml carries the document element name");
        Expect(label, todoXml["properties"]!["id"]!["xml"]!["attribute"]?.GetValue<bool>() is true,
            "TodoXml marks id as an attribute");
        Expect(label, todoXml["properties"]!["due-by"]!["format"]?.GetValue<string>() is "date",
            "TodoXml describes due-by as a date");
        Log.Information("  {Label}: OpenAPI equals the committed contract", label);
    }

    private static HttpResponseMessage Post(HttpClient client, string path, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return client.PostAsync(path, content).GetAwaiter().GetResult();
    }

    private static JsonNode? ReadJson(HttpResponseMessage response) =>
        JsonNode.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());

    private static void Expect(string label, bool condition, string what) =>
        ApiSdkHost.Expect(condition, $"{label}: {what}");

    /// <summary>
    /// Both documents with their <c>servers</c> array removed and their members ordered, so the
    /// comparison sees the contract and not the host's address or the writer's member order.
    /// </summary>
    private static string Canonicalize(string json)
    {
        var node = JsonNode.Parse(json) ?? throw new InvalidOperationException("The OpenAPI document is not JSON");
        node.AsObject().Remove("servers");
        return Order(node).ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonNode Order(JsonNode node)
    {
        switch (node)
        {
            case JsonObject o:
            {
                var ordered = new JsonObject();
                foreach (var (key, value) in o.ToList().OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    o.Remove(key);
                    ordered[key] = value is null ? null : Order(value);
                }

                return ordered;
            }
            case JsonArray a:
            {
                var ordered = new JsonArray();
                foreach (var value in a.ToList())
                {
                    a.Remove(value);
                    ordered.Add(value is null ? null : Order(value));
                }

                return ordered;
            }
            default:
                return node.DeepClone();
        }
    }
}

/// <summary>
/// The session proof: an agent's header in, the collector's read API out. Written against the .NET HTTP
/// stack for the same reason the rest of the scenario is — a gate that needs a shell tool installed first
/// is a gate that gets skipped instead of failed.
/// </summary>
internal static class ApiSdkSession
{
    /// <summary>The session an agent claims. Fixed per run so a leftover database cannot answer for it.</summary>
    private static string NewSessionId() =>
        $"apisdk-{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}-{DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)}";

    private const string BaggageHeader = "baggage";

    // The vocabulary is the registry's, read from the same packages the collector's ingest policy is generated
    // from. A rename upstream breaks this gate at compile time instead of leaving it asserting a key nobody
    // writes any more. The one deliberate literal is the `sha256:` value format below: the gate is the second
    // opinion about it, and a constant shared with the producer would make it one opinion twice.
    private const string ContractRevisionAttribute = NamiAttributes.ApiContractRevision;
    private const string SessionIdName = SessionAttributes.Id;
    private const string RouteAttribute = HttpAttributes.Route;
    private const string MethodAttribute = HttpAttributes.RequestMethod;
    private const string StatusAttribute = HttpAttributes.ResponseStatusCode;
    private const string DomainAttribute = NamiAttributes.InstrumentationDomain;

    /// <summary>The value nami stamps on the ASP.NET Core server span it enriches.</summary>
    private const string AspNetCoreServerDomain = NamiAttributes.InstrumentationDomainValues.AspNetCoreServer;

    /// <summary>OTLP span kind 2, SPAN_KIND_SERVER, as the read contract writes it.</summary>
    private const string ServerSpanKind = "2";

    private static readonly TimeSpan s_startupTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan s_sessionTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan s_settleDelay = TimeSpan.FromMilliseconds(400);

    public static void Run(AbsolutePath collector, AbsolutePath sample, AbsolutePath dataDirectory, AbsolutePath contract)
    {
        var apiPort = ApiSdkScenario.FreeLoopbackPort();
        var otlpPort = ApiSdkScenario.FreeLoopbackPort();
        var samplePort = ApiSdkScenario.FreeLoopbackPort();
        var sessionId = NewSessionId();
        var revision = ContractRevisionValue(contract);

        using var collectorHost = ApiSdkHost.Start(
            collector,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["NAMI_BIND_ADDRESS"] = "127.0.0.1",
                ["NAMI_PORT"] = apiPort.ToString(CultureInfo.InvariantCulture),
                ["NAMI_OTLP_PORT"] = otlpPort.ToString(CultureInfo.InvariantCulture),
                // 0 disables the listener: the sample exports over OTLP/HTTP, and a second fixed port is one
                // more way for a concurrent build on the same machine to collide.
                ["NAMI_GRPC_PORT"] = "0",
                ["NAMI_DATA_PATH"] = dataDirectory / "nami.duckdb",
                // A loopback development stack. The collector refuses to start in Production with no API keys.
                ["NAMI_OTLP_AUTH_MODE"] = "Unsecured",
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
            });

        try
        {
            var collectorAddress = new Uri($"http://127.0.0.1:{apiPort.ToString(CultureInfo.InvariantCulture)}");
            using var collectorClient = new HttpClient();
            collectorClient.BaseAddress = collectorAddress;
            collectorClient.Timeout = TimeSpan.FromSeconds(30);
            WaitUntil(collectorClient, "/health", "collector", collectorHost);

            var sampleAddress = new Uri($"http://127.0.0.1:{samplePort.ToString(CultureInfo.InvariantCulture)}");
            using var sampleHost = ApiSdkHost.Start(
                sample,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ASPNETCORE_ENVIRONMENT"] = "Development",
                    ["OTEL_SERVICE_NAME"] = "nami.sample",
                    ["OTEL_EXPORTER_OTLP_ENDPOINT"] = $"http://127.0.0.1:{otlpPort.ToString(CultureInfo.InvariantCulture)}",
                    ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
                    // The gate must not wait five seconds for the default batch delay after every request.
                    ["OTEL_BSP_SCHEDULE_DELAY"] = "500",
                },
                $"http://127.0.0.1:{samplePort.ToString(CultureInfo.InvariantCulture)}");

            try
            {
                using var sampleClient = new HttpClient();
                sampleClient.BaseAddress = sampleAddress;
                sampleClient.Timeout = TimeSpan.FromSeconds(30);
                WaitUntil(sampleClient, "/todos", "native sample", sampleHost);

                Drive(sampleClient, sessionId);

                AwaitControl(collectorClient, collectorHost);
                var spans = AwaitSession(collectorClient, sessionId, collectorHost);
                Assert(spans, revision, sessionId, contract);
            }
            catch
            {
                // The sample's stderr is where an export failure is written; it is the first thing worth reading.
                Log.Error("native sample output:{NewLine}{Output}", Environment.NewLine, sampleHost.Snapshot());
                throw;
            }
        }
        catch
        {
            Log.Error("collector output:{NewLine}{Output}", Environment.NewLine, collectorHost.Snapshot());
            throw;
        }
    }

    /// <summary>The untagged control: same API, same collector, no <c>baggage</c> header.</summary>
    private static readonly (string Method, string Route, string Status) s_control = ("GET", "/todos", "200");

    /// <summary>
    /// Three requests under one header, and a fourth with none. The sample has no telemetry line of its own;
    /// the header is the entire agent-side contract, and the control is how the gate knows the header is what
    /// put the other three in the session.
    /// </summary>
    private static void Drive(HttpClient client, string sessionId)
    {
        using (var invalid = Post(client, "/todos", """{"title":"no"}""", sessionId))
        {
            Expect(invalid.StatusCode is HttpStatusCode.BadRequest, "the session's invalid POST returns 400");
        }

        string id;
        using (var created = Post(client, "/todos", """{"title":"Observe the session","dueBy":"2026-09-05"}""", sessionId))
        {
            Expect(created.StatusCode is HttpStatusCode.Created, "the session's valid POST returns 201");
            id = JsonNode.Parse(created.Content.ReadAsStringAsync().GetAwaiter().GetResult())?["id"]
                     ?.GetValue<int>().ToString(CultureInfo.InvariantCulture)
                 ?? throw new InvalidOperationException("the created todo carries no id");
        }

        using (var xml = Get(client, $"/todos/{id}/xml", sessionId))
        {
            Expect(xml.StatusCode is HttpStatusCode.OK, "the session's XML read returns 200");
            Expect(xml.Content.Headers.ContentType?.MediaType is "application/xml",
                "the session's XML read answers application/xml");
        }

        // No header. It reaches the same collector over the same connection and must not reach the session.
        using var request = new HttpRequestMessage(HttpMethod.Get, s_control.Route);
        using var control = client.Send(request);
        Expect(control.StatusCode is HttpStatusCode.OK, "the untagged control request returns 200");
    }

    /// <summary>
    /// The collector's own read API is the oracle: <c>GET /api/v1/sessions/{id}/traces</c>, exactly what the
    /// MCP <c>list_sessions</c> / <c>get_trace</c> tools serve an agent.
    /// </summary>
    /// <remarks>
    /// A count is only believed once it stops moving. Spans leave the process in export batches, so the first
    /// poll that reaches three could be three of four — and "exactly three" would then pass on a duplicate that
    /// simply had not arrived yet. Two further polls must agree before the answer is used.
    /// </remarks>
    private static List<JsonNode> AwaitSession(HttpClient collector, string sessionId, ApiSdkHost collectorHost)
    {
        const int SettledPollsRequired = 2;
        var elapsed = Stopwatch.StartNew();
        var lastBody = string.Empty;
        List<JsonNode>? candidate = null;
        var settled = 0;

        while (elapsed.Elapsed < s_sessionTimeout)
        {
            var spans = ReadSessionSpans(collector, sessionId, out lastBody);
            if (spans is { Count: >= 3 })
            {
                settled = candidate is not null && candidate.Count == spans.Count ? settled + 1 : 0;
                candidate = spans;
                if (settled >= SettledPollsRequired)
                    return spans;
            }
            else
            {
                settled = 0;
                candidate = null;
            }

            Thread.Sleep(s_settleDelay);
        }

        Log.Error("collector output:{NewLine}{Output}", Environment.NewLine, collectorHost.Snapshot());
        throw new InvalidOperationException(
            $"The collector did not settle on an answer for session '{sessionId}' within {s_sessionTimeout}. " +
            $"Last body: {lastBody}");
    }

    private static List<JsonNode>? ReadSessionSpans(HttpClient collector, string sessionId, out string body)
    {
        using var response = collector
            .GetAsync($"/api/v1/sessions/{Uri.EscapeDataString(sessionId)}/traces")
            .GetAwaiter()
            .GetResult();
        body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        return response.StatusCode is HttpStatusCode.OK ? Spans(body) : null;
    }

    /// <summary>
    /// The untagged control, read back from the collector's unfiltered trace list. It proves the exclusion
    /// asserted below is an exclusion: the request was recorded, and it is still not in the session.
    /// </summary>
    private static void AwaitControl(HttpClient collector, ApiSdkHost collectorHost)
    {
        var elapsed = Stopwatch.StartNew();
        var lastBody = string.Empty;
        while (elapsed.Elapsed < s_sessionTimeout)
        {
            using var response = collector.GetAsync("/api/v1/traces").GetAwaiter().GetResult();
            lastBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (response.StatusCode is HttpStatusCode.OK && (Spans(lastBody) ?? []).Any(IsControl))
                return;

            Thread.Sleep(s_settleDelay);
        }

        Log.Error("last /api/v1/traces answer:{NewLine}{Body}", Environment.NewLine, lastBody);
        Log.Error("collector output:{NewLine}{Output}", Environment.NewLine, collectorHost.Snapshot());
        throw new InvalidOperationException(
            $"The untagged control request ({s_control.Method} {s_control.Route}) never reached the collector, " +
            "so its absence from the session would prove nothing.");
    }

    private static bool IsControl(JsonNode span) =>
        Attribute(span, MethodAttribute) == s_control.Method &&
        Attribute(span, RouteAttribute) == s_control.Route &&
        Attribute(span, StatusAttribute) == s_control.Status;

    private static List<JsonNode>? Spans(string body) =>
        JsonNode.Parse(body)?["items"]?.AsArray()
            .SelectMany(static trace => trace?["spans"]?.AsArray() ?? [])
            .Where(static span => span is not null)
            .Select(static span => span!)
            .ToList();

    private static void Assert(List<JsonNode> spans, string revision, string sessionId, AbsolutePath contract)
    {
        var paths = ContractPaths(contract);

        // One SERVER span per request and nothing else. Since Nami.Telemetry.Hosting 14.1.0 the ASP.NET Core
        // hosting activity is that span, enriched in place, rather than the parent of a second nami span; a
        // fourth span here means a request is being reported twice again.
        var observed = spans
            .Select(static span => (
                Route: Attribute(span, RouteAttribute),
                Method: Attribute(span, MethodAttribute),
                Status: Attribute(span, StatusAttribute),
                Domain: Attribute(span, DomainAttribute),
                Kind: AsText(span["kind"]),
                Name: AsText(span["name"]) ?? "(unnamed)",
                Revision: ResourceAttribute(span, ContractRevisionAttribute)))
            .OrderBy(static row => row.Route, StringComparer.Ordinal)
            .ThenBy(static row => row.Status, StringComparer.Ordinal)
            .ToList();

        foreach (var row in observed)
            Log.Information("  session: {Name} — {Method} {Route} -> {Status}", row.Name, row.Method, row.Route, row.Status);

        if (observed.Count is not 3)
        {
            Log.Error("session spans as the collector returned them:{NewLine}{Spans}",
                Environment.NewLine,
                string.Join(Environment.NewLine, spans.Select(static span => span.ToJsonString())));
        }

        Expect(observed.Count is 3,
            $"the session holds exactly one span per request that carried the header, not {observed.Count.ToString(CultureInfo.InvariantCulture)}");

        // The session query returns whole traces once any span of one matched, so membership alone would not
        // say the header did anything. The untagged control went to the same API on the same connection and
        // reached the collector (AwaitControl); its absence here is what the header is responsible for.
        Expect(!spans.Any(IsControl),
            $"the untagged control ({s_control.Method} {s_control.Route}) stayed out of the session");

        foreach (var row in observed)
        {
            Expect(row.Kind is ServerSpanKind, $"'{row.Name}' is a SERVER span (kind {row.Kind})");
            Expect(row.Domain is AspNetCoreServerDomain,
                $"'{row.Name}' was enriched by nami ({DomainAttribute}={AspNetCoreServerDomain}, saw '{row.Domain}')");
            Expect(string.Equals(row.Name, $"{row.Method} {row.Route}", StringComparison.Ordinal),
                $"the span is named '{{method}} {{route}}' (saw '{row.Name}')");
            Expect(string.Equals(row.Revision, revision, StringComparison.Ordinal),
                $"every span carries {ContractRevisionAttribute}={revision} (saw '{row.Revision}')");

            // The span and the document must name the same endpoint. A route template carries its constraints
            // and the OpenAPI path does not, so the span's route is compared the way the document spells it.
            var documented = WithoutRouteConstraints(row.Route);
            Expect(documented is not null && paths.Contains(documented),
                $"http.route '{row.Route}' is a path of the committed contract (as '{documented}'; it has {string.Join(", ", paths)})");
        }

        var calls = observed
            .Select(static row => $"{row.Method} {row.Status}")
            .OrderBy(static call => call, StringComparer.Ordinal)
            .ToList();
        Expect(calls.SequenceEqual(["GET 200", "POST 201", "POST 400"], StringComparer.Ordinal),
            $"the session is the validation 400, the create 201 and the XML 200 (saw {string.Join(", ", calls)})");

        var xml = observed.Where(static row => row.Route!.EndsWith("/xml", StringComparison.Ordinal)).ToList();
        Expect(xml.Count is 1, "exactly one span in the session is the XML endpoint");
        Expect(xml[0].Status is "200", $"the XML span reports 200 (saw '{xml[0].Status}')");

        Log.Information(
            "Nami.Api.Sdk: session '{Session}' answers with exactly its three SERVER spans, every route a path of the committed contract, and contract revision {Revision}",
            sessionId, revision);
    }

    private static HashSet<string> ContractPaths(AbsolutePath contract)
    {
        var document = JsonNode.Parse(contract.ReadAllText())
                       ?? throw new InvalidOperationException("The committed OpenAPI document is not JSON");
        return [.. document["paths"]!.AsObject().Select(static path => path.Key)];
    }

    /// <summary>
    /// A route template with its parameter constraints removed — <c>/todos/{id:int}/xml</c> becomes
    /// <c>/todos/{id}/xml</c> — which is how ASP.NET Core's OpenAPI generator writes the path key.
    /// </summary>
    private static string? WithoutRouteConstraints(string? route) =>
        route is null ? null : Regex.Replace(route, @"\{([^:?*}]+)[^}]*\}", "{$1}", RegexOptions.None, TimeSpan.FromSeconds(1));

    private static string? Attribute(JsonNode span, string key) => ReadAttribute(span["attributes"], key);

    private static string? ResourceAttribute(JsonNode span, string key) =>
        ReadAttribute(span["resource"]?["attributes"], key);

    /// <summary>
    /// Attributes reach the read API as <c>[{ "key": ..., "value": ... }]</c>; the value is the JSON the
    /// producer sent, so a route is a string and a status code a number.
    /// </summary>
    private static string? ReadAttribute(JsonNode? attributes, string key)
    {
        if (attributes is not JsonArray array)
            return null;

        foreach (var attribute in array)
        {
            if (attribute is null || AsText(attribute["key"]) != key)
                continue;

            var value = attribute["value"];

            // Non-string values arrive in the contract's typed form, {"type":"int","value":"400"}; a string
            // arrives as itself. Both are compared as text, so unwrap the one that has an envelope.
            if (value is JsonObject envelope && envelope.TryGetPropertyValue("value", out var inner))
                return AsText(inner);

            return AsText(value);
        }

        return null;
    }

    /// <summary>
    /// The read API returns attribute values as the JSON the producer sent, so a route arrives as a string
    /// and a status code as a number. Both are compared as text here; nothing about the assertion depends on
    /// which one the contract chose.
    /// </summary>
    private static string? AsText(JsonNode? node) =>
        node switch
        {
            null => null,
            JsonValue value => value.GetValueKind() switch
            {
                JsonValueKind.String => value.GetValue<string>(),
                JsonValueKind.Null => null,
                _ => value.ToJsonString(),
            },
            _ => node.ToJsonString(),
        };

    /// <summary>
    /// The oracle for nami.api.contract.revision, in the value format the registry defines and the
    /// collector's /health already reports: <c>sha256:&lt;lowercase hex&gt;</c>. Spelled here rather than
    /// re-derived from the SDK's own MSBuild, so a change to either side is what this gate catches.
    /// </summary>
    internal static string ContractRevisionValue(AbsolutePath file)
    {
        using var stream = File.OpenRead(file);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static void WaitUntil(HttpClient client, string path, string label, ApiSdkHost host)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < s_startupTimeout)
        {
            if (host.HasExited)
                throw new InvalidOperationException($"The {label} exited before it served:{Environment.NewLine}{host.Snapshot()}");

            try
            {
                using var response = client.GetAsync(path).GetAwaiter().GetResult();
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }

            Thread.Sleep(100);
        }

        throw new InvalidOperationException(
            $"The {label} did not serve {path} within {s_startupTimeout}:{Environment.NewLine}{host.Snapshot()}");
    }

    /// <summary>The agent's whole contribution: one header. Sent synchronously so the request outlives no task.</summary>
    private static HttpResponseMessage Post(HttpClient client, string path, string json, string sessionId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation(BaggageHeader, $"{SessionIdName}={sessionId}");
        return client.Send(request);
    }

    private static HttpResponseMessage Get(HttpClient client, string path, string sessionId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation(BaggageHeader, $"{SessionIdName}={sessionId}");
        return client.Send(request);
    }

    private static void Expect(bool condition, string what) => ApiSdkHost.Expect(condition, $"session: {what}");
}
