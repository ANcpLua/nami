using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DuckDB.NET.Data;
using Google.Protobuf;
using Microsoft.Extensions.Time.Testing;
using Nami.Collector.Ingestion;
using Nami.Collector.Storage;
using Nami.Tests.Infrastructure;
using Nami.Tools;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using LogRecord = OpenTelemetry.Proto.Logs.V1.LogRecord;

namespace Nami.IntegrationTests;

/// <summary>
///     The corpus of credential shapes (<see cref="CredentialShapes" />) against the credential stage: every shape is
///     redacted by the stage alone, by the converter for the shapes that depend on a key or a structure, over each
///     of the three transports into every stored column and tool answer, and by the read side for rows that were
///     stored before a rule existed. The shapes the stage does not recognise are listed by name, so that a change
///     to any of them is seen; the PRD lists them too.
/// </summary>
[TestClass]
[TestCategory("Integration")]
[DependsOn(typeof(IngestStoreTests), nameof(IngestStoreTests.Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
public sealed partial class CredentialShapeTests(TestContext context)
{
    private const string TraceId = "0af7651916cd43dd8448eb211c80319c";
    private const ulong Time = 1_794_745_800_000_000_000;

    // Shapes without a pattern: a secret that is a segment of a URL path, a value named by a neighbouring XML
    // attribute, a pgpass line, a password named only by prose, and a list under the plural tokens, which is a
    // count far more often than a credential.
    private static readonly HashSet<string> s_notRecognised = new(StringComparer.Ordinal)
    {
        "secret in a URL path (webhook)", "bot token in a URL path", "XML appSettings key ApiKey value", "pgpass line",
        "password in prose", "JSON, plural name tokens", "list under a plural name tokens"
    };

    [GeneratedRegex(@"ZQ[A-Z]\d{3}X\d{3}")]
    private static partial Regex Core();

    private static List<string> Survivors(string where, string text, CredentialShapes.Set set)
    {
        var decoded = text.Replace("\\u002B", "+", StringComparison.Ordinal);
        var found = Core().Matches(decoded).Select(match => where + ": " +
            CredentialShapes.NameOf(int.Parse(match.Value.AsSpan(3, 3), CultureInfo.InvariantCulture))).ToList();
        found.AddRange(set.Needles.Where(needle => decoded.Contains(needle.Text, StringComparison.Ordinal))
            .Select(needle => where + ": " + needle.Name + " (encoded)"));
        if (decoded.Contains(CredentialShapes.NumberMark, StringComparison.Ordinal)) found.Add(where + ": a number under a credential name");
        return found;
    }

    [TestMethod]
    public void Stage_EveryPlainAndJsonShape_IsRedacted()
    {
        var set = CredentialShapes.Build('S', 0);
        var survivors = new List<string>();
        foreach (var shape in set.Plain.Concat(set.Json).Where(static shape => !s_notRecognised.Contains(shape.Name)))
        {
            var redacted = CredentialRedaction.Redact(shape.Text);
            var needles = new CredentialShapes.Set([], [], [], [.. set.Needles.Where(needle => needle.Name == shape.Name)]);
            survivors.AddRange(Survivors(shape.Name, redacted, needles).Select(found => found + " => " + redacted));
            Assert.AreEqual(redacted, CredentialRedaction.Redact(redacted), shape.Name + ": a second pass changes nothing");
        }

        Assert.IsEmpty(survivors, "secret stored=True: " + string.Join("\n", survivors.Take(30)));
    }

    /// <summary>More shapes, found in the second round of the evaluation, with what must be left of each.</summary>
    [TestMethod]
    [DataRow("export TOKEN_GITHUB=ZQM900X000ZZZZZZ", "export TOKEN_GITHUB=[REDACTED]", DisplayName = "a name that goes on after the credential word")]
    [DataRow("GET /login?user=svc&pass=ZQM900X000ZZZZZZ", "GET /login?[REDACTED]", DisplayName = "relative URL with pass=")]
    [DataRow("aws iam x --secret-access-key ZQM900X000ZZZZZZ", "aws iam x --secret-access-key [REDACTED]", DisplayName = "--secret-access-key")]
    [DataRow("cs=Server%3Ddb%3BPassword%3DZQM900X000ZZZZZZ%3B", "cs=Server%3Ddb%3BPassword%3D[REDACTED]", DisplayName = "URL-encoded assignment")]
    [DataRow("ALTER USER app WITH PASSWORD 'ZQM900X000ZZZZZZ'", "ALTER USER app WITH PASSWORD '[REDACTED]'", DisplayName = "SQL PASSWORD")]
    [DataRow("CREATE USER 'app'@'%' IDENTIFIED BY 'ZQM900X000ZZZZZZ'", "CREATE USER 'app'@'%' IDENTIFIED BY '[REDACTED]'", DisplayName = "SQL IDENTIFIED BY")]
    [DataRow("api.key=ZQM900X000ZZZZZZ", "api.key=[REDACTED]", DisplayName = "java properties api.key=")]
    [DataRow("curl -u svc:ZQM900X000ZZZZZZ https://x.example.test/ -H \"X-Note: pass -u later\"",
        "curl -u svc:[REDACTED] https://x.example.test/ -H \"X-Note: pass -u later\"", DisplayName = "curl -u with a later -u in a header")]
    [DataRow("-----BEGIN RSA PRIVATE KEY-----\nMIIEZQM900X000ZZZZZZ\nno end line", "[REDACTED]", DisplayName = "PEM block without its END line")]
    [DataRow("key:\n-----BEGIN PUBLIC KEY-----\nMIIBIjANBgkq\n-----END PUBLIC KEY-----", "key:\n-----BEGIN PUBLIC KEY-----\nMIIBIjANBgkq\n-----END PUBLIC KEY-----",
        DisplayName = "a public key stays")]
    [DataRow("started 12:30:45.123, token_count=5 secret_name=db password_policy=strict", "started 12:30:45.123, token_count=5 secret_name=db password_policy=strict",
        DisplayName = "names that are about a credential and hold none")]
    public void Stage_FurtherShapes_AreRedacted_AndTheRestStays(string text, string expected)
    {
        Assert.AreEqual(expected, CredentialRedaction.Redact(text));
    }

    [TestMethod]
    public void Converter_EveryKeyedShape_IsRedactedOrDropped()
    {
        var set = CredentialShapes.Build('K', 0);
        var survivors = new List<string>();
        foreach (var shape in set.Keyed.Where(static shape => !s_notRecognised.Contains(shape.Name)))
        {
            var span = new Span
            {
                TraceId = ByteString.CopyFrom(Convert.FromHexString(TraceId)), SpanId = ByteString.CopyFrom(Convert.FromHexString("b7ad6b7169203331")),
                Name = "span", StartTimeUnixNano = Time, EndTimeUnixNano = Time + 1, Attributes = { shape.Attribute }
            };
            var row = Assert.ContainsSingle(IngestionStorageMapper.ToSpanStorageRows(OtlpConverter.ConvertTraceRequest(
                new ExportTraceServiceRequest { ResourceSpans = { new ResourceSpans { ScopeSpans = { new ScopeSpans { Spans = { span } } } } } })));
            survivors.AddRange(Survivors(shape.Name, row.AttributesJson ?? "", set).Select(found => found + " => " + row.AttributesJson));
        }

        Assert.IsEmpty(survivors, "secret stored=True: " + string.Join("\n", survivors.Take(30)));
    }

    /// <summary>
    ///     The whole corpus over each transport: as string attributes of spans and of a log record, as members of a
    ///     log body, and the keyed shapes as they are. No stored column and no tool answer holds a core.
    /// </summary>
    [TestMethod]
    [Timeout(180000, CooperativeCancellation = true)]
    [DataRow(CollectorHost.JsonTransport, 'J', DisplayName = "OTLP/HTTP JSON")]
    [DataRow(CollectorHost.ProtobufTransport, 'P', DisplayName = "OTLP/HTTP protobuf")]
    [DataRow(CollectorHost.GrpcTransport, 'G', DisplayName = "OTLP/gRPC")]
    public async Task Corpus_OverEachTransport_ReachesNoColumnAndNoToolAnswer(string transport, char letter)
    {
        var set = CredentialShapes.Build(letter, 1);
        var texts = set.Plain.Concat(set.Json).Where(static shape => !s_notRecognised.Contains(shape.Name))
            .Select(static shape => new KeyValue { Key = "acme.shape." + shape.Shape.ToString("000", CultureInfo.InvariantCulture), Value = new AnyValue { StringValue = shape.Text } })
            .Concat(set.Keyed.Where(static shape => !s_notRecognised.Contains(shape.Name)).Select(static shape => shape.Attribute)).ToArray();
        var traces = new ExportTraceServiceRequest { ResourceSpans = { new ResourceSpans { Resource = Service(), ScopeSpans = { new ScopeSpans() } } } };
        var logs = new ExportLogsServiceRequest { ResourceLogs = { new ResourceLogs { Resource = Service(), ScopeLogs = { new ScopeLogs() } } } };
        byte index = 0;
        foreach (var chunk in texts.Chunk(60))
        {
            index++;
            traces.ResourceSpans[0].ScopeSpans[0].Spans.Add(new Span
            {
                TraceId = ByteString.CopyFrom(Convert.FromHexString(TraceId)), SpanId = ByteString.CopyFrom(0, 0, 0, 0, 0, 0, 0, index),
                Name = "corpus " + index, StartTimeUnixNano = Time, EndTimeUnixNano = Time + 1_000_000, Attributes = { chunk }
            });
            var body = new KeyValueList();
            body.Values.Add(chunk);
            body.Values.Add(new KeyValue { Key = "message", Value = new AnyValue { StringValue = "corpus body " + index } });
            logs.ResourceLogs[0].ScopeLogs[0].LogRecords.Add(new LogRecord
            {
                TimeUnixNano = Time + index, SeverityNumber = SeverityNumber.Info, Body = new AnyValue { KvlistValue = body },
                Attributes = { chunk.Take(20) }
            });
        }

        var clock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds((long)(Time / 1_000_000)).AddSeconds(2));
        var path = Path.Combine(
            context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"), "shapes-" + transport + ".duckdb");
        var answers = new List<(string Tool, string Text)>();
        await using (var host = await CollectorHost.StartAsync(path, context.CancellationToken, clock: clock))
        {
            await SendAsync(host, transport, Signals.Traces, traces);
            await SendAsync(host, transport, Signals.Logs, logs);
            clock.Advance(TimeSpan.FromSeconds(1));
            var tools = new NamiToolService(host.Store);
            foreach (var (tool, input) in new (string, JsonObject)[]
                     {
                         ("sources_status", new JsonObject()), ("errors_recent", new JsonObject()), ("traces_slowest", new JsonObject()),
                         ("changes_recent", new JsonObject()), ("logs_search", new JsonObject { ["query"] = "corpus" }),
                         ("traces_get", new JsonObject { ["trace_id"] = TraceId })
                     })
            {
                input["max_bytes"] = 262144;
                using var document = JsonDocument.Parse(input.ToJsonString());
                var result = await tools.InvokeAsync(tool, document.RootElement, ProjectScope.DefaultProjectId, context.CancellationToken);
                answers.Add((tool, result.Content.GetRawText()));
            }
        }

        var survivors = new SortedSet<string>(StringComparer.Ordinal);
        var cells = CredentialGateTests.Cells(path);
        foreach (var (where, text) in cells.Concat(answers.Select(static answer => ("answer of " + answer.Tool, answer.Text))))
            survivors.UnionWith(Survivors(where, text, set));

        Assert.IsEmpty(survivors, "secret stored=True: " + string.Join("\n", survivors.Take(40)));
        Assert.IsTrue(cells.Any(static cell => cell.Column == "spans.attributes_json" && cell.Text.Contains("acme.shape.000", StringComparison.Ordinal)),
            "the corpus did not reach the span attributes, so the test proves nothing");
        Assert.IsTrue(cells.Any(static cell => cell.Column == "logs.body" && cell.Text.Contains("corpus body", StringComparison.Ordinal)),
            "the corpus did not reach the log body, so the test proves nothing");
        Assert.IsTrue(answers.Any(static answer => answer.Tool == "traces_get" && answer.Text.Contains("acme.shape.000", StringComparison.Ordinal)),
            "no tool answer holds the spans of the corpus");

        static Resource Service() => new()
        {
            Attributes = { new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = "corpus" } } }
        };
    }

    private async Task SendAsync(CollectorHost host, string transport, string signal, IMessage request)
    {
        switch (transport)
        {
            case CollectorHost.JsonTransport:
                {
                    using var response = await host.PostJsonAsync(signal, CredentialGateTests.OtlpJson(request), context.CancellationToken);
                    var body = await response.Content.ReadAsStringAsync(context.CancellationToken);
                    Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
                    Assert.DoesNotContain("rejected", body, "the collector rejected part of the corpus");
                    break;
                }
            case CollectorHost.ProtobufTransport:
                {
                    using var response = await host.PostProtobufAsync(signal, request.ToByteArray(), context.CancellationToken);
                    var body = await response.Content.ReadAsByteArrayAsync(context.CancellationToken);
                    Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, Encoding.UTF8.GetString(body));
                    Assert.AreEqual(0L, Signals.PartialSuccess(signal, body)?.Rejected ?? 0L, Signals.PartialSuccess(signal, body)?.Message);
                    break;
                }
            default:
                {
                    var body = await host.ExportGrpcAsync(signal, request.ToByteArray(), context.CancellationToken);
                    Assert.AreEqual(0L, Signals.PartialSuccess(signal, body)?.Rejected ?? 0L, Signals.PartialSuccess(signal, body)?.Message);
                    break;
                }
        }
    }

    /// <summary>
    ///     The read side matches ingest for rows that were stored before a rule existed: under a credential name a
    ///     number, a double, bytes, a map, a list and a list of maps go; in a list the argument after a credential
    ///     flag goes; the same in the map of a log body, in an event and on a resource. The rows are written with
    ///     placeholders and changed behind the stage's back.
    /// </summary>
    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task Tools_RowsStoredBeforeARule_ShowNoKeyedOrPositionalCredential()
    {
        var clock = new FakeTimeProvider(TestData.s_now);
        var path = Path.Combine(
            context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"), "before-a-rule.duckdb");
        var time = TestData.Nanos(TestData.s_now.AddSeconds(-2));
        var traceId = 1.ToString("x32", CultureInfo.InvariantCulture);
        await using (var store = new DuckDbStore(path, timeProvider: clock))
        {
            var traces = JsonNode.Parse(OtlpData.Traces(time, service: "svc", exceptionEvent: true, error: true, operation: "op", attributes: Attributes()))!;
            var span = traces["resourceSpans"]![0]!["scopeSpans"]![0]!["spans"]![0]!;
            span["events"]![0]!["attributes"] = Attributes();
            traces["resourceSpans"]![0]!["resource"]!["attributes"]!.AsArray().Add((JsonNode)A("probe20.resource.zzkeyzz", Iv(915117020)));
            await StorageTestData.SeedTraces(store, traces.ToJsonString(), context.CancellationToken);
            var logs = JsonNode.Parse(OtlpData.Logs(time, "order body", service: "svc", trace: traceId, span: 1.ToString("x16", CultureInfo.InvariantCulture)))!;
            var record = logs["resourceLogs"]![0]!["scopeLogs"]![0]!["logRecords"]![0]!;
            record["attributes"] = Attributes();
            record["body"] = Map(A("message", Sv("order body map")), A("zzkeyzz", Iv(915117030)), A("args", Arr(Sv("--zzkeyzz"), Sv("ZZOPAQUEZZ30"))));
            await StorageTestData.SeedLogs(store, logs.ToJsonString(), clock, context.CancellationToken);
        }

        var planted = 0;
        using (var connection = new DuckDBConnection($"DataSource={path}"))
        {
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
                foreach (var (from, to) in new[] { ("ZZOPAQUEZZ", "ZQR950X0"), ("zzkeyzz", "password") })
                {
                    using var update = connection.CreateCommand();
                    update.CommandText = $"UPDATE \"{table}\" SET \"{column}\" = replace(CAST(\"{column}\" AS VARCHAR), $1, $2) WHERE contains(CAST(\"{column}\" AS VARCHAR), $1)";
                    update.Parameters.Add(new DuckDBParameter { Value = from });
                    update.Parameters.Add(new DuckDBParameter { Value = to });
                    planted += update.ExecuteNonQuery();
                }
            }
        }

        Assert.IsGreaterThan(5, planted, "nothing was planted behind the stage, so the test proves nothing");
        clock.Advance(TimeSpan.FromSeconds(1));
        var shown = new List<string>();
        var answered = new StringBuilder();
        await using (var store = new DuckDbStore(path, timeProvider: clock))
        {
            foreach (var (tool, input) in new (string, JsonObject)[]
                     {
                         ("sources_status", new JsonObject()), ("errors_recent", new JsonObject()), ("traces_slowest", new JsonObject()),
                         ("changes_recent", new JsonObject()), ("logs_search", new JsonObject { ["query"] = "order" }),
                         ("traces_get", new JsonObject { ["trace_id"] = traceId })
                     })
            {
                input["max_bytes"] = 262144;
                using var document = JsonDocument.Parse(input.ToJsonString());
                var result = await new NamiToolService(store).InvokeAsync(tool, document.RootElement, ProjectScope.DefaultProjectId, context.CancellationToken);
                var text = result.Content.GetRawText().Replace("\\u002B", "+", StringComparison.Ordinal);
                answered.Append(text);
                shown.AddRange(Planted().Matches(text).Select(match => tool + ": " + match.Value).Distinct());
            }
        }

        Assert.IsEmpty(shown, "secret shown=True: " + string.Join(", ", shown));
        Assert.Contains("probe12.bool_under.password", answered.ToString(), "the planted span was not answered, so the test proves nothing");
        Assert.Contains("order body map", answered.ToString(), "the planted log body was not answered");
    }

    [GeneratedRegex(@"ZQR\d\d\dX\d\d\d|91511[5-9]\d{3}|RkFLRUJZVEVTU0VDUkVU")]
    private static partial Regex Planted();

    private static JsonObject A(string key, JsonObject value) => new() { ["key"] = key, ["value"] = value };
    private static JsonObject Sv(string text) => new() { ["stringValue"] = text };
    private static JsonObject Iv(long number) => new() { ["intValue"] = number.ToString(CultureInfo.InvariantCulture) };
    private static JsonObject Arr(params JsonNode[] items) => new() { ["arrayValue"] = new JsonObject { ["values"] = new JsonArray(items) } };
    private static JsonObject Map(params JsonNode[] members) => new() { ["kvlistValue"] = new JsonObject { ["values"] = new JsonArray(members) } };

    private static JsonArray Attributes() => new(
        A("probe01.int_under.zzkeyzz", Iv(915117001)),
        A("probe02.double_under.zzkeyzz", new JsonObject { ["doubleValue"] = 915118001.5 }),
        A("probe03.bytes_under.zzkeyzz", new JsonObject { ["bytesValue"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("FAKEBYTESSECRETVALUE")) }),
        A("probe04.args", Arr(Sv("mysql"), Sv("--zzkeyzz"), Sv("ZZOPAQUEZZ04"))),
        A("probe05.map", Map(A("zzkeyzz", Sv("ZZOPAQUEZZ05")), A("n_zzkeyzz", Iv(915119001)))),
        A("probe06.map_under.zzkeyzz", Map(A("value", Sv("ZZOPAQUEZZ06")))),
        A("probe07.cmd", Sv("mysql -u root -pZZOPAQUEZZ07 orders; tool --zzkeyzz ZZOPAQUEZZ07")),
        A("probe08.json", Sv("{\"zzkeyzz\":915116001,\"args\":[\"x\",\"--zzkeyzz\",\"ZZOPAQUEZZ08\"]}")),
        A("probe09.list_under.zzkeyzz", Arr(Sv("ZZOPAQUEZZ09"), Iv(915117002))),
        A("probe10.string_under.zzkeyzz", Sv("ZZOPAQUEZZ10")),
        A("probe11.list_of_maps_under.zzkeyzz", Arr(Map(A("v", Sv("ZZOPAQUEZZ11"))))),
        A("probe12.bool_under.zzkeyzz", new JsonObject { ["boolValue"] = true }));

    /// <summary>
    ///     The stage reads a text a bounded number of times, however long its lines are. Four mebibytes of
    ///     assignments on one line took minutes when every match searched its line from the start; the budget here
    ///     is far above what a loaded machine needs for a linear pass and far below that.
    /// </summary>
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow("token=x ", DisplayName = "assignments")]
    [DataRow("a;b password=x ", DisplayName = "assignments on a line with semicolons")]
    [DataRow("curl -u a:b mysql -px --token y ", DisplayName = "flags of tools")]
    [DataRow("\"k\":1,", DisplayName = "no credential at all")]
    public void Stage_OnOneLineOfFourMebibytes_StaysWithinALinearBudget(string unit)
    {
        var text = new StringBuilder(4 * 1024 * 1024 + unit.Length).Insert(0, unit, 4 * 1024 * 1024 / unit.Length).ToString();
        _ = CredentialRedaction.Redact(unit + unit);

        var watch = Stopwatch.StartNew();
        var redacted = CredentialRedaction.Redact(text);
        watch.Stop();

        Assert.IsLessThan(TimeSpan.FromSeconds(30), watch.Elapsed, $"{text.Length} characters took {watch.Elapsed}");
        Assert.DoesNotContain("=x ", redacted);
        context.WriteLine($"{unit.Trim()}: {text.Length} characters in {watch.ElapsedMilliseconds} ms");
    }
}
