using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Fallout.Common;
using Fallout.Common.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serilog;

namespace Nami.Build;

/// <summary>
/// The checks that catch product defects the compiler and the tests do not: the collector's routes,
/// key roles and page limits against the OpenAPI contract, the generated semantic catalog against
/// the pinned conventions, project-scoped storage reads, span and log storage identity, and
/// redacted, bounded instrumentation telemetry.
/// </summary>
interface IVerify : ICollectorSemanticCatalog, IContracts
{
    // The product list routes whose `limit` is the contract's shared PageLimit.
    private static readonly string[] s_sharedPageLimitRoutes =
    [
        "/api/v1/sessions",
        "/api/v1/sessions/{session_id}/traces",
        "/api/v1/traces",
        "/api/v1/traces/{trace_id}/spans",
        "/api/v1/logs",
        "/api/v1/metrics",
        "/api/v1/metrics/{metric_name}/series",
        "/api/v1/annotations",
        "/api/v1/saved-queries"
    ];

    Target VerifyCollectorEndpointLimitsMatchOpenApi => d => d
        .ProceedAfterFailure()
        .Unlisted()
        .Description("Verify collector endpoint pagination limits match the OpenAPI contract")
        .Executes(() =>
        {
            using var document = ReadOpenApi();

            // The contract has one PageLimit: every list route's `limit` and SavedQueryRunRequest.limit
            // reference the same schema (1 to 1000) with the same default (100). The collector keeps that
            // pair once, so every route is held to the first one; only the metric query's series_limit and
            // step_ms carry their own ranges.
            var shared = ReadOpenApiQueryBounds(document, s_sharedPageLimitRoutes[0], "limit");
            foreach (var route in s_sharedPageLimitRoutes.Skip(1))
            {
                var bounds = ReadOpenApiQueryBounds(document, route, "limit");
                if (bounds != shared)
                    throw new InvalidOperationException(
                        $"{route} limit bounds {bounds} differ from {s_sharedPageLimitRoutes[0]}'s {shared}; the collector keeps one PageLimit.");
            }

            var runLimit = ResolveOpenApiSchema(document, document.RootElement.GetProperty("components").GetProperty("schemas")
                .GetProperty("SavedQueries.SavedQueryRunRequest").GetProperty("properties").GetProperty("limit"));
            if (runLimit != shared)
                throw new InvalidOperationException(
                    $"SavedQueryRunRequest.limit bounds {runLimit} must be the shared PageLimit {shared}.");

            Log.Information("The shared page contract is consistent; runtime bounds are generated from TypeSpec");
        });

