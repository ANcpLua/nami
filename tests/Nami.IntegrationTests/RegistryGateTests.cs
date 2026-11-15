using System.Reflection;
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
        Assert.AreEqual(125, counts.GetProperty("attributes").GetProperty("stable").GetInt32());
        Assert.AreEqual(53, counts.GetProperty("metrics").GetProperty("stable").GetInt32());
        Assert.AreEqual(14, counts.GetProperty("spans").GetProperty("stable_refinements").GetInt32());
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
    public void SchemaUrls_NameTheSourcesOfTheCatalog()
    {
        // Each attribute records the schema it came from. The three schema constants must be
        // exactly those sources, so that a move of a pinned registry cannot leave one behind.
        var sources = AttributeCatalog.All.Select(static attribute => attribute.SourceSchema)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string[] constants = [RegistryInfo.CoreSchemaUrl, RegistryInfo.GenAiSchemaUrl, RegistryInfo.NamiSchemaUrl];
        CollectionAssert.AreEqual(constants.Order(StringComparer.Ordinal).ToArray(), sources);
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
    public void DeprecatedDefinitions_AndOnlyThey_CarryObsolete()
    {
        using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "registry/resolved.json")));
        var registry = source.RootElement.GetProperty("registry");
        var catalog = source.RootElement.GetProperty("attribute_catalog");

        // What the registry says: one entry per deprecated definition that has generated constants.
        var expected = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var deprecatedAttributes = 0;
        foreach (var index in registry.GetProperty("attributes").EnumerateArray())
        {
            var attribute = catalog[index.GetInt32()];
            if (ObsoleteMessage(attribute) is not { } message || !HasGeneratedConstants(attribute)) continue;
            var key = attribute.GetProperty("key").GetString()!;
            expected.Add("attribute " + key, message);
            deprecatedAttributes++;
            var type = attribute.GetProperty("type");
            if (type.ValueKind == JsonValueKind.Object &&
                type.GetProperty("members").EnumerateArray().Any(static member => ObsoleteMessage(member) is null))
                expected.Add("values of " + key, message);
        }

        var deprecatedMetrics = 0;
        foreach (var metric in registry.GetProperty("metrics").EnumerateArray())
        {
            if (ObsoleteMessage(metric) is not { } message || !HasGeneratedConstants(metric)) continue;
            expected.Add("metric " + metric.GetProperty("name").GetString(), message);
            deprecatedMetrics++;
        }

        // A span shape is a refinement (by id) or, when no refinement has that id, a base span (by type).
        var shapes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var span in source.RootElement.GetProperty("refinements").GetProperty("spans").EnumerateArray())
            shapes.TryAdd(span.GetProperty("id").GetString()!, span);
        foreach (var span in registry.GetProperty("spans").EnumerateArray())
            shapes.TryAdd(span.GetProperty("type").GetString()!, span);
        var deprecatedSpans = 0;
        foreach (var (id, span) in shapes)
        {
            if (ObsoleteMessage(span) is not { } message || !HasGeneratedConstants(span)) continue;
            expected.Add("span " + id, message);
            deprecatedSpans++;
        }

        var deprecatedEvents = 0;
        foreach (var definition in registry.GetProperty("events").EnumerateArray())
        {
            if (ObsoleteMessage(definition) is not { } message || !HasGeneratedConstants(definition)) continue;
            expected.Add("event " + definition.GetProperty("name").GetString(), message);
            deprecatedEvents++;
        }

        // What the generated assembly says: every type and member that carries [Obsolete].
        const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
                                      BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var actual = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var type in typeof(RegistryInfo).Assembly.GetTypes())
        {
            foreach (var member in type.GetMembers(declared).Where(static member => member is not Type).Prepend(type))
            {
                if (member.GetCustomAttribute<ObsoleteAttribute>(false) is not { } obsolete) continue;
                Assert.IsFalse(obsolete.IsError, member + " must warn, not fail the build of old readers");
                actual.Add(Describe(member), obsolete.Message ?? "");
            }
        }

        Assert.IsGreaterThan(100, deprecatedAttributes);
        Assert.IsGreaterThan(50, deprecatedMetrics);
        Assert.IsGreaterThan(0, deprecatedSpans);
        Assert.IsGreaterThan(0, deprecatedEvents);
        var missing = expected.Keys.Except(actual.Keys, StringComparer.Ordinal).ToArray();
        var unexpected = actual.Keys.Except(expected.Keys, StringComparer.Ordinal).ToArray();
        Assert.IsEmpty(missing,
            missing.Length + " deprecated definitions lack [Obsolete], first: " + string.Join(", ", missing.Take(5)));
        Assert.IsEmpty(unexpected,
            unexpected.Length + " definitions carry [Obsolete] without being deprecated, first: " +
            string.Join(", ", unexpected.Take(5)));
        foreach (var (definition, message) in expected)
            Assert.AreEqual(message, actual[definition], definition);
        Assert.AreEqual("Replaced by android.app.state.", actual["attribute android.state"]);
        Assert.AreEqual("Removed, no replacement at this time.", actual["attribute db.user"]);

        static string? ObsoleteMessage(JsonElement definition)
        {
            if (!definition.TryGetProperty("deprecated", out var deprecated) ||
                deprecated.ValueKind != JsonValueKind.Object) return null;
            if (deprecated.TryGetProperty("renamed_to", out var successor) &&
                successor.ValueKind == JsonValueKind.String)
                return "Replaced by " + successor.GetString() + ".";
            var note = deprecated.TryGetProperty("note", out var text) && text.ValueKind == JsonValueKind.String
                ? Whitespace().Replace(text.GetString()!, " ").Trim()
                : "";
            return note.Length > 0 ? note : "Deprecated.";
        }

        static bool HasGeneratedConstants(JsonElement definition)
        {
            return !(definition.TryGetProperty("annotations", out var annotations) &&
                     annotations.TryGetProperty("code_generation", out var generation) &&
                     generation.TryGetProperty("exclude", out var exclude) &&
                     exclude.ValueKind == JsonValueKind.True);
        }

        static string Describe(MemberInfo member)
        {
            var owner = member.DeclaringType;
            switch (member)
            {
                case FieldInfo { IsLiteral: true } field when IsAttributeClass(owner):
                    return "attribute " + (string)field.GetRawConstantValue()!;
                case Type values when IsAttributeClass(owner) && values.Name.EndsWith("Values", StringComparison.Ordinal) &&
                                      owner!.GetField(values.Name[..^"Values".Length]) is { IsLiteral: true } key:
                    return "values of " + (string)key.GetRawConstantValue()!;
                case Type metric when owner == typeof(Metrics) && metric.GetField("Name") is { IsLiteral: true } name:
                    return "metric " + (string)name.GetRawConstantValue()!;
                case Type span when owner == typeof(Spans) && span.GetField("Id") is { IsLiteral: true } id:
                    return "span " + (string)id.GetRawConstantValue()!;
                case Type signal when owner == typeof(Events) && signal.GetField("Name") is { IsLiteral: true } name:
                    return "event " + (string)name.GetRawConstantValue()!;
                default:
                    return "unexpected " + (owner is null ? "" : owner.FullName + ".") + member.Name;
            }
        }

        static bool IsAttributeClass(Type? type)
        {
            return type is { IsNested: false, Namespace: "Nami.SemanticConventions" } &&
                   type.Name.EndsWith("Attributes", StringComparison.Ordinal);
        }
    }

    [TestMethod]
    [Timeout(240000, CooperativeCancellation = true)]
    [DoNotParallelize]
    public async Task Build_UseOfDeprecatedRegistryConstant_FailsUnderProductionPolicy()
    {
        await using var current = new FixtureProject("CurrentKeyConsumer", context: context);
        UseRegistryConstants(current, "AndroidAttributes.AppState", "Metrics.SystemCpuTime.Name");
        var accepted = await current.BuildAsync(context.CancellationToken);
        Assert.AreEqual(0, accepted.ExitCode, accepted.Output + accepted.Error);
        Assert.AreEqual("false", accepted.GetRecordedProperty("TreatWarningsAsErrors"));

        await using var deprecated = new FixtureProject("DeprecatedKeyConsumer", context: context);
        UseRegistryConstants(deprecated, "AndroidAttributes.State", "Metrics.CpuTime.Name");
        var rejected = await deprecated.BuildAsync(context.CancellationToken);
        var output = rejected.Output + rejected.Error;
        Assert.AreNotEqual(0, rejected.ExitCode,
            "A hand-written use of a deprecated registry constant must fail the build:\n" + output);
        var errors = CompilerError().Matches(output).Select(static match => match.Groups[1].Value).Distinct().ToArray();
        Assert.AreSequenceEqual(["CS0618"], errors, output);
        Assert.Contains("'AndroidAttributes.State' is obsolete: 'Replaced by android.app.state.'", output);
        Assert.Contains("'Metrics.CpuTime' is obsolete: 'Replaced by system.cpu.time.'", output);
    }

    private static void UseRegistryConstants(FixtureProject project, string attributeKey, string metricName)
    {
        UnionCompilerTests.UseProductionWarningPolicy(project);
        project.AddAssemblyReference(typeof(RegistryInfo).Assembly.Location);
        project.AddSource("Keys.cs", $$"""
            using Nami.SemanticConventions;

            namespace RegistryConsumer;

            public static class Keys
            {
                public static string Attribute => {{attributeKey}};
                public static string Metric => {{metricName}};
            }
            """);
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

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task CoreMigrationExceptions_RequireExactPinnedSource()
    {
        const string script = """
            import sys
            from pathlib import Path
            sys.dont_write_bytecode = True
            sys.path.insert(0, str(Path(sys.argv[1]) / 'registry'))
            import generate as gate
            workspace = Path(sys.argv[1]) / 'artifacts/core-warning-probe'
            assert len(gate.CORE_GROUPS_WITHOUT_REQUIREMENT_LEVEL) == 16
            for group, relative in gate.CORE_GROUPS_WITHOUT_REQUIREMENT_LEVEL.items():
                exact = {'group_id': group, 'path_or_url': str(workspace / 'core/model' / relative)}
                assert gate.migration_diagnostic('MissingRequirementLevelWarning', exact, workspace), group
                other_file = {**exact, 'path_or_url': str(workspace / 'nami' / relative)}
                assert not gate.migration_diagnostic('MissingRequirementLevelWarning', other_file, workspace), group
            stability = {'attribute_id': 'faas.time', 'group_id': 'registry.faas.deprecated', 'error': 'Missing stability field.',
                         'path_or_url': str(workspace / 'core/model/faas/deprecated/registry-deprecated.yaml')}
            assert gate.migration_diagnostic('InvalidAttributeWarning', stability, workspace)
            assert not gate.migration_diagnostic('InvalidAttributeWarning', {**stability, 'attribute_id': 'faas.trigger'}, workspace)
            gate.CORE_COMMIT = '0' * 40
            assert not gate.migration_diagnostic('MissingRequirementLevelWarning', exact, workspace), 'Other pins must fail'
            assert not gate.migration_diagnostic('InvalidAttributeWarning', stability, workspace), 'Other pins must fail'
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

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\berror ([A-Z]+\d+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex CompilerError();
}
