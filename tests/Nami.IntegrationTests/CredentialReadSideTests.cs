using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DuckDB.NET.Data;
using Microsoft.Extensions.Time.Testing;
using Nami.Collector.Storage;
using Nami.IntegrationTests.ClaudeCode;
using Nami.Tests.Infrastructure;
using Nami.Tools;

namespace Nami.IntegrationTests;

/// <summary>
///     The tools never show a credential either. Rows stored before the credential stage ran at ingest stay as they
///     are (ruled: no migration), so the read side passes every string of an answer through the same stage. The
///     test builds such rows: it ingests ordinary telemetry with a placeholder in every text a tool can return, closes
///     the store, turns each placeholder into a made-up token with SQL, behind the stage's back, and then asks all
///     six tools.
/// </summary>
[TestClass]
[TestCategory("Integration")]
[DependsOn(typeof(IngestStoreTests), nameof(IngestStoreTests.Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
public sealed class CredentialReadSideTests(TestContext context)
{
    // No pattern of the stage matches the placeholder, so ingest stores it as sent.
    private const string Placeholder = "ZZPLANTEDZZ";

    // An attribute key that becomes db.password behind the stage's back; at ingest such a key would lose its value.
    private const string KeyPlaceholder = "zzkeyzz";

    // A made-up token in the ghp_ shape, put together from parts so that no line of this file holds a whole token.
    private const string Token = "gh" + "p_" + "SYNTHETICsyntheticSYNTHETIC0123456789";

    private static readonly string[] s_plantedColumns =
    [
        "logs.attributes_json", "logs.body", "logs.service_name", "logs.severity_text", "resource_observations.attributes_json",
        "spans.attributes_json", "spans.events_json", "spans.name", "spans.resource_json", "spans.service_name",
        "spans.status_message"
    ];

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task Tools_RowsStoredBeforeTheStage_ReturnNoCredential()
    {
        var answers = await AnswersAfterPlantingAsync(Token, "before-the-stage.duckdb");

        var leaks = answers.Where(static answer => answer.Text.Contains(Token, StringComparison.Ordinal))
            .Select(static answer => answer.Tool).ToArray();
        Assert.IsEmpty(leaks, "these answers return the planted token: " + string.Join(", ", leaks));

        // The rest of each string is still there: the answers were read from the planted rows.
        var all = string.Join('\n', answers.Select(static answer => answer.Text));
        foreach (var rest in new[]
                 {
                     "[REDACTED]-operation", "[REDACTED]-status", "[REDACTED]-route", "[REDACTED]-type",
                     "[REDACTED]-version", "[REDACTED]-body", "[REDACTED]-severity", "[REDACTED]-service"
                 })
            Assert.Contains(rest, all);
        Assert.DoesNotContain("-named", all, "the value of an attribute named like a credential goes whole");
        Assert.AreEqual(7, answers.Count);
        Assert.HasCount(1, answers.Where(static answer => answer.Tool.EndsWith("(refusal)", StringComparison.Ordinal)));
    }

    /// <summary>
    ///     Ledger N77. The shapes of the fourth round, in rows stored before their rules existed: a secret named only
    ///     by the place it stands in is not shown either. Each text is planted into every text column and asked for
    ///     through all six tools. Quotes are written as the JSON columns hold them.
    /// </summary>
    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    [DataRow("https://hooks.example.test/services/T000/B000/{0}", DisplayName = "Slack webhook path")]
    [DataRow("https://api.example.test/bot123456:{0}/sendMessage", DisplayName = "Telegram bot path")]
    [DataRow("GET /app/home;jsessionid={0} 200", DisplayName = "jsessionid path parameter")]
    [DataRow("<add key='ApiKey' value='{0}' />", DisplayName = ".NET configuration entry")]
    [DataRow("db.example.test:5432:orders:svc:{0}", DisplayName = "pgpass line")]
    [DataRow("DbPwd={0}", DisplayName = "camel-case DbPwd=")]
    [DataRow("requirepass {0}", DisplayName = "redis requirepass")]
    [DataRow("X-Auth: {0}", DisplayName = "X-Auth header")]
    [DataRow("echo -n 'svc:{0}' | base64", DisplayName = "echo user:password piped to base64")]
    [DataRow("{{\\\"kty\\\":\\\"RSA\\\",\\\"d\\\":\\\"{0}\\\",\\\"e\\\":\\\"AQAB\\\"}}", DisplayName = "JWK private exponent")]
    [DataRow("sent Bearer {0} and failed", DisplayName = "Bearer with a lower-case value")]
    public async Task Tools_RowsStoredBeforeARuleOfTheFourthRound_ShowNoSecret(string shape)
    {
        // After a scheme only a value of lower-case letters needed a new rule.
        var secret = shape.Contains("Bearer", StringComparison.Ordinal) ? "zqrnineoooxoookkkk" : "ZQR900X000ZZZZZZ";
        var answers = await AnswersAfterPlantingAsync(string.Format(CultureInfo.InvariantCulture, shape, secret),
            "fourth-round-" + Guid.NewGuid().ToString("N") + ".duckdb", secret);

        var leaks = answers.Where(answer => answer.Text.Contains(secret, StringComparison.Ordinal))
            .Select(static answer => answer.Tool).ToArray();
        Assert.IsEmpty(leaks, "these answers return the planted secret: " + string.Join(", ", leaks));
        Assert.Contains("[REDACTED]", string.Join('\n', answers.Select(static answer => answer.Text)));
    }

    // Stores ordinary rows with a placeholder, turns the placeholder into the planted text behind the stage's back
    // and asks every tool. proof is what the planted columns must then hold.
    private async Task<List<(string Tool, string Text)>> AnswersAfterPlantingAsync(string planted, string file, string? proof = null)
    {
        proof ??= planted;
        var clock = new FakeTimeProvider(TestData.s_now);
        var path = Path.Combine(
            context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"),
            file);
        var time = TestData.Nanos(TestData.s_now.AddSeconds(-2));
        var traceId = 1.ToString("x32", CultureInfo.InvariantCulture);
        await using (var store = new DuckDbStore(path, timeProvider: clock))
        {
            await StorageTestData.SeedTraces(store, Traces(time), context.CancellationToken);
            await StorageTestData.SeedLogs(store, Logs(time, traceId), clock, context.CancellationToken);
            Assert.HasCount(1, (await store.ReadSnapshotAsync("default", ct: context.CancellationToken)).Spans);
        }

        Plant(path, planted);
        var stored = StoredRows.ColumnsContaining(path, proof);
        foreach (var column in s_plantedColumns)
            Assert.IsGreaterThan(0L, stored.GetValueOrDefault(column), column + " holds no planted text, so the test proves nothing there");

        clock.Advance(TimeSpan.FromSeconds(1));
        var answers = new List<(string Tool, string Text)>();
        await using (var store = new DuckDbStore(path, timeProvider: clock))
        {
            foreach (var (tool, input) in new (string, JsonObject)[]
                     {
                         ("sources_status", new JsonObject()), ("errors_recent", new JsonObject()),
                         ("traces_slowest", new JsonObject()), ("changes_recent", new JsonObject()),
                         ("logs_search", new JsonObject { ["query"] = "order" }),
                         ("traces_get", new JsonObject { ["trace_id"] = traceId }),
                         // A refusal echoes its input.
                         ("traces_slowest", new JsonObject { ["fields"] = new JsonArray("token=" + Token) })
                     })
            {
                using var document = JsonDocument.Parse(input.ToJsonString());
                var result = await new NamiToolService(store).InvokeAsync(tool, document.RootElement,
                    ProjectScope.DefaultProjectId, context.CancellationToken);
                answers.Add((tool + (result.IsError ? " (refusal)" : ""), result.Content.GetRawText()));
            }
        }

        return answers;
    }

    private static string Traces(ulong time)
    {
        var traces = JsonNode.Parse(OtlpData.Traces(time, service: Placeholder + "-service", exceptionEvent: true, error: true,
            operation: Placeholder + "-operation",
            attributes: new JsonArray(OtlpData.Attribute("http.route", Placeholder + "-route"),
                OtlpData.Attribute("db." + KeyPlaceholder, Placeholder + "-named"))))!;
        var resource = traces["resourceSpans"]![0]!["resource"]!["attributes"]!.AsArray();
        resource.Add((JsonNode)OtlpData.Attribute("service.version", Placeholder + "-version"));
        var span = traces["resourceSpans"]![0]!["scopeSpans"]![0]!["spans"]![0]!;
        span["status"]!["message"] = Placeholder + "-status";
        span["events"]![0]!["attributes"] = new JsonArray(OtlpData.Attribute("exception.type", Placeholder + "-type"));
        return traces.ToJsonString();
    }

    private static string Logs(ulong time, string traceId)
    {
        var logs = JsonNode.Parse(OtlpData.Logs(time, "order " + Placeholder + "-body", service: Placeholder + "-service",
            trace: traceId, span: 1.ToString("x16", CultureInfo.InvariantCulture)))!;
        var record = logs["resourceLogs"]![0]!["scopeLogs"]![0]!["logRecords"]![0]!;
        record["severityText"] = Placeholder + "-severity";
        record["attributes"] = new JsonArray(OtlpData.Attribute("http.route", Placeholder + "-route"));
        return logs.ToJsonString();
    }

    /// <summary>Replaces the placeholder with the token in every text and JSON column of every table.</summary>
    private static void Plant(string path, string planted)
    {
        using var connection = new DuckDBConnection($"DataSource={path}");
        connection.Open();
        var columns = new List<(string Table, string Column)>();
        using (var list = connection.CreateCommand())
        {
            list.CommandText = """
                SELECT table_name, column_name FROM duckdb_columns()
                WHERE database_name = current_database() AND schema_name = 'main' AND NOT internal
                  AND (data_type LIKE 'VARCHAR%' OR data_type = 'JSON')
                ORDER BY table_name, column_index
                """;
            using var reader = list.ExecuteReader();
            while (reader.Read()) columns.Add((reader.GetString(0), reader.GetString(1)));
        }

        foreach (var (table, column) in columns)
        {
            using var update = connection.CreateCommand();
            // CA2100: the names are this store's own tables and columns, read from its catalog above.
            update.CommandText = $"UPDATE \"{table}\" SET \"{column}\" = replace(CAST(\"{column}\" AS VARCHAR), $1, $2) " +
                                 $"WHERE contains(CAST(\"{column}\" AS VARCHAR), $1)";
            update.Parameters.Add(new DuckDBParameter { Value = Placeholder });
            update.Parameters.Add(new DuckDBParameter { Value = planted });
            update.ExecuteNonQuery();
            update.Parameters.Clear();
            update.Parameters.Add(new DuckDBParameter { Value = KeyPlaceholder });
            update.Parameters.Add(new DuckDBParameter { Value = "password" });
            update.ExecuteNonQuery();
        }
    }
}
