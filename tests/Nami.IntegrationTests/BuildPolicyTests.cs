using System.Text.Json;
using System.Xml.Linq;
using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

/// <summary>
/// The warning policy of Directory.Build.props as dotnet evaluates it: a Release build and every CI build fail on
/// any warning and on any NuGet audit finding; a local Debug build keeps warnings as warnings and still audits.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class BuildPolicyTests(TestContext context)
{
    private static readonly string[] s_properties =
    [
        "TreatWarningsAsErrors", "WarningsAsErrors", "WarningsNotAsErrors", "NuGetAudit", "NuGetAuditMode",
        "NuGetAuditLevel"
    ];

    // A known vulnerability of low, moderate, high or critical severity.
    private static readonly string[] s_auditFindings = ["NU1901", "NU1902", "NU1903", "NU1904"];

    // At NativeAOT publish IL2104 and IL3053 summarize the trim and AOT warnings of a package assembly; today only
    // DuckDB.NET.Data has them. NU1900 says the vulnerability data could not be read, which is not a finding.
    private static readonly string[] s_warningsKeptAsWarnings = ["IL2104", "IL3053", "NU1900"];

    private static readonly (string Name, string Value)[] s_auditSettings =
        [("NuGetAudit", "true"), ("NuGetAuditMode", "all"), ("NuGetAuditLevel", "low")];

    private static readonly char[] s_separators = [';', ','];

    [TestMethod]
    [Timeout(180000, CooperativeCancellation = true)]
    public async Task ReleaseBuild_EveryCSharpProjectOfTheSolution_FailsOnWarningsAndAuditFindings()
    {
        var projects = SolutionProjects();
        Assert.IsNotEmpty(projects);
        var problems = new List<string>();
        foreach (var project in projects)
        {
            var properties = await EvaluateAsync(project, release: true, ci: false);
            problems.AddRange(StrictPolicyProblems(properties).Select(problem => project + ": " + problem));
        }

        Assert.IsEmpty(problems, string.Join(Environment.NewLine, problems));
    }

    [TestMethod]
    public async Task CiBuildWithoutConfiguration_FailsOnWarningsAndAuditFindings()
    {
        // eng/build.sh builds this project without -c, and on GitHub Actions only CI=true marks the build.
        var properties = await EvaluateAsync("eng/build/build.csproj", release: false, ci: true);
        var problems = StrictPolicyProblems(properties);
        Assert.IsEmpty(problems, string.Join(Environment.NewLine, problems));
    }

    [TestMethod]
    public async Task LocalDebugBuild_KeepsWarningsAsWarnings_AndStillAudits()
    {
        var properties = await EvaluateAsync("src/nami/nami.csproj", release: false, ci: false);
        Assert.AreNotEqual("true", properties["TreatWarningsAsErrors"], ignoreCase: true);
        var errors = Codes(properties["WarningsAsErrors"]);
        Assert.IsEmpty(s_auditFindings.Where(errors.Contains).ToArray(), properties["WarningsAsErrors"]);
        Assert.Contains("CS0612", errors, properties["WarningsAsErrors"]);
        foreach (var (name, expected) in s_auditSettings)
            Assert.AreEqual(expected, properties[name], ignoreCase: true, name);
    }

    private static string[] SolutionProjects()
    {
        return [.. XDocument.Load(Path.Combine(ProcessRunner.RepositoryRoot, "nami.slnx"))
            .Descendants("Project")
            .Select(static project => (string?)project.Attribute("Path"))
            .OfType<string>()
            .Where(static path => path.EndsWith(".csproj", StringComparison.Ordinal))];
    }

    private static List<string> StrictPolicyProblems(Dictionary<string, string> properties)
    {
        var problems = new List<string>();
        if (!string.Equals(properties["TreatWarningsAsErrors"], "true", StringComparison.OrdinalIgnoreCase))
            problems.Add("TreatWarningsAsErrors is '" + properties["TreatWarningsAsErrors"] + "'");
        var errors = Codes(properties["WarningsAsErrors"]);
        var missing = s_auditFindings.Append("CS0612").Where(code => !errors.Contains(code)).ToArray();
        if (missing.Length > 0) problems.Add("WarningsAsErrors lacks " + string.Join(';', missing));
        if (!Codes(properties["WarningsNotAsErrors"]).SetEquals(s_warningsKeptAsWarnings))
            problems.Add("WarningsNotAsErrors is '" + properties["WarningsNotAsErrors"] + "', expected exactly " +
                         string.Join(';', s_warningsKeptAsWarnings));
        foreach (var (name, expected) in s_auditSettings)
        {
            if (!string.Equals(properties[name], expected, StringComparison.OrdinalIgnoreCase))
                problems.Add(name + " is '" + properties[name] + "'");
        }

        return problems;
    }

    private static HashSet<string> Codes(string value)
    {
        return value.Split(s_separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static code => code.ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);
    }

    private async Task<Dictionary<string, string>> EvaluateAsync(string project, bool release, bool ci)
    {
        List<string> arguments = ["msbuild", project, .. s_properties.Select(static name => "-getProperty:" + name)];
        if (release) arguments.Add("-p:Configuration=Release");
        // Only the switches under test decide: an inherited CI or Configuration variable must not.
        var environment = new Dictionary<string, string?> { ["CI"] = ci ? "true" : "", ["Configuration"] = "" };
        var result = await ProcessRunner.RunAsync("dotnet", arguments, ProcessRunner.RepositoryRoot,
            context.CancellationToken, environment);
        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        var json = result.Output.IndexOf('{', StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, json, result.Output);
        using var document = JsonDocument.Parse(result.Output[json..]);
        return document.RootElement.GetProperty("Properties").EnumerateObject()
            .ToDictionary(static property => property.Name, static property => property.Value.GetString() ?? "",
                StringComparer.Ordinal);
    }
}
