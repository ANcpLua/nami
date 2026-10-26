using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using JetBrains.Annotations;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;

namespace Nami.Tests;

/// <summary>A disposable, ordinary .NET consumer built outside the test project's import hierarchy.</summary>
[PublicAPI]
public sealed class FixtureProject : IAsyncDisposable
{
    private readonly TestContext? _context;
    private readonly XElement _defaults;
    private readonly Dictionary<string, string?> _environment = new(StringComparer.Ordinal);
    private readonly List<FixtureEvidence> _evidence = [];
    private readonly string _identity = Guid.NewGuid().ToString("N");
    private readonly List<PackageFeedFixture> _packageFeeds = [];
    private readonly TimeProvider _timeProvider;
    private bool _disposed;
    private int _operation;

    public FixtureProject(string name, TimeProvider? timeProvider = null, TestContext? context = null)
    {
        _context = context;
        _defaults = XElement.Load(Path.Combine(AppContext.BaseDirectory, "fixture-defaults.xml"));
        var targetFramework = (string?)_defaults.Attribute("TargetFramework")
                              ?? throw new InvalidDataException("The fixture target framework was not staged.");
        Definition = new FixtureProjectDefinition(name, targetFramework);
        _timeProvider = timeProvider ?? TimeProvider.System;
        DirectoryPath = Path.Combine(Environment.GetEnvironmentVariable("NAMI_FIXTURE_WORK_ROOT")
                                     ?? Path.GetTempPath(), "nami-fixture-" + Guid.NewGuid().ToString("N"));
        ProjectPath = Path.Combine(DirectoryPath, name + ".csproj");
    }

    public string DirectoryPath { get; }
    public string ProjectPath { get; }
    public FixtureProjectDefinition Definition { get; }
    public IEnumerable<string> EvidenceFiles => _evidence.SelectMany(evidence => evidence.Files);

    private string PackageCache => _packageFeeds.Count == 0
        ? Path.Combine(DirectoryPath, ".nuget", "packages")
        : _packageFeeds[0].CacheDirectory;

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    private static TestContext? CurrentContext()
    {
#pragma warning disable MSTESTEXP
        return TestContext.Current;
#pragma warning restore MSTESTEXP
    }

    private FixtureEvidenceCapture? BeginEvidence(string command)
    {
        var context = _context ?? CurrentContext();
        return context is null
            ? null
            : new FixtureEvidenceCapture(
                context, Path.Combine(context.TestTempDirectory
                                      ?? throw new InvalidOperationException(
                                          "MSTest did not provide an evidence directory."), "fixture-" + _identity),
                ++_operation, command, DirectoryPath, Definition);
    }

    private FixtureEvidence? PreserveEvidence(FixtureEvidenceCapture? capture, FixtureBuildResult? result,
        Exception? failure)
    {
        if (capture is null) return null;
        try
        {
            var evidence = capture.Complete(result, failure);
            _evidence.Add(evidence);
            return evidence;
        }
        catch (Exception preservation) when (failure is not null)
        {
            throw new AggregateException("Fixture execution and evidence preservation both failed.", failure,
                preservation);
        }
    }

    public FixtureProject AddSource(string relativePath, string source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Definition.AddSource(relativePath, source);
        return this;
    }

    public FixtureProject WithProperty(string name, string value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Definition.WithProperty(name, value);
        return this;
    }