    Target VerifyCollectorRoutesMatchOpenApi => d => d
        .ProceedAfterFailure()
        .Unlisted()
        .Description("Verify every contract route is mapped by the collector with the key role the contract names")
        .Executes(() =>
        {
            var endpointFile = CollectorDirectory / "Hosting" / "CollectorEndpointExtensions.cs";
            using var openApiDocument = ReadOpenApi();

            var operationNames = new HashSet<string>(StringComparer.Ordinal)
            {
                "delete", "get", "patch", "post", "put"
            };
            // The key role each operation's security scheme names (common/auth.tsp): the query
            // routes keep ApiKeyAuth and require `read`.
            var expectedRoles = openApiDocument.RootElement.GetProperty("paths")
                .EnumerateObject()
                .Where(static path => path.Name.StartsWith("/api/v1/", StringComparison.Ordinal))
                .SelectMany(path => path.Value.EnumerateObject()
                    .Where(operation => operationNames.Contains(operation.Name))
                    .Select(operation => (
                        Route: $"{operation.Name.ToUpperInvariant()} {path.Name}",
                        Role: SecuritySchemeRole(operation.Value))))
                .ToDictionary(static item => item.Route, static item => item.Role, StringComparer.Ordinal);
            var expected = expectedRoles.Keys.ToHashSet(StringComparer.Ordinal);

            var mapped = ParseCompilationUnit(endpointFile).DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Select(static invocation => (Invocation: invocation, Member: invocation.Expression as MemberAccessExpressionSyntax))
                .Where(static item => item.Member?.Expression is IdentifierNameSyntax { Identifier.ValueText: "api" })
                .Select(static item =>
                {
                    var method = item.Member!.Name.Identifier.ValueText switch
                    {
                        "MapDelete" => "DELETE",
                        "MapGet" => "GET",
                        "MapPatch" => "PATCH",
                        "MapPost" => "POST",
                        "MapPut" => "PUT",
                        _ => null
                    };
                    var route = item.Invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression
                        as LiteralExpressionSyntax;
                    return method is null || route is null
                        ? null
                        : ((string Route, string? Role)?)($"{method} /api/v1{route.Token.ValueText}",
                            DeclaredKeyRole(item.Invocation));
                })
                .Where(static route => route is not null)
                .Select(static route => route!.Value)
                .ToList();
            var actual = mapped.Select(static route => route.Route).ToHashSet(StringComparer.Ordinal);

            var missing = expected.Except(actual, StringComparer.Ordinal).OrderBy(static route => route).ToArray();
            var extra = actual.Except(expected, StringComparer.Ordinal).OrderBy(static route => route).ToArray();
            var wrongRoles = mapped
                .Where(route => expectedRoles.TryGetValue(route.Route, out var role) &&
                                !string.Equals(role, route.Role, StringComparison.Ordinal))
                .Select(route => $"{route.Route}: collector requires {route.Role ?? "no role"}, " +
                                 $"OpenAPI security names {expectedRoles[route.Route]}")
                .OrderBy(static route => route)
                .ToArray();
            if (missing.Length is 0 && extra.Length is 0 && wrongRoles.Length is 0)
            {
                Log.Information("Collector product routes and their key roles exactly match the OpenAPI contract");
                return;
            }

            foreach (var route in missing)
                Log.Error("  Missing collector product route: {Route}", route);
            foreach (var route in extra)
                Log.Error("  Collector route absent from the contract: {Route}", route);
            foreach (var route in wrongRoles)
                Log.Error("  Key role mismatch: {Route}", route);

            throw new InvalidOperationException(
                "Collector /api/v1 routes, and the key role each requires, must exactly match the OpenAPI contract.");

            static string SecuritySchemeRole(JsonElement operation)
            {
                var scheme = operation.TryGetProperty("security", out var security) && security.GetArrayLength() is 1
                    ? security[0].EnumerateObject().Single().Name
                    : null;
                return scheme switch
                {
                    "ApiKeyAuth" => "Read",
                    "NamiWriteKeyAuth" => "Write",
                    "NamiDeleteKeyAuth" => "Delete",
                    _ => throw new InvalidOperationException(
                        $"An /api/v1 operation names no collector key scheme it can map to a role: '{scheme}'.")
                };
            }

            // `api.MapX(...).RequireKeyRole(ApiKeyRoles.Y)` declares role Y for that route.
            static string? DeclaredKeyRole(InvocationExpressionSyntax map) =>
                map.Parent is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "RequireKeyRole", Parent: InvocationExpressionSyntax requirement } &&
                requirement.ArgumentList.Arguments.SingleOrDefault()?.Expression is MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.ValueText: "ApiKeyRoles" }
                } role
                    ? role.Name.Identifier.ValueText
                    : null;
        });

    Target VerifyInstrumentationTelemetryIsBoundedAndRedacted => d => d
        .ProceedAfterFailure()
        .Unlisted()
        .Description("Verify instrumentation telemetry avoids raw exception content and unbounded span names")
        .Executes(() =>
        {
            string[] forbiddenTokens =
            [
                "ExceptionAttributes.Message",
                "ExceptionAttributes.Stacktrace",
                "exception.Message",
                "exception.ToString()",
                "ex.Message",
                "{Message}",
                "SetStatus(ActivityStatusCode.Error, error)",
                "$\"{GenAiAttributes.OperationNameValues.ExecuteTool} {toolName}\"",
                "StartActivity(operation,",
                "TryParseCollectionName",
                "DbAttributes.CollectionName",
                "$\"{operationName} {collectionName}\"",
                "GenAiAttributes.ToolCallId",
                "\"exception.source\""
            ];

            var offenders = (RootDirectory / "internal" / "nami.instrumentation").GlobFiles("**/*.cs")
                .Where(static file =>
                {
                    var path = file.ToString();
                    return !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
                })
                .Select(file => (
                    File: RootDirectory.GetRelativePathTo(file).ToString(),
                    Text: File.ReadAllText(file)))
                .SelectMany(file => forbiddenTokens
                    .Where(token => file.Text.Contains(token, StringComparison.Ordinal))
                    .Select(token => (file.File, Token: token)))
                .ToList();

            if (offenders.Count is 0)
            {
                Log.Information("Instrumentation telemetry is bounded and redacted by default");
                return;
            }

            foreach (var offender in offenders)
                Log.Error("  Unsafe instrumentation telemetry token '{Token}' found in {File}",
                    offender.Token,
                    offender.File);

            throw new InvalidOperationException(
                "Do not record exception messages, stack traces, raw tool names in span names, or caller-provided " +
                "operation names as span names or attributes by default. Normalize operation names and keep sensitive content gated.");
        });

    Target VerifyCollectorStorageReadsAreProjectScoped => d => d
        .ProceedAfterFailure()
        .Unlisted()
        .Description("Verify collector storage reads stay scoped by project_id")
        .Executes(() =>
        {
            var projectIdentityFile = CollectorIngestionDirectory / "Primitives" / "ProjectIdentity.cs";
            var storeFile = CollectorStorageDirectory / "DuckDbStore.cs";
            var sessionsFile = CollectorStorageDirectory / "DuckDbStore.Sessions.cs";

            var projectIdentityText = projectIdentityFile.FileExists() ? File.ReadAllText(projectIdentityFile) : "";
            var storeText = storeFile.FileExists() ? File.ReadAllText(storeFile) : "";
            var sessionsText = sessionsFile.FileExists() ? File.ReadAllText(sessionsFile) : "";

            string[] requiredProjectIdentityTokens =
            [
                "public const string DefaultProjectId = \"default\";",
                "public static string Normalize(string? projectId)"
            ];

            string[] requiredStoreTokens =
            [
                "FROM spans WHERE project_id = $1 AND trace_id IN (",
                "SELECT DISTINCT trace_id FROM spans WHERE project_id = $1",
                "AND (session_id = $2 OR (session_id IS NULL AND trace_id = $2)))",
                "FROM spans WHERE project_id = $1 AND trace_id = $2",
                "qb.Add(\"project_id = $N\", projectId);",
                "(SELECT COUNT(*) FROM spans WHERE project_id = $1)",
                "SELECT COUNT(*) FROM spans WHERE project_id = $1"
            ];

            string[] requiredSessionTokens =
            [
                "WHERE project_id = $1",
                "WHERE project_id = $1 AND (session_id = $2 OR (session_id IS NULL AND trace_id = $2))"
            ];

            var missing = new List<string>();
            var forbidden = new List<string>();

            foreach (var token in requiredProjectIdentityTokens)
                if (!projectIdentityText.Contains(token, StringComparison.Ordinal))
                    missing.Add($"{RootDirectory.GetRelativePathTo(projectIdentityFile)} missing token: {token}");

            foreach (var token in requiredStoreTokens)
                if (!storeText.Contains(token, StringComparison.Ordinal))
                    missing.Add($"{RootDirectory.GetRelativePathTo(storeFile)} missing token: {token}");

            foreach (var token in requiredSessionTokens)
                if (!sessionsText.Contains(token, StringComparison.Ordinal))
                    missing.Add($"{RootDirectory.GetRelativePathTo(sessionsFile)} missing token: {token}");

            string[] forbiddenReadScopeTokens =
            [
                "projectId = ProjectScope.DefaultProjectId",
                "projectId = ProjectIdentity.DefaultProjectId",
                "ProjectScope.Normalize(projectId)",
                "ProjectIdentity.Normalize(projectId)"
            ];

            foreach (var token in forbiddenReadScopeTokens)
            {
                if (storeText.Contains(token, StringComparison.Ordinal))
                    forbidden.Add($"{RootDirectory.GetRelativePathTo(storeFile)} contains forbidden token: {token}");
                if (sessionsText.Contains(token, StringComparison.Ordinal))
                    forbidden.Add($"{RootDirectory.GetRelativePathTo(sessionsFile)} contains forbidden token: {token}");
            }

            if (missing.Count is 0 && forbidden.Count is 0)
            {
                Log.Information("Collector storage reads require explicit project scope");
                return;
            }

            foreach (var item in missing)
                Log.Error("  {Missing}", item);
            foreach (var item in forbidden)
                Log.Error("  {Forbidden}", item);

            throw new InvalidOperationException(
                "Storage reads over spans, logs, and session summaries must include explicit project_id scope. " +
                "Do not default or normalize project scope inside read query APIs; pass the request edge scope in explicitly.");
        });

    Target VerifyCollectorSpanIdentityIsComposite => d => d
        .ProceedAfterFailure()
        .Unlisted()
        .Description("Verify span storage identity is project- and trace-scoped")
        .Executes(() =>
        {
            var spanStorageRowFile = CollectorStorageDirectory / "StorageRecords.cs";
            var storeFile = CollectorStorageDirectory / "DuckDbStore.cs";

            var missingRequired = new List<string>();
            var spanIdentity = ReadSpanStorageIdentity(spanStorageRowFile);
            var expectedPrimaryKeyOrdinals = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["ProjectId"] = 0,
                ["TraceId"] = 1,
                ["SpanId"] = 2
            };

            foreach (var expected in expectedPrimaryKeyOrdinals)
            {
                if (!spanIdentity.PrimaryKeyOrdinals.TryGetValue(expected.Key, out var ordinal) || ordinal != expected.Value)
                {
                    missingRequired.Add(RootDirectory.GetRelativePathTo(spanStorageRowFile).ToString()
                                        + $" must declare {expected.Key} with PrimaryKeyOrdinal = {expected.Value}");
                }
            }

            foreach (var unexpected in spanIdentity.PrimaryKeyOrdinals.Keys.Except(expectedPrimaryKeyOrdinals.Keys, StringComparer.Ordinal))
            {
                missingRequired.Add(RootDirectory.GetRelativePathTo(spanStorageRowFile).ToString()
                                    + $" declares unexpected SpanStorageRow primary key column: {unexpected}");
            }

            if (!NormalizeSqlFragment(spanIdentity.OnConflict).StartsWith(
                    "on conflict (project_id, trace_id, span_id)",
                    StringComparison.Ordinal))
            {
                missingRequired.Add(RootDirectory.GetRelativePathTo(spanStorageRowFile).ToString()
                                    + " must upsert ON CONFLICT (project_id, trace_id, span_id)");
            }

            if (!StoreInitializesGeneratedAuthoritativeDdl(storeFile))
            {
                missingRequired.Add(RootDirectory.GetRelativePathTo(storeFile).ToString()
                                    + " must initialize authoritative tables through "
                                    + "DuckDbGeneratedSchema.AuthoritativeDdl");
            }

            var unsafeSql = UnsafeSpanIdentitySqlFragments().ToList();

            if (unsafeSql.Count is 0 && missingRequired.Count is 0)
            {
                Log.Information("Collector span storage identity is project- and trace-scoped");
                return;
            }

            foreach (var offender in unsafeSql)
                Log.Error("  Unsafe span identity SQL at {File}:{Line}: {Text}",
                    offender.File,
                    offender.Line,
                    offender.Text);

            foreach (var missing in missingRequired)
                Log.Error("  Missing span identity invariant: {Invariant}", missing);

            throw new InvalidOperationException(
                "Span ids are unique only within traces and tenants. Store and upsert spans by " +
                "(project_id, trace_id, span_id), never span_id alone.");
        });

    Target VerifyCollectorStorageWritesAreReplayIdempotent => d => d
        .ProceedAfterFailure()
        .Unlisted()
        .Description("Verify collector log storage writes are replay-idempotent")
        .Executes(() =>
        {
            var storageRowsFile = CollectorStorageDirectory / "StorageRecords.cs";
            var storageMapperFile = CollectorStorageDirectory / "IngestionStorageMapper.cs";
            var persistedPolicyFile = CollectorStorageDirectory / "PersistedAttributePolicy.cs";
            var converterFile = CollectorIngestionDirectory / "OtlpConverter.cs";
            var storedRecordMapperFile = CollectorStorageDirectory / "StoredRecordMapper.cs";

            foreach (var file in new[] { storageRowsFile, storageMapperFile, persistedPolicyFile, converterFile, storedRecordMapperFile })
            {
                if (!file.FileExists())
                    throw new FileNotFoundException("Missing replay-idempotency verification input", file.ToString());
            }

            var missingRequired = new List<string>();
            EnsureStorageIdentity(
                "LogStorageRow",
                new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["ProjectId"] = 0,
                    ["LogId"] = 1
                },
                "on conflict (project_id, log_id)");
            var storageMapperText = File.ReadAllText(storageMapperFile);
            var persistedPolicyText = File.ReadAllText(persistedPolicyFile);
            var storedRecordMapperText = File.ReadAllText(storedRecordMapperFile);

            var forbiddenRandomIdTokens = new List<(AbsolutePath File, string Token)>
            {
                (storageMapperFile, "Guid.NewGuid"),
                (converterFile, "Guid.NewGuid")
            }
            .Where(item => File.ReadAllText(item.File).Contains(item.Token, StringComparison.Ordinal))
            .ToList();

            string[] storageMapperRequired = ["StableStorageId(\"log\""];

            foreach (var token in storageMapperRequired)
            {
                if (!storageMapperText.Contains(token, StringComparison.Ordinal))
                    missingRequired.Add(RootDirectory.GetRelativePathTo(storageMapperFile) + $" must contain deterministic storage identity token '{token}'");
            }

            if (!persistedPolicyText.Contains("OrderBy(static item => item.Key, StringComparer.Ordinal)", StringComparison.Ordinal))
            {
                missingRequired.Add(
                    RootDirectory.GetRelativePathTo(persistedPolicyFile)
                    + " must sort persisted top-level OTLP attributes by key before writing JSON");
            }

            if (!storedRecordMapperText.Contains("OrderBy(static item => item.Key, StringComparer.Ordinal)", StringComparison.Ordinal))
            {
                missingRequired.Add(
                    RootDirectory.GetRelativePathTo(storedRecordMapperFile)
                    + " must sort nested kvlist attributes by key before storage writes JSON");
            }

            if (missingRequired.Count is 0 && forbiddenRandomIdTokens.Count is 0)
            {
                Log.Information("Collector log storage writes are replay-idempotent");
                return;
            }

            foreach (var missing in missingRequired)
                Log.Error("  Missing replay-idempotency invariant: {Invariant}", missing);

            foreach (var offender in forbiddenRandomIdTokens)
                Log.Error("  Random storage identity token '{Token}' found in {File}",
                    offender.Token,
                    RootDirectory.GetRelativePathTo(offender.File));

            throw new InvalidOperationException(
                "Collector log storage writes must use project-scoped primary keys, ON CONFLICT upserts, " +
                "deterministic ids, and canonical persisted JSON.");

            void EnsureStorageIdentity(
                string recordName,
                IReadOnlyDictionary<string, int> expectedPrimaryKeyOrdinals,
                string expectedOnConflictPrefix)
            {
                var identity = ReadStorageRowIdentity(storageRowsFile, recordName);
                foreach (var expected in expectedPrimaryKeyOrdinals)
                {
                    if (!identity.PrimaryKeyOrdinals.TryGetValue(expected.Key, out var ordinal) || ordinal != expected.Value)
                    {
                        missingRequired.Add(RootDirectory.GetRelativePathTo(storageRowsFile)
                                            + $" must declare {recordName}.{expected.Key} with PrimaryKeyOrdinal = {expected.Value}");
                    }
                }

                foreach (var unexpected in identity.PrimaryKeyOrdinals.Keys.Except(expectedPrimaryKeyOrdinals.Keys, StringComparer.Ordinal))
                {
                    missingRequired.Add(RootDirectory.GetRelativePathTo(storageRowsFile)
                                        + $" declares unexpected {recordName} primary key column: {unexpected}");
                }

                if (!NormalizeSqlFragment(identity.OnConflict).StartsWith(expectedOnConflictPrefix, StringComparison.Ordinal))
                {
                    missingRequired.Add(RootDirectory.GetRelativePathTo(storageRowsFile)
                                        + $" must upsert {recordName} with {expectedOnConflictPrefix}");
                }
            }
        });

    Target Verify => d => d
        .Description("Verify the collector against the OpenAPI contract, the generated semantic catalog and its storage invariants")
        .DependsOn(VerifyCollectorEndpointLimitsMatchOpenApi)
        .DependsOn(VerifyCollectorRoutesMatchOpenApi)
        .DependsOn<ICollectorSemanticCatalog>(static x => x.VerifyCollectorSemanticAttributeCatalog)
        .DependsOn(VerifyCollectorStorageReadsAreProjectScoped)
        .DependsOn(VerifyCollectorSpanIdentityIsComposite)
        .DependsOn(VerifyCollectorStorageWritesAreReplayIdempotent)
        .DependsOn(VerifyInstrumentationTelemetryIsBoundedAndRedacted);

    private readonly record struct SpanStorageIdentity(
        IReadOnlyDictionary<string, int> PrimaryKeyOrdinals,
        string OnConflict);

    private readonly record struct StorageRowIdentity(
        IReadOnlyDictionary<string, int> PrimaryKeyOrdinals,
        string OnConflict);

    private readonly record struct UnsafeSqlOffender(
        string File,
        int Line,
        string Text);

    private static SpanStorageIdentity ReadSpanStorageIdentity(AbsolutePath spanStorageRowFile)
    {
        if (!spanStorageRowFile.FileExists())
            throw new FileNotFoundException("Missing SpanStorageRow source", spanStorageRowFile.ToString());

        var root = ParseCompilationUnit(spanStorageRowFile);
        var spanRecord = root.DescendantNodes()
            .OfType<RecordDeclarationSyntax>()
            .SingleOrDefault(static record => record.Identifier.ValueText is "SpanStorageRow");

        if (spanRecord is null)
            throw new InvalidOperationException("Missing SpanStorageRow declaration.");

        var primaryKeyOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var property in spanRecord.Members.OfType<PropertyDeclarationSyntax>())
        {
            foreach (var attribute in property.AttributeLists.SelectMany(static list => list.Attributes))
            {
                if (!IsAttributeNamed(attribute, "DuckDbColumn"))
                    continue;

                if (TryReadNamedIntAttributeArgument(attribute, "PrimaryKeyOrdinal", out var ordinal))
                    primaryKeyOrdinals[property.Identifier.ValueText] = ordinal;
            }
        }

        var tableAttribute = spanRecord.AttributeLists
            .SelectMany(static list => list.Attributes)
            .SingleOrDefault(static attribute => IsAttributeNamed(attribute, "DuckDbTable"));

        if (tableAttribute is null)
            throw new InvalidOperationException("SpanStorageRow must declare DuckDbTable metadata.");

        var onConflict = ReadNamedStringAttributeArgument(tableAttribute, "OnConflict");
        if (string.IsNullOrWhiteSpace(onConflict))
            throw new InvalidOperationException("SpanStorageRow DuckDbTable metadata must declare OnConflict.");

        return new SpanStorageIdentity(primaryKeyOrdinals, onConflict);
    }

    private static StorageRowIdentity ReadStorageRowIdentity(AbsolutePath storageRowsFile, string recordName)
    {
        if (!storageRowsFile.FileExists())
            throw new FileNotFoundException("Missing storage row source", storageRowsFile.ToString());

        var root = ParseCompilationUnit(storageRowsFile);
        var record = root.DescendantNodes()
            .OfType<RecordDeclarationSyntax>()
            .SingleOrDefault(record => record.Identifier.ValueText == recordName);

        if (record is null)
            throw new InvalidOperationException($"Missing {recordName} declaration.");

        var primaryKeyOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var property in record.Members.OfType<PropertyDeclarationSyntax>())
        {
            foreach (var attribute in property.AttributeLists.SelectMany(static list => list.Attributes))
            {
                if (!IsAttributeNamed(attribute, "DuckDbColumn"))
                    continue;

                if (TryReadNamedIntAttributeArgument(attribute, "PrimaryKeyOrdinal", out var ordinal))
                    primaryKeyOrdinals[property.Identifier.ValueText] = ordinal;
            }
        }

        var tableAttribute = record.AttributeLists
            .SelectMany(static list => list.Attributes)
            .SingleOrDefault(static attribute => IsAttributeNamed(attribute, "DuckDbTable"));

        if (tableAttribute is null)
            throw new InvalidOperationException($"{recordName} must declare DuckDbTable metadata.");

        var onConflict = ReadNamedStringAttributeArgument(tableAttribute, "OnConflict");
        if (string.IsNullOrWhiteSpace(onConflict))
            throw new InvalidOperationException($"{recordName} DuckDbTable metadata must declare OnConflict.");

        return new StorageRowIdentity(primaryKeyOrdinals, onConflict);
    }

    private static bool StoreInitializesGeneratedAuthoritativeDdl(AbsolutePath storeFile)
    {
        if (!storeFile.FileExists())
            return false;

        var root = ParseCompilationUnit(storeFile);
        return root.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Any(static memberAccess =>
                memberAccess is { Expression: IdentifierNameSyntax
                    {
                        Identifier.ValueText: "DuckDbGeneratedSchema"
                    },
                    Name.Identifier.ValueText: "AuthoritativeDdl"
                });
    }

    private IEnumerable<UnsafeSqlOffender> UnsafeSpanIdentitySqlFragments()
    {
        var sourceFiles = CollectorProjectDirectories.SelectMany(static directory => directory.GlobFiles("**/*.cs"))
            .Concat(CollectorProjectDirectories.SelectMany(static directory => directory.GlobFiles("**/*.sql")))
            .Where(static file =>
            {
                var path = file.ToString();
                return !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                       && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
            });

        foreach (var file in sourceFiles)
        {
            var relativePath = RootDirectory.GetRelativePathTo(file).ToString().Replace('\\', '/');
            if (file.Extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
            {
                var root = ParseCompilationUnit(file);
                foreach (var literal in root.DescendantNodes().OfType<LiteralExpressionSyntax>())
                {
                    if (literal.IsKind(SyntaxKind.StringLiteralExpression) &&
                        literal.Token.ValueText is { Length: > 0 } value &&
                        IsUnsafeSpanIdentitySql(value))
                    {
                        yield return new UnsafeSqlOffender(relativePath, NodeLine(literal), NodePreview(literal));
                    }
                }

                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (IsUnsafeSpanIdentitySql(lines[i]))
                    yield return new UnsafeSqlOffender(relativePath, i + 1, lines[i].Trim());
            }
        }
    }

    private static bool IsAttributeNamed(AttributeSyntax attribute, string expectedName)
    {
        var name = attribute.Name.ToString();
        return name.Equals(expectedName, StringComparison.Ordinal) ||
               name.Equals(expectedName + "Attribute", StringComparison.Ordinal) ||
               name.EndsWith("." + expectedName, StringComparison.Ordinal) ||
               name.EndsWith("." + expectedName + "Attribute", StringComparison.Ordinal);
    }

    private static bool TryReadNamedIntAttributeArgument(AttributeSyntax attribute, string name, out int value)
    {
        value = 0;
        foreach (var argument in attribute.ArgumentList?.Arguments ?? default(SeparatedSyntaxList<AttributeArgumentSyntax>))
        {
            if (argument.NameEquals?.Name.Identifier.ValueText != name ||
                argument.Expression is not LiteralExpressionSyntax literal ||
                !literal.IsKind(SyntaxKind.NumericLiteralExpression) ||
                literal.Token.Value is not int ordinal)
            {
                continue;
            }

            value = ordinal;
            return true;
        }

        return false;
    }

    /// <summary>Default and maximum of an integer schema, with a <c>$ref</c> to a components schema resolved.</summary>
    private readonly record struct OpenApiIntegerBounds(int Default, int Maximum)
    {
        public override string ToString() => $"default {Default}, maximum {Maximum}";
    }

    private static OpenApiIntegerBounds ReadOpenApiQueryBounds(JsonDocument document, string path, string parameterName)
    {
        var parameters = document.RootElement
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty("get")
            .GetProperty("parameters");

        foreach (var parameter in parameters.EnumerateArray())
        {
            if (!parameter.TryGetProperty("name", out var name) || name.GetString() != parameterName)
                continue;

            return ResolveOpenApiSchema(document, parameter.GetProperty("schema"));
        }

        throw new InvalidOperationException($"OpenAPI path '{path}' does not define parameter '{parameterName}'.");
    }

    // A parameter or property schema may carry its `default` inline and reference a shared scalar
    // (`$ref: #/components/schemas/Common.Pagination.PageLimit`) for the range; either side may hold either.
    private static OpenApiIntegerBounds ResolveOpenApiSchema(JsonDocument document, JsonElement schema)
    {
        JsonElement? referenced = null;
        if (schema.TryGetProperty("$ref", out var reference))
        {
            const string Prefix = "#/components/schemas/";
            var target = reference.GetString() ?? "";
            if (!target.StartsWith(Prefix, StringComparison.Ordinal))
                throw new InvalidOperationException($"Unsupported OpenAPI $ref '{target}'.");
            referenced = document.RootElement.GetProperty("components").GetProperty("schemas")
                .GetProperty(target[Prefix.Length..]);
        }

        return new OpenApiIntegerBounds(ReadInteger("default"), ReadInteger("maximum"));

        int ReadInteger(string property)
        {
            if (schema.TryGetProperty(property, out var inline)) return inline.GetInt32();
            if (referenced is { } shared && shared.TryGetProperty(property, out var value)) return value.GetInt32();
            throw new InvalidOperationException($"OpenAPI schema declares no '{property}'.");
        }
    }

    private static string ReadNamedStringAttributeArgument(AttributeSyntax attribute, string name)
    {
        foreach (var argument in attribute.ArgumentList?.Arguments ?? default(SeparatedSyntaxList<AttributeArgumentSyntax>))
        {
            if (argument.NameEquals?.Name.Identifier.ValueText == name &&
                argument.Expression is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return literal.Token.ValueText;
            }
        }

        return "";
    }

    private static bool IsUnsafeSpanIdentitySql(string sql)
    {
        var normalized = NormalizeSqlFragment(sql);
        return normalized.Contains("primary key (span_id)", StringComparison.Ordinal) ||
               normalized.Contains("on conflict (span_id)", StringComparison.Ordinal) ||
               normalized.Contains("on conflict (trace_id, span_id)", StringComparison.Ordinal);
    }

    private static string NormalizeSqlFragment(string sql)
    {
        var normalized = string.Join(
                ' ',
                sql.Replace("\"", "", StringComparison.Ordinal)
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();

        return normalized
            .Replace("( ", "(", StringComparison.Ordinal)
            .Replace(" )", ")", StringComparison.Ordinal)
            .Replace(" ,", ",", StringComparison.Ordinal)
            .Replace(",", ", ", StringComparison.Ordinal)
            .Replace("  ", " ", StringComparison.Ordinal);
    }

    private static CompilationUnitSyntax ParseCompilationUnit(AbsolutePath file)
    {
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file.ToString());
        return tree.GetCompilationUnitRoot();
    }

    private static int NodeLine(SyntaxNode node) =>
        node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    private static string NodePreview(SyntaxNode node)
    {
        var line = node.ToString()
            .Split('\n', 2)[0]
            .Replace('\r', ' ')
            .Trim();

        return line.Length <= 160 ? line : line[..160];
    }
}
