using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using JetBrains.Annotations;

namespace Nami.Tests;

/// <summary>Files retained by MSTest independently of a disposable consumer workspace.</summary>
[PublicAPI]
public sealed record FixtureEvidence(
    string DirectoryPath,
    string ManifestPath,
    string InputsPath,
    string PropertiesPath,
    string OutputPath,
    string? BinlogPath)
{
    public IEnumerable<string> Files => BinlogPath is null
        ? [ManifestPath, InputsPath, PropertiesPath, OutputPath]
        : [ManifestPath, InputsPath, PropertiesPath, OutputPath, BinlogPath];
}

internal sealed class FixtureEvidenceCapture
{
    private const string InputManifestName = "fixture-invocation.xml";
    private readonly TestContext _context;
    private readonly string _directory;
    private readonly bool _executesSdk;
    private readonly string[] _inputs;
    private readonly XDocument _manifest;
    private readonly string _workspace;

    internal FixtureEvidenceCapture(TestContext context, string root, int sequence, string command,
        string workspace, FixtureProjectDefinition definition)
    {
        _context = context;
        _executesSdk = command != "materialize";
        _workspace = workspace;
        _directory = Path.Combine(root, $"{sequence:D4}-{command}");
        Directory.CreateDirectory(_directory);
        _inputs =
        [
            "global.json", "NuGet.Config", "Directory.Build.props", "Directory.Build.targets",
            "Directory.Packages.props", definition.Name + ".csproj", InputManifestName, .. definition.Sources.Keys
        ];
        _manifest = new XDocument(new XElement("FixtureInvocation", new XAttribute("Command", command),
            new XAttribute("Sequence", sequence), new XAttribute("Workspace", workspace),
            new XElement("Definition", definition.ToProjectDocument().Root),
            new XElement("Inputs", _inputs.Select(path => new XElement("File", new XAttribute("Path", path))))));
        _manifest.Save(ManifestPath);
    }

    private string ManifestPath => Path.Combine(_directory, "invocation.xml");
    internal string BinlogPath => Path.Combine(_directory, "build.binlog");
    internal IEnumerable<string> InputPaths => _inputs.Select(path => Path.Combine(_workspace, path));

    internal void WriteInputManifest()
    {
        _manifest.Save(Path.Combine(_workspace, InputManifestName));
    }

    internal void RecordCommand(ProcessStartInfo start)
    {
        _manifest.Root!.Add(new XElement("Execution", new XAttribute("FileName", start.FileName),
            new XAttribute("WorkingDirectory", start.WorkingDirectory),
            start.ArgumentList.Select(argument => new XElement("Argument", argument))));
        _manifest.Save(ManifestPath);
    }

    internal void CaptureInputs()
    {
        var path = Path.Combine(_directory, "inputs.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var name in _inputs)
        {
            var source = Path.Combine(_workspace, name);
            if (File.Exists(source)) archive.CreateEntryFromFile(source, name.Replace('\\', '/'));
        }
    }

    internal FixtureEvidence Complete(FixtureBuildResult? result, Exception? failure)
    {
        var inputsPath = Path.Combine(_directory, "inputs.zip");
        if (!File.Exists(inputsPath)) CaptureInputs();
        var propertiesPath = Path.Combine(_directory, "properties.xml");
        var snapshots = _executesSdk && Directory.Exists(_workspace)
            ? Directory.EnumerateFiles(_workspace, "fixture-initial-properties.*.txt", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(_workspace, "fixture-properties.*.txt", SearchOption.AllDirectories))
                .Order(StringComparer.Ordinal).Select(ReadProperties).ToArray()
            : [];
        new XDocument(new XElement("RecordedProperties", new XAttribute("Available", snapshots.Length != 0), snapshots))
            .Save(propertiesPath);
        var outputPath = Path.Combine(_directory, "output.log");
        File.WriteAllText(outputPath, result is null
            ? failure?.ToString() ?? "Materialized without running the SDK."
            : $"Exit code: {result.ExitCode}\nstdout:\n{result.Output}\nstderr:\n{result.Error}", Encoding.UTF8);
        _manifest.Root!.Add(new XElement("Outcome",
            new XAttribute("Status",
                failure is null
                    ? result is null ? "Materialized" : result.Succeeded ? "Succeeded" : "Failed"
                    : "Interrupted"),
            result is null ? null : new XAttribute("ExitCode", result.ExitCode),
            failure is null
                ? null
                : new XElement("Exception",
                    new XAttribute("Type", failure.GetType().FullName ?? failure.GetType().Name),
                    new XAttribute("Details", "output.log"))));
        _manifest.Save(ManifestPath);
        var evidence = new FixtureEvidence(_directory, ManifestPath, inputsPath, propertiesPath, outputPath,
            File.Exists(BinlogPath) ? BinlogPath : null);
        foreach (var path in evidence.Files) _context.AddResultFile(path);
        return evidence;
    }

    private XElement ReadProperties(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        const string InitialPrefix = "fixture-initial-properties.";
        const string FinalPrefix = "fixture-properties.";
        var initial = name.StartsWith(InitialPrefix, StringComparison.Ordinal);
        return new XElement("Snapshot", new XAttribute("Phase", initial ? "BeforeBuild" : "AfterBuild"),
            new XAttribute("TargetFramework", name[(initial ? InitialPrefix.Length : FinalPrefix.Length)..]),
            new XAttribute("Path", Path.GetRelativePath(_workspace, path)),
            File.ReadLines(path).Select(line =>
            {
                var separator = line.IndexOf('=');
                return separator > 0
                    ? new XElement("Property", new XAttribute("Name", line[..separator]), line[(separator + 1)..])
                    : new XElement("UnparsedLine", line);
            }));
    }
}