    public FixtureProject WithProperty(string name, string value, string condition)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Definition.WithProperty(name, value, condition);
        return this;
    }

    public FixtureProject WithTargetFrameworks(params string[] frameworks)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Definition.WithTargetFrameworks(frameworks);
        return this;
    }

    public FixtureProject WithCustomTarget(FixtureTarget target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Definition.WithCustomTarget(target);
        return this;
    }

    public FixtureProject RecordProperties(params string[] names)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Definition.RecordProperties(names);
        return this;
    }

    public FixtureProject WithPackageFeed(PackageFeedFixture feed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(feed);
        _packageFeeds.Add(feed);
        return this;
    }

    public FixtureProject WithEnvironment(string name, string? value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _environment[name] = value;
        return this;
    }

    public FixtureProject AddPackage(string id, string? version = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        version ??= _defaults.Elements("Package")
            .Where(package => string.Equals((string?)package.Attribute("Id"), id, StringComparison.OrdinalIgnoreCase))
            .Select(package => (string?)package.Attribute("Version")).FirstOrDefault();
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        Definition.AddPackage(id, version);
        return this;
    }

    public FixtureProject AddAssemblyReference(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Definition.AddAssemblyReference(path);
        return this;
    }

    /// <summary>Compiles the shared Capture sources into the consumer without referencing the test assembly.</summary>
    public FixtureProject AddCaptureSources()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var directory = Path.Combine(AppContext.BaseDirectory, "fixture-sources", "capture");
        var files = Directory.GetFiles(directory, "*.cs").Order(StringComparer.Ordinal).ToArray();
        if (files.Length == 0) throw new InvalidDataException("The Capture fixture sources were not staged.");
        foreach (var file in files) AddSource("Capture/" + Path.GetFileName(file), File.ReadAllText(file));
        if (!Definition.Packages.ContainsKey("OpenTelemetry")) AddPackage("OpenTelemetry");
        if (!Definition.Packages.ContainsKey("JetBrains.Annotations")) AddPackage("JetBrains.Annotations");
        return this;
    }

    public Task<FixtureBuildResult> BuildAsync(CancellationToken cancellationToken)
    {
        return RunAsync("build", false, cancellationToken);
    }

    public Task<FixtureBuildResult> RestoreAsync(CancellationToken cancellationToken)
    {
        return RunAsync("restore", false, cancellationToken);
    }

    public Task<FixtureBuildResult> PackAsync(CancellationToken cancellationToken)
    {
        return RunAsync("pack", false, cancellationToken);
    }

    public Task<FixtureBuildResult> PublishAsync(bool nativeAot, CancellationToken cancellationToken)
    {
        return RunAsync("publish", nativeAot, cancellationToken);
    }

    private async Task<FixtureBuildResult> RunAsync(string command, bool nativeAot, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var frameworks = Definition.Properties.TryGetValue("TargetFrameworks", out var multiple)
            ? multiple.Split(';').ToImmutableArray()
            : [Definition.Properties["TargetFramework"]];
        if (command == "publish" && frameworks.Length > 1)
            throw new InvalidOperationException("Publish a single target framework at a time.");
        var capture = BeginEvidence(command);
        FixtureBuildResult? build = null;
        Exception? failure = null;
        try
        {
            if (Directory.Exists(DirectoryPath))
            {
                foreach (var file in Directory.EnumerateFiles(DirectoryPath, "fixture-properties.*.txt",
                                 SearchOption.AllDirectories)
                             .Concat(Directory.EnumerateFiles(DirectoryPath, "fixture-initial-properties.*.txt",
                                 SearchOption.AllDirectories))) File.Delete(file);
            }

            var output = command == "pack"
                ? Path.Combine(DirectoryPath, "packages", Guid.NewGuid().ToString("N"))
                : Path.Combine(DirectoryPath, command == "publish" ? nativeAot ? "native" : "published" : "output");
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                WorkingDirectory = DirectoryPath
            };
            foreach (var argument in new[]
                     {
                         command, ProjectPath, "-p:Configuration=Release", "-m:1", "-nodeReuse:false",
                         "-p:UseSharedCompilation=false",
                         "-bl:" + (capture?.BinlogPath ?? Path.Combine(DirectoryPath,
                             command + "-" + Guid.NewGuid().ToString("N") + ".binlog")) + ";ProjectImports=Embed"
                     })
                start.ArgumentList.Add(argument);
            if (command == "build" && frameworks.Length > 1)
                start.ArgumentList.Add("-p:BaseOutputPath=" + output + Path.DirectorySeparatorChar);
            else if (command != "restore")
            {
                start.ArgumentList.Add("--output");
                start.ArgumentList.Add(output);
            }

            if (command == "publish")
            {
                start.ArgumentList.Add("--runtime");
                start.ArgumentList.Add(RuntimeInformation.RuntimeIdentifier);
                start.ArgumentList.Add("-p:PublishAot=" + (nativeAot ? "true" : "false"));
            }

            foreach (var key in start.Environment.Keys.Where(key => key == "CI" || key == "DOTNET_ENVIRONMENT"
                             || key.StartsWith("GITHUB", StringComparison.Ordinal) ||
                             key.StartsWith("RUNNER_", StringComparison.Ordinal)
                             || key.StartsWith("VSTEST", StringComparison.OrdinalIgnoreCase))
                         .ToArray()) start.Environment[key] = "";
            start.Environment["NUGET_PACKAGES"] = PackageCache;
            start.Environment["NUGET_HTTP_CACHE_PATH"] = Path.Combine(DirectoryPath, ".nuget", "http");
            start.Environment["NUGET_SCRATCH"] = Path.Combine(DirectoryPath, ".nuget", "scratch");
            start.Environment["NUGET_PLUGINS_CACHE_PATH"] = Path.Combine(DirectoryPath, ".nuget", "plugins");
            foreach (var (key, value) in _environment) start.Environment[key] = value;
            capture?.RecordCommand(start);
            MaterializeCore(capture);
            var result = await ChildProcess.RunAsync(start, "generated fixture " + Definition.Name, cancellationToken,
                new ChildProcessOptions { TimeProvider = _timeProvider }).ConfigureAwait(false);
            build = new FixtureBuildResult(result.ExitCode, result.Output, result.Error, output, Definition.Name)
            {
                RecordedProperties = result.ExitCode == 0
                    ? ReadRecordedProperties()
                    : ImmutableDictionary<string, ImmutableDictionary<string, string>>.Empty,
                TargetFrameworks = frameworks
            };
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        var evidence = PreserveEvidence(capture, build, failure);
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        return (build ?? throw new InvalidOperationException("The fixture produced no build result.")) with
        {
            Evidence = evidence
        };
    }

    /// <summary>Writes the current project document into its owned workspace without invoking the compiler.</summary>
    public void Materialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var capture = BeginEvidence("materialize");
        Exception? failure = null;
        try
        {
            MaterializeCore(capture);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        PreserveEvidence(capture, null, failure);
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void MaterializeCore(FixtureEvidenceCapture? capture)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixture-global.json"),
            Path.Combine(DirectoryPath, "global.json"), true);
        WriteNuGetConfiguration();
        new XDocument(new XElement("Project")).Save(Path.Combine(DirectoryPath, "Directory.Build.props"));
        new XDocument(new XElement("Project")).Save(Path.Combine(DirectoryPath, "Directory.Build.targets"));
        SaveProject(Definition.ToPackageDocument(), Path.Combine(DirectoryPath, "Directory.Packages.props"));
        var document = Definition.ToProjectDocument();
        if (capture is not null)
        {
            capture.WriteInputManifest();
            document.Root!.Add(new XElement("ItemGroup", capture.InputPaths.Select(path =>
                    new XElement("EmbedInBinlog", new XAttribute("Include", ProjectCollection.Escape(path)))),
                new XElement("None", new XAttribute("Update", "fixture-invocation.xml"),
                    new XAttribute("Pack", "false"), new XAttribute("CopyToOutputDirectory", "Never"),
                    new XAttribute("CopyToPublishDirectory", "Never"))));
        }

        SaveProject(document, ProjectPath);
        foreach (var (relativePath, source) in Definition.Sources)
        {
            var path = Path.Combine(DirectoryPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, source);
        }

        capture?.CaptureInputs();
    }

    private void WriteNuGetConfiguration()
    {
        var sources = new XElement("packageSources", new XElement("clear"));
        var mapping = new XElement("packageSourceMapping");
        for (var index = 0; index < _packageFeeds.Count; index++)
        {
            var feed = _packageFeeds[index];
            feed.EnsureAvailable();
            var name = "FixtureFeed" + index;
            sources.Add(new XElement("add", new XAttribute("key", name), new XAttribute("value", feed.DirectoryPath)));
            mapping.Add(new XElement("packageSource", new XAttribute("key", name),
                feed.PackageIds.Select(id => new XElement("package", new XAttribute("pattern", id)))));
        }

        sources.Add(new XElement("add", new XAttribute("key", "nuget.org"),
            new XAttribute("value", "https://api.nuget.org/v3/index.json")));
        mapping.Add(new XElement("packageSource", new XAttribute("key", "nuget.org"),
            new XElement("package", new XAttribute("pattern", "*"))));
        new XDocument(new XElement("configuration", new XElement("config",
                new XElement("add", new XAttribute("key", "globalPackagesFolder"),
                    new XAttribute("value", PackageCache))), sources, mapping))
            .Save(Path.Combine(DirectoryPath, "NuGet.Config"));
    }

    private static void SaveProject(XDocument document, string path)
    {
        using var collection = new ProjectCollection();
        using var reader = document.CreateReader();
        ProjectRootElement.Create(reader, collection).Save(path);
    }

    private ImmutableDictionary<string, ImmutableDictionary<string, string>> ReadRecordedProperties()
    {
        const string Prefix = "fixture-properties.";
        var result =
            ImmutableDictionary.CreateBuilder<string, ImmutableDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(DirectoryPath, Prefix + "*.txt", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            var framework = Path.GetFileNameWithoutExtension(file)[Prefix.Length..];
            var properties = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(file))
            {
                var separator = line.IndexOf('=');
                if (separator <= 0) throw new InvalidDataException("Invalid recorded property: " + line);
                properties.Add(line[..separator], line[(separator + 1)..]);
            }

            result.Add(framework, properties.ToImmutable());
        }

        return result.ToImmutable();
    }
}

