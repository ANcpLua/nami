using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Nami.SemanticConventions;
using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
public sealed partial class RegistryGateTests(TestContext context)
{
    private static readonly string[] s_generatedDirectories =
        ["internal/nami.semconv/Generated", "docs/semconv"];

    private static readonly string[] s_generationCheckArgs = ["registry/generate.sh", "--check"];
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
        await using var stream =
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
        Assert.AreEqual(RegistryInfo.NamiSchemaUrl, resolved.RootElement.GetProperty("schema_url").GetString());
        var catalog = resolved.RootElement.GetProperty("attribute_catalog");
        var keys = resolved.RootElement.GetProperty("registry").GetProperty("attributes").EnumerateArray()
            .Select(i => catalog[i.GetInt32()].GetProperty("key").GetString()!).Order(StringComparer.Ordinal).ToArray();
        var generatedKeys = AttributeCatalog.All.Select(static a => a.Key).Order(StringComparer.Ordinal).ToArray();
        Assert.HasCount(keys.Length, generatedKeys);
        Assert.IsEmpty(keys.Except(generatedKeys, StringComparer.Ordinal).ToArray());
        Assert.IsEmpty(generatedKeys.Except(keys, StringComparer.Ordinal).ToArray());
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
            var run = await ProcessRunner.RunAsync("bash", s_generationCheckArgs, Root, context.CancellationToken);
            Assert.AreEqual(0, run.ExitCode, run.Output + run.Error);
        }

        Assert.AreSequenceEqual(before, Snapshot());
    }

    private static string[] Snapshot()
    {
        return [.. s_generatedDirectories
            .SelectMany(dir => Directory.GetFiles(Path.Combine(Root, dir), "*", SearchOption.AllDirectories))
            .Order(StringComparer.Ordinal)
            .Select(path =>
                Path.GetRelativePath(Root, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))];
    }

    [DependsOn(nameof(Catalog_MatchesResolvedRegistry))]
    [TestMethod]
    public void NamiKeys_AppearOnlyInGeneratedFiles()
    {
        var offending = Directory.GetFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Contains("/obj/", StringComparison.Ordinal) &&
                                  !path.Contains("/bin/", StringComparison.Ordinal) &&
                                  !path.EndsWith(".g.cs", StringComparison.Ordinal))
            .Where(path => NamiKeyLiteral().IsMatch(File.ReadAllText(path)))
            .ToArray();
        Assert.IsEmpty(offending, string.Join(Environment.NewLine, offending));
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

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task CurrentPins_WithoutHistoricalRegistry_ReachSourceResolution()
    {
        var temp = Path.Combine(
            context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"),
            "current-registry-only");
        const string script = """
            from pathlib import Path
            from types import SimpleNamespace
            import os, sys
            sys.dont_write_bytecode = True
            source, scratch = map(Path, sys.argv[1:3])
            sys.path.insert(0, str(source / 'registry'))
            import generate as gate
            (scratch / 'registry/source').mkdir(parents=True)
            (scratch / 'registry/source/manifest.yaml').write_text(
                (source / 'registry/source/manifest.yaml').read_text())
            current = (source / 'registry/CHANGELOG.md').read_text().split('\n## ', 1)[1].split('\n## ', 1)[0]
            (scratch / 'registry/CHANGELOG.md').write_text('# Registry changes\n\n## ' + current)
            gate.ROOT = scratch
            os.environ['WEAVER'] = 'weaver-version-probe'
            gate.run = lambda *args, **kwargs: SimpleNamespace(stdout='weaver ' + gate.WEAVER_VERSION)
            class SourceResolutionReached(Exception):
                pass
            def reached(*args, **kwargs):
                raise SourceResolutionReached()
            gate.pinned_model = reached
            sys.argv = ['generate.py', '--check']
            try:
                gate.main()
            except SourceResolutionReached:
                assert not (scratch / 'registry/changes').exists()
                print('Current pins accepted without a historical registry.')
            else:
                raise AssertionError('Expected source resolution')
            """;
        var result = await ProcessRunner.RunAsync("python3", ["-c", script, Root, temp], Root,
            context.CancellationToken);
        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
        Assert.AreEqual("Current pins accepted without a historical registry.", result.Output.Trim());
    }

    [TestMethod]
    [DataRow("core", "current registry CHANGELOG")]
    [DataRow("genai", "current registry CHANGELOG")]
    [DataRow("weaver", "current registry CHANGELOG")]
    [DataRow("dependency", "Registry dependencies")]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task CurrentPins_MismatchedMetadata_FailsBeforeSourceResolution(string field, string expectedError)
    {
        var temp = Path.Combine(
            context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"),
            "mismatched-current-pin");
        const string script = """
            from pathlib import Path
            from types import SimpleNamespace
            import os, sys
            sys.dont_write_bytecode = True
            source, scratch = map(Path, sys.argv[1:3])
            field = sys.argv[3]
            sys.path.insert(0, str(source / 'registry'))
            import generate as gate
            (scratch / 'registry/source').mkdir(parents=True)
            manifest = (source / 'registry/source/manifest.yaml').read_text()
            current = (source / 'registry/CHANGELOG.md').read_text().split('\n## ', 1)[1].split('\n## ', 1)[0]
            if field == 'dependency':
                manifest = manifest.replace(gate.GENAI_COMMIT, '0' * 40)
            else:
                value = {'core': gate.CORE_COMMIT, 'genai': gate.GENAI_COMMIT, 'weaver': gate.WEAVER_VERSION}[field]
                current = current.replace(value, 'missing-' + field)
            (scratch / 'registry/source/manifest.yaml').write_text(manifest)
            (scratch / 'registry/CHANGELOG.md').write_text('# Registry changes\n\n## ' + current)
            gate.ROOT = scratch
            os.environ['WEAVER'] = 'weaver-version-probe'
            gate.run = lambda *args, **kwargs: SimpleNamespace(stdout='weaver ' + gate.WEAVER_VERSION)
            def unexpected_source_resolution(*args, **kwargs):
                raise RuntimeError('Unexpected upstream source resolution')
            gate.pinned_model = unexpected_source_resolution
            sys.argv = ['generate.py', '--check']
            try:
                gate.main()
            except RuntimeError as error:
                print(error, file=sys.stderr)
                sys.exit(1)
            """;
        var result = await ProcessRunner.RunAsync("python3", ["-c", script, Root, temp, field], Root,
            context.CancellationToken);
        Assert.AreEqual(1, result.ExitCode, result.Output + result.Error);
        Assert.Contains(expectedError, result.Error);
        Assert.DoesNotContain("Unexpected upstream", result.Error);
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task A2aMigrationExceptions_RequireExactPinnedSource()
    {
        const string script = """
            import sys
            from pathlib import Path
            sys.dont_write_bytecode = True
            sys.path.insert(0, str(Path(sys.argv[1]) / 'registry'))
            import generate as gate
            workspace = Path(sys.argv[1]) / 'artifacts/a2a-warning-probe'
            for group in ('span.a2a.client', 'span.a2a.server'):
                exact = {'group_id': group, 'path_or_url': str(workspace / 'genai/model/a2a/spans.yaml')}
                assert gate.migration_diagnostic('MissingRequirementLevelWarning', exact, workspace), group
                other_file = {**exact, 'path_or_url': str(workspace / 'nami/a2a/spans.yaml')}
                assert not gate.migration_diagnostic('MissingRequirementLevelWarning', other_file, workspace), group
                other_group = {**exact, 'group_id': 'span.a2a.unrecognized'}
                assert not gate.migration_diagnostic('MissingRequirementLevelWarning', other_group, workspace), group
            gate.GENAI_COMMIT = '0' * 40
            assert not gate.migration_diagnostic('MissingRequirementLevelWarning', exact, workspace), 'Other pins must fail'
            """;
        var result = await ProcessRunner.RunAsync("python3", ["-c", script, Root], Root,
            context.CancellationToken);
        Assert.AreEqual(0, result.ExitCode, result.Output + result.Error);
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
        Assert.IsFalse(TrimmingDiagnostic().IsMatch(result.Output + result.Error));
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

    [GeneratedRegex("\\\"nami\\.[a-z]", RegexOptions.CultureInvariant)]
    private static partial Regex NamiKeyLiteral();

    [GeneratedRegex(@"\bIL[23][0-9]{3}\b")]
    private static partial Regex TrimmingDiagnostic();
}
