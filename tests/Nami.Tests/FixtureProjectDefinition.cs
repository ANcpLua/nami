using System.Collections.ObjectModel;
using System.Xml;
using System.Xml.Linq;
using JetBrains.Annotations;

namespace Nami.Tests;

/// <summary>An in-memory project document; configuring or inspecting it performs no file-system operations.</summary>
[PublicAPI]
public sealed class FixtureProjectDefinition
{
    private readonly List<XElement> _conditionalProperties = [];
    private readonly Dictionary<string, string> _packages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _properties = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _recordedProperties = new(StringComparer.Ordinal);
    private readonly List<string> _references = [];
    private readonly Dictionary<string, string> _sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, XElement> _targets = new(StringComparer.Ordinal);

    public FixtureProjectDefinition(string name, string targetFramework)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
        if (!char.IsLetter(name[0]) || name.Any(character =>
                !char.IsLetterOrDigit(character) && character is not '.' and not '_' and not '-'))
        {
            throw new ArgumentException("The fixture name must be a simple assembly name starting with a letter.",
                nameof(name));
        }

        Name = name;
        Sources = new ReadOnlyDictionary<string, string>(_sources);
        Packages = new ReadOnlyDictionary<string, string>(_packages);
        Properties = new ReadOnlyDictionary<string, string>(_properties);
        _properties.Add("TargetFramework", targetFramework);
        _properties.Add("OutputType", "Exe");
        _properties.Add("ImplicitUsings", "enable");
        _properties.Add("Nullable", "enable");
        _properties.Add("TreatWarningsAsErrors", "true");
        _properties.Add("JsonSerializerIsReflectionEnabledByDefault", "false");
    }

    public string Name { get; }
    public IReadOnlyDictionary<string, string> Sources { get; }
    public IReadOnlyDictionary<string, string> Packages { get; }
    public IReadOnlyDictionary<string, string> Properties { get; }

    public FixtureProjectDefinition AddSource(string relativePath, string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _sources.Add(NormalizeSourcePath(relativePath), source);
        return this;
    }

    public FixtureProjectDefinition WithProperty(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        switch (name)
        {
            case "TargetFramework": _properties.Remove("TargetFrameworks"); break;
            case "TargetFrameworks": _properties.Remove("TargetFramework"); break;
        }

        _properties[XmlConvert.VerifyNCName(name)] = value;
        return this;
    }

    public FixtureProjectDefinition WithProperty(string name, string value, string condition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(condition);
        ArgumentNullException.ThrowIfNull(value);
        _conditionalProperties.Add(new XElement("PropertyGroup", new XAttribute("Condition", condition),
            new XElement(XmlConvert.VerifyNCName(name), value)));
        return this;
    }

    public FixtureProjectDefinition WithTargetFrameworks(params string[] targetFrameworks)
    {
        ArgumentNullException.ThrowIfNull(targetFrameworks);
        if (targetFrameworks.Length == 0 || targetFrameworks.Any(string.IsNullOrWhiteSpace)
                                         || targetFrameworks.Distinct(StringComparer.Ordinal).Count() !=
                                         targetFrameworks.Length)
            throw new ArgumentException("Supply distinct non-empty target frameworks.", nameof(targetFrameworks));
        _properties.Remove("TargetFramework");
        _properties["TargetFrameworks"] = string.Join(';', targetFrameworks);
        return this;
    }

    public FixtureProjectDefinition WithCustomTarget(FixtureTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Name is "_RecordFixtureProperties" or "_RecordInitialFixtureProperties")
            throw new ArgumentException("The target name is reserved.", nameof(target));
        _targets.Add(target.Name, target.ToElement());
        return this;
    }

    public FixtureProjectDefinition RecordProperties(params string[] propertyNames)
    {
        ArgumentNullException.ThrowIfNull(propertyNames);
        foreach (var name in propertyNames) _recordedProperties.Add(XmlConvert.VerifyNCName(name));
        return this;
    }

    public FixtureProjectDefinition AddPackage(string id, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        _packages.Add(id, version);
        return this;
    }

    public FixtureProjectDefinition AddAssemblyReference(string path)
    {
        _references.Add(Path.GetFullPath(path));
        return this;
    }

    public XDocument ToProjectDocument()
    {
        return new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", _properties.Select(property => new XElement(property.Key, property.Value))),
            _conditionalProperties.Select(group => new XElement(group)),
            new XElement("ItemGroup",
                _packages.Keys.Select(id => new XElement("PackageReference", new XAttribute("Include", id)))),
            new XElement("ItemGroup", _references.Select(path => new XElement("Reference",
                new XAttribute("Include", Path.GetFileNameWithoutExtension(path)), new XElement("HintPath", path)))),
            _targets.Values.Select(target => new XElement(target)),
            RecordedPropertiesTarget(true), RecordedPropertiesTarget(false)));
    }

    private XElement? RecordedPropertiesTarget(bool initial)
    {
        if (_recordedProperties.Count == 0) return null;
        var file = initial
            ? "$(IntermediateOutputPath)fixture-initial-properties.$(TargetFramework).txt"
            : "$(IntermediateOutputPath)fixture-properties.$(TargetFramework).txt";
        var item = initial ? "_InitialFixtureProperty" : "_FixtureProperty";
        return new XElement("Target",
            new XAttribute("Name", initial ? "_RecordInitialFixtureProperties" : "_RecordFixtureProperties"),
            new XAttribute(initial ? "BeforeTargets" : "AfterTargets", initial ? "PrepareForBuild" : "Build"),
            new XAttribute("Condition", "'$(IsCrossTargetingBuild)' != 'true'"),
            new XElement("MakeDir", new XAttribute("Directories", "$(IntermediateOutputPath)")),
            new XElement("ItemGroup", _recordedProperties.Select(name => new XElement(item,
                new XAttribute("Include", name), new XElement("Value", "$(" + name + ")")))),
            new XElement("WriteLinesToFile", new XAttribute("File", file),
                new XAttribute("Lines", "@(" + item + "->'%(Identity)=%(Value)')"),
                new XAttribute("Overwrite", "true")),
            new XElement("ItemGroup", new XElement("FileWrites", new XAttribute("Include", file)),
                new XElement("_FixturePropertyFile", new XAttribute("Include", file)),
                new XElement("EmbedInBinlog", new XAttribute("Include", "@(_FixturePropertyFile->'%(FullPath)')"))));
    }

    public XDocument ToPackageDocument()
    {
        return new XDocument(new XElement("Project",
            new XElement("PropertyGroup", new XElement("ManagePackageVersionsCentrally", true)),
            new XElement("ItemGroup", _packages.Select(package => new XElement("PackageVersion",
                new XAttribute("Include", package.Key), new XAttribute("Version", package.Value))))));
    }

    internal static string NormalizeSourcePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = path.Replace('\\', '/');
        if (Path.IsPathRooted(path) || normalized.Contains(':')
                                    || normalized.Split('/').Any(segment => segment is "" or "." or "..")
                                    || !normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A fixture source must be a relative C# path without traversal.", nameof(path));
        return normalized;
    }
}
