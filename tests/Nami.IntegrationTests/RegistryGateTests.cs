using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nami.SemanticConventions;
using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
public sealed class RegistryGateTests(TestContext context)
{
    private static readonly string[] GeneratedDirectories =
        ["internal/nami.semconv/Generated", "crates/nami-semconv/src", "docs/semconv"];

    private static readonly string[] GenerationCheckArgs = ["registry/generate.sh", "--check"];
    private static string Root => ProcessRunner.RepositoryRoot;

    private static string Weaver
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("WEAVER");
            if (!string.IsNullOrWhiteSpace(configured)) return configured;
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local/share/mise/installs/github-open-telemetry-weaver/0.28.0/weaver");
            return File.Exists(path) ? path : "weaver";
        }
    }

    [DependsOn(nameof(Catalog_MatchesResolvedRegistry))]
    [TestMethod]
    [Timeout(180000, CooperativeCancellation = true)]
    public async Task Registry_Check_PassesWithFuture()
    {
        var result = await ProcessRunner.RunAsync(Weaver,
        [
            "--future", "registry", "check", "--v2", "-r", Path.Combine(Root, "registry"), "-p",
            Path.Combine(Root, "registry/policies")
        ], Root, context.CancellationToken);
        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
    }

    [TestMethod]
    public async Task Catalog_MatchesResolvedRegistry()
    {
        using var stream =
            typeof(RegistryInfo).Assembly.GetManifestResourceStream("Nami.SemanticConventions.RegistryInventory.json");
        Assert.IsNotNull(stream);
        using var data = await JsonDocument.ParseAsync(stream, cancellationToken: context.CancellationToken);
        var counts = data.RootElement.GetProperty("counts");
        Assert.AreEqual(121, counts.GetProperty("attributes").GetProperty("stable").GetInt32());
        Assert.AreEqual(51, counts.GetProperty("metrics").GetProperty("stable").GetInt32());
        Assert.AreEqual(8, counts.GetProperty("spans").GetProperty("stable_refinements").GetInt32());
        Assert.AreEqual(1, counts.GetProperty("events").GetProperty("stable").GetInt32());
        Assert.AreEqual(5, counts.GetProperty("entities").GetProperty("stable").GetInt32());
        using var resolved =
            JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Root, "registry/resolved.json"),
                context.CancellationToken));
        var catalog = resolved.RootElement.GetProperty("attribute_catalog");
        var keys = resolved.RootElement.GetProperty("registry").GetProperty("attributes").EnumerateArray()
            .Select(i => catalog[i.GetInt32()].GetProperty("key").GetString()!).Order(StringComparer.Ordinal).ToArray();
        var generatedKeys = AttributeCatalog.All.Select(static a => a.Key).Order(StringComparer.Ordinal).ToArray();
        Assert.AreEqual(keys.Length, generatedKeys.Length);
        Assert.HasCount(0, keys.Except(generatedKeys, StringComparer.Ordinal).ToArray());
        Assert.HasCount(0, generatedKeys.Except(keys, StringComparer.Ordinal).ToArray());
        Assert.IsTrue(
            AttributeCatalog.All.All(static a => a.Stability != Stability.Unknown && a.SourceSchema.Length > 0));
    }

    [DependsOn(nameof(Catalog_MatchesResolvedRegistry))]
    [TestMethod]
    public void Registry_NestedCoreDependency_ResolvesOnce()
    {
        Assert.HasCount(1, AttributeCatalog.All.Where(static a => a.Key == "http.request.method").ToArray());
    }

    [DependsOn(nameof(Catalog_MatchesResolvedRegistry))]
    [TestMethod]
    [Timeout(240000, CooperativeCancellation = true)]
    [DoNotParallelize]
    public async Task Generate_TwiceSameRegistry_IsByteIdentical()
    {
        var before = Snapshot();
        for (var i = 0; i < 2; i++)
        {
            var run = await ProcessRunner.RunAsync("bash", GenerationCheckArgs, Root, context.CancellationToken);
            Assert.AreEqual(0, run.ExitCode, run.Output + run.Error);
        }

        Assert.AreSequenceEqual(before, Snapshot());
    }

    private static string[] Snapshot()
    {
        return GeneratedDirectories
            .SelectMany(dir => Directory.GetFiles(Path.Combine(Root, dir), "*", SearchOption.AllDirectories))
            .Order(StringComparer.Ordinal)
            .Select(path =>
                Path.GetRelativePath(Root, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
            .ToArray();
    }

    [DependsOn(nameof(Catalog_MatchesResolvedRegistry))]
    [TestMethod]
    public void NamiKeys_AppearOnlyInGeneratedFiles()
    {
        var offending = Directory.GetFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Contains("/obj/", StringComparison.Ordinal) &&
                                  !path.Contains("/bin/", StringComparison.Ordinal) &&
                                  !path.EndsWith(".g.cs", StringComparison.Ordinal))
            .Where(path => Regex.IsMatch(File.ReadAllText(path), "\\\"nami\\.[a-z]", RegexOptions.CultureInvariant))
            .ToArray();
        Assert.HasCount(0, offending, string.Join(Environment.NewLine, offending));
    }

    [DependsOn(nameof(Catalog_MatchesResolvedRegistry))]
    [TestMethod]
    [DataRow("exception.escaped", null, DisplayName = "Dropped exception flag")]
    [DataRow("http.method", "http.request.method", DisplayName = "Renamed HTTP method")]
    [DataRow("http.status_code", "http.response.status_code", DisplayName = "Renamed HTTP status")]
    public void DeprecatedKeys_HaveSuccessorOrDrop(string key, string? successor)
    {
        Assert.AreEqual(Stability.Deprecated, AttributeStability.Of(key));
        Assert.AreEqual(successor, DeprecatedAttributes.SuccessorOf(key));
        Assert.IsFalse(RegistryPolicy.IsPersisted(key));
    }

    [DependsOn(nameof(Catalog_MatchesResolvedRegistry))]
    [TestMethod]
    public void EnumValues_MatchSchemaStability()
    {
        Assert.AreEqual(Stability.Stable, AttributeStability.ValueOf("db.system.name", "postgresql"));
        Assert.AreEqual(Stability.Development, AttributeStability.ValueOf("db.system.name", "redis"));
        Assert.AreNotEqual(Stability.Stable, AttributeStability.ValueOf("http.request.method", "QUERY"));
        Assert.AreEqual(Stability.Stable, AttributeStability.ValueOf("http.request.method", "GET"));
        using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "registry/resolved.json")));
        var catalog = source.RootElement.GetProperty("attribute_catalog");
        var checkedMembers = 0;
        foreach (var index in source.RootElement.GetProperty("registry").GetProperty("attributes").EnumerateArray())
        {
            var attribute = catalog[index.GetInt32()];
            var type = attribute.GetProperty("type");
            if (type.ValueKind != JsonValueKind.Object || !type.TryGetProperty("members", out var members)) continue;
            foreach (var member in members.EnumerateArray())
            {
                var expected =
                    (attribute.TryGetProperty("deprecated", out var ad) && ad.ValueKind != JsonValueKind.Null) ||
                    (member.TryGetProperty("deprecated", out var md) && md.ValueKind != JsonValueKind.Null)
                        ? Stability.Deprecated
                        : Enum.Parse<Stability>(
                            (member.TryGetProperty("stability", out var own)
                                ? own.GetString()!
                                : attribute.GetProperty("stability").GetString()!)
                            .Replace("_", "", StringComparison.Ordinal), true);
                var value = member.TryGetProperty("value", out var v) && v.ValueKind != JsonValueKind.Null
                    ? v.ValueKind == JsonValueKind.String ? v.GetString()! : v.GetRawText()
                    : member.GetProperty("id").GetString()!;
                var key = attribute.GetProperty("key").GetString()!;
                Assert.AreEqual(expected, AttributeStability.ValueOf(key, value), key + "=" + value);
                checkedMembers++;
            }
        }

        Assert.IsGreaterThan(100, checkedMembers);
    }

    [DependsOn(nameof(Catalog_MatchesResolvedRegistry))]
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task PolicyNamingUnpersistedKey_FailsGeneration()
    {
        var temp = Path.Combine(
            context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"),
            "invalid-registry");
        Directory.CreateDirectory(temp);
        File.Copy(Path.Combine(Root, "registry/manifest.yaml"), Path.Combine(temp, "manifest.yaml"));
        var node = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(Root, "registry/resolved.json"),
            context.CancellationToken))!;
        var attr = node["attribute_catalog"]!.AsArray().First(a => a!["key"]!.GetValue<string>() == "nami.project.id")!;
        attr["annotations"]!.AsObject().Remove("nami.persisted");
        await File.WriteAllTextAsync(Path.Combine(temp, "resolved.json"), node.ToJsonString(),
            context.CancellationToken);
        var result = await ProcessRunner.RunAsync(Weaver,
            ["--future", "registry", "check", "--v2", "-r", temp, "-p", Path.Combine(Root, "registry/policies")], Root,
            context.CancellationToken);
        Assert.AreNotEqual(0, result.ExitCode);
        Assert.Contains("nami_persistence_required", result.Output + result.Error);
    }

    [DependsOn(nameof(Catalog_MatchesResolvedRegistry))]
    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task PinChange_WithoutDiffEntry_FailsGate()
    {
        var temp = Path.Combine(
            context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"),
            "pin-change");
        Directory.CreateDirectory(Path.Combine(temp, "registry/source"));
        foreach (var file in new[] { "generate.py", "materialize.py", "CHANGELOG.md" })
            File.Copy(Path.Combine(Root, "registry", file), Path.Combine(temp, "registry", file));
        File.Copy(Path.Combine(Root, "registry/source/manifest.yaml"),
            Path.Combine(temp, "registry/source/manifest.yaml"));
        const string changed = "1111111111111111111111111111111111111111";
        var script = Path.Combine(temp, "registry/generate.py");
        var code = await File.ReadAllTextAsync(script, context.CancellationToken);
        code = code.Replace("CORE_COMMIT = '" + RegistryInfo.CoreCommit + "'", "CORE_COMMIT = '" + changed + "'",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(script, code, context.CancellationToken);
        var changelog = Path.Combine(temp, "registry/CHANGELOG.md");
        await File.WriteAllTextAsync(changelog,
            (await File.ReadAllTextAsync(changelog, context.CancellationToken)).Replace(RegistryInfo.CoreCommit,
                changed, StringComparison.Ordinal), context.CancellationToken);
        var result = await ProcessRunner.RunAsync("python3", [script, "--check"], temp, context.CancellationToken);
        Assert.AreNotEqual(0, result.ExitCode);
        Assert.Contains("pin change", result.Error);
    }

    [DependsOn(nameof(Catalog_MatchesResolvedRegistry))]
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DoNotParallelize]
    public async Task GeneratedCode_AotAnalyzers_ReportNothing()
    {
        var log = Path.Combine(
            context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"),
            "semconv.binlog");
        var result = await ProcessRunner.RunAsync("dotnet",
        [
            "build", "internal/nami.semconv/Nami.SemanticConventions.csproj", "-c", "Release", "--no-restore",
            "-t:Rebuild", "-p:IsAotCompatible=true", "-p:EnableTrimAnalyzer=true", "-p:EnableAotAnalyzer=true",
            "-p:UseSharedCompilation=false", "-nodeReuse:false", "/bl:" + log
        ], Root, context.CancellationToken);
        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        Assert.IsFalse(Regex.IsMatch(result.Output + result.Error, @"\bIL[23][0-9]{3}\b"));
        Assert.IsTrue(File.Exists(log));
    }

    [DependsOn(nameof(Catalog_MatchesResolvedRegistry))]
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    public async Task TokenExemption_ForUnpersistedKey_FailsGeneration()
    {
        var temp = Path.Combine(
            context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"),
            "invalid-exemption");
        Directory.CreateDirectory(temp);
        File.Copy(Path.Combine(Root, "registry/manifest.yaml"), Path.Combine(temp, "manifest.yaml"));
        var node = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(Root, "registry/resolved.json"),
            context.CancellationToken))!;
        var attr = node["attribute_catalog"]!.AsArray().First(a => a!["key"]!.GetValue<string>() == "nami.project.id")!;
        attr["annotations"]!["nami.persisted"] = false;
        attr["annotations"]!["nami.token_exempt"] = true;
        await File.WriteAllTextAsync(Path.Combine(temp, "resolved.json"), node.ToJsonString(),
            context.CancellationToken);
        var result = await ProcessRunner.RunAsync(Weaver,
            ["--future", "registry", "check", "--v2", "-r", temp, "-p", Path.Combine(Root, "registry/policies")], Root,
            context.CancellationToken);
        Assert.AreNotEqual(0, result.ExitCode);
        Assert.Contains("nami_token_exemption_requires_persistence", result.Output + result.Error);
    }

    [DependsOn(nameof(Catalog_MatchesResolvedRegistry))]
    [TestMethod]
    public async Task RustCrate_Constants_EqualCatalogKeys()
    {
        var rust = await File.ReadAllTextAsync(Path.Combine(Root, "crates/nami-semconv/src/lib.rs"),
            context.CancellationToken);
        var start = rust.IndexOf("pub const KEYS:", StringComparison.Ordinal);
        var end = rust.IndexOf("];", start, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start);
        Assert.IsGreaterThan(start, end);
        var keys = Regex.Matches(rust[start..end], "\\\"([^\\\"]+)\\\"").Select(static m => m.Groups[1].Value)
            .Order(StringComparer.Ordinal).ToArray();
        var expected = AttributeCatalog.All.Select(static a => a.Key).Order(StringComparer.Ordinal).ToArray();
        Assert.AreEqual(expected.Length, keys.Length);
        Assert.HasCount(0, expected.Except(keys, StringComparer.Ordinal).ToArray());
        Assert.HasCount(0, keys.Except(expected, StringComparer.Ordinal).ToArray());
    }
}