/// <summary>Compiler diagnostics and the output directory of one generated consumer build.</summary>
[PublicAPI]
public sealed record FixtureBuildResult(
    int ExitCode,
    string Output,
    string Error,
    string OutputDirectory,
    string AssemblyName)
{
    public bool Succeeded => ExitCode == 0;
    public FixtureEvidence? Evidence { get; init; }

    public ImmutableDictionary<string, ImmutableDictionary<string, string>> RecordedProperties { get; init; } =
        ImmutableDictionary<string, ImmutableDictionary<string, string>>.Empty;

    public ImmutableArray<string> TargetFrameworks { get; init; } = [];

    public string AssemblyPath => TargetFrameworks.Length > 1
        ? throw new InvalidOperationException("Select an assembly with GetAssemblyPath(targetFramework).")
        : Path.Combine(OutputDirectory, AssemblyName + ".dll");

    public string NativePath =>
        Path.Combine(OutputDirectory, AssemblyName + (OperatingSystem.IsWindows() ? ".exe" : ""));

    public string? GetRecordedProperty(string name, string? targetFramework = null)
    {
        if (RecordedProperties.Count == 0) return null;
        if (targetFramework is not null)
            return RecordedProperties.GetValueOrDefault(targetFramework)?.GetValueOrDefault(name);
        return RecordedProperties.Count == 1
            ? RecordedProperties.Values.Single().GetValueOrDefault(name)
            : throw new InvalidOperationException("Specify a target framework when a build produces more than one.");
    }

    public string GetAssemblyPath(string targetFramework)
    {
        return TargetFrameworks.Contains(targetFramework)
            ? TargetFrameworks.Length > 1
                ? Path.Combine(OutputDirectory, "Release", targetFramework, AssemblyName + ".dll")
                : AssemblyPath
            : throw new ArgumentException("The build did not target this framework.", nameof(targetFramework));
    }
}
