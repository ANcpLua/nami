using System.Globalization;
using System.IO.Compression;
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
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using LogRecord = OpenTelemetry.Proto.Logs.V1.LogRecord;

namespace Nami.IntegrationTests;

/// <summary>
///     The gate of the observer's rule (16 Nov 2026): no token or password is stored or shown by nami. A fixed corpus
///     of credential shapes goes through each transport into every carrier and text field of a trace, a log and a
///     metric export; then every text column of every table (read from the store's own catalog) and every answer of
///     the six tools is scanned, and no credential may appear. A text column this test does not know fails it, so a
///     new column cannot go uncovered. A second corpus of ordinary telemetry must arrive byte-identical.
///     Every credential is made up here from a marker that names its transport, shape and place; a failure names
///     those, never the credential.
/// </summary>
[TestClass]
[TestCategory("Integration")]
[DependsOn(typeof(IngestStoreTests), nameof(IngestStoreTests.Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
public sealed partial class CredentialGateTests(TestContext context)
{
    // Every text and JSON column of the store, with a place of the corpus whose text must arrive there, or null for a
    // column that holds no text of a sender (ids nami computes, hashes, names nami gives).
    private static readonly Dictionary<string, string?> s_columns = new(StringComparer.Ordinal)
    {
        ["spans.project_id"] = null, // covered by logs.project_id: one rule reads the project hint for every signal
        ["spans.span_id"] = null, ["spans.trace_id"] = null, ["spans.parent_span_id"] = null,
        ["spans.session_id"] = "span.session", ["spans.name"] = "span.name", ["spans.service_name"] = "traces.service",
        ["spans.gen_ai_provider_name"] = "span.provider", ["spans.gen_ai_operation_name"] = "span.operation",
        ["spans.gen_ai_output_type"] = "span.output", ["spans.gen_ai_request_model"] = "span.request_model",
        ["spans.gen_ai_response_model"] = "span.response_model", ["spans.attributes_json"] = "span.string",
        ["spans.resource_json"] = "traces.resource.string", ["spans.resource_entity_refs_json"] = "traces.entity.type",
        ["spans.schema_url"] = "traces.scope.schema", ["spans.status_message"] = "span.status",
        ["spans.events_json"] = "event.name", ["spans.links_json"] = "link.tracestate",
        ["spans.scope_name"] = "traces.scope.name", ["spans.scope_version"] = "traces.scope.version",
        ["spans.scope_schema_url"] = "traces.scope.schema", ["spans.scope_attributes_json"] = "traces.scope.string",
        ["spans.resource_schema_url"] = "traces.resource.schema", ["spans.trace_state"] = "span.tracestate",
        ["nami_schema_meta.authoritative_schema_hash"] = null, ["nami_schema_meta.derived_schema_hash"] = null,
        ["logs.project_id"] = "project", ["logs.log_id"] = null, ["logs.trace_id"] = null, ["logs.span_id"] = null,
        ["logs.event_name"] = "log.event", ["logs.session_id"] = "log.session", ["logs.severity_text"] = "log.severity",
        ["logs.body"] = "log.body", ["logs.service_name"] = "logs.service", ["logs.attributes_json"] = "log.string",
        ["logs.resource_json"] = "logs.resource.string", ["logs.resource_entity_refs_json"] = "logs.entity.type",
        ["logs.scope_name"] = "logs.scope.name", ["logs.scope_version"] = "logs.scope.version",
        ["logs.scope_schema_url"] = "logs.scope.schema", ["logs.scope_attributes_json"] = "logs.scope.string",
        ["logs.resource_schema_url"] = "logs.resource.schema",
        ["signal_watermarks.project_id"] = null, ["signal_watermarks.signal"] = null,
        ["signal_watermarks.service_name"] = "traces.service",
        ["resource_observations.project_id"] = null, ["resource_observations.observation_id"] = null,
        ["resource_observations.service_name"] = "traces.service",
        ["resource_observations.attributes_json"] = "traces.resource.string",
        ["read_snapshots.project_id"] = null, ["read_snapshots.snapshot_id"] = null, ["read_snapshots.payload"] = "span.name",
        ["attribute_stability.project_id"] = null, ["attribute_stability.attribute_key"] = "span.key",
        ["attribute_stability.stability"] = null,
        // Written by no code since ingest renames nothing (F1); scanned all the same.
        ["attribute_discovery.project_id"] = null, ["attribute_discovery.original_key"] = null,
        ["attribute_discovery.canonical_key"] = null, ["attribute_discovery.namespace"] = null,
        ["metric_series.project_id"] = null, ["metric_series.series_id"] = null, ["metric_series.metric_name"] = "metric.name",
        ["metric_series.unit"] = "metric.unit", ["metric_series.description"] = "metric.description",
        ["metric_series.service_name"] = "metrics.service", ["metric_series.schema_url"] = "metrics.scope.schema",
        ["metric_series.attributes_json"] = "point.string", ["metric_series.resource_json"] = "metrics.resource.string",
        ["metric_series.resource_entity_refs_json"] = "metrics.entity.type", ["metric_series.scope_name"] = "metrics.scope.name",
        ["metric_series.scope_version"] = "metrics.scope.version", ["metric_series.scope_schema_url"] = "metrics.scope.schema",
        ["metric_series.scope_attributes_json"] = "metrics.scope.string",
        ["metric_series.resource_schema_url"] = "metrics.resource.schema", ["metric_series.instrument_id"] = null,
        ["metric_points.project_id"] = null, ["metric_points.series_id"] = null,
        ["metric_points.bucket_bounds_json"] = null, ["metric_points.bucket_counts_json"] = null,
        ["metric_points.projection_unavailable_reason"] = null,
        ["metric_observations.project_id"] = null, ["metric_observations.series_id"] = null,
        ["metric_observations.observation_id"] = null, ["metric_observations.measurement_id"] = null,
        ["metric_observations.data_json"] = null, ["metric_observations.exemplars_json"] = "exemplar.string",
        ["metric_observations.instrument_metadata_json"] = "metric.metadata.string",
        ["metric_exemplars.project_id"] = null, ["metric_exemplars.series_id"] = null, ["metric_exemplars.trace_id"] = null
    };

    private static readonly string[] s_ordinary =
    [
        "SELECT id, total FROM orders WHERE status = 'open' AND created_at > $1 ORDER BY created_at DESC LIMIT 20",
        "https://api.example.test/v1/orders/42", "/home/builder/src/nami/OtlpConverter.cs:412", "GET /api/orders/{id}",
        "gen_ai.usage.input_tokens", "tokens=5", "input_tokens: 1200, output_tokens: 87", "claude_code.tool_result",
        "claude_code.api_request", "4185b11a38b2a139176df82c0e9f068a2af2e41c", "0f8fad5b-d9cb-469f-a165-70867728950e",
        "at Nami.Collector.Ingestion.OtlpConverter.ConvertLogs(ExportLogsServiceRequest otlp)", "ls -la /tmp && git status --short"
    ];

    [GeneratedRegex(@"ZQ[JPG]\d\dX\d\d\d")]
    private static partial Regex Core();

    /// <summary>
    ///     The credential corpus over one transport: nothing of it is in any column of the closed store or in any
    ///     tool answer, every text column is known to this test, and each covered column holds the words around its
    ///     credential.
    /// </summary>
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow(CollectorHost.JsonTransport, 'J', DisplayName = "OTLP/HTTP JSON")]
    [DataRow(CollectorHost.ProtobufTransport, 'P', DisplayName = "OTLP/HTTP protobuf")]
    [DataRow(CollectorHost.GrpcTransport, 'G', DisplayName = "OTLP/gRPC")]
    public async Task Credentials_InEveryCarrierAndTextField_ReachNoColumnAndNoToolAnswer(string transport, char letter)
    {
        var corpus = new Corpus(letter);
        var path = Path.Combine(
            context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"),
            "gate-" + transport + ".duckdb");
        var answers = await IngestAndAskAsync(path, transport, corpus.Full, corpus.Short);

        var cells = Cells(path);
        var unknown = cells.Select(static cell => cell.Column).Distinct().Where(static column => !s_columns.ContainsKey(column))
            .ToArray();
        Assert.IsEmpty(unknown, "Text columns this gate does not know; give each a place of the corpus or say why it " +
                                "holds no sender's text: " + string.Join(", ", unknown));
        Assert.IsEmpty(s_columns.Keys.Except(cells.Select(static cell => cell.Column).Distinct()).ToArray(),
            "columns of the gate's table that the store no longer has");

        var leaks = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (column, text) in cells.Concat(answers.Select(static answer => ("answer of " + answer.Tool, answer.Text))))
        {
            foreach (Match match in Core().Matches(text)) leaks.Add(column + ": " + corpus.Describe(match.Value));
            foreach (var encoded in corpus.Encoded.Where(encoded => text.Contains(encoded.Text, StringComparison.Ordinal)))
                leaks.Add(column + ": " + encoded.Name);
        }

        Assert.IsEmpty(leaks, "secret stored=True: " + string.Join("; ", leaks.Take(40)));
        Assert.IsGreaterThan(0, corpus.Planted, "the corpus planted nothing");

        foreach (var (column, place) in s_columns.Where(static pair => pair.Value is not null))
        {
            Assert.IsTrue(cells.Any(cell => cell.Column == column && cell.Text.Contains("(" + place + ")", StringComparison.Ordinal)),
                $"{column} does not hold the text of {place}: the corpus did not reach it, so the gate proves nothing there");
        }

        Assert.IsTrue(answers.Any(static answer => answer.Text.Contains("(span.name)", StringComparison.Ordinal)),
            "no tool answer holds the span of the corpus");
        Assert.IsTrue(answers.Any(static answer => answer.Text.Contains("(log.body)", StringComparison.Ordinal)),
            "no tool answer holds the log record of the corpus");
    }

    /// <summary>
    ///     Ordinary telemetry in every carrier and text field arrives byte-identical over each transport: SQL, URLs
    ///     without credentials, paths, names, token counters, Claude Code event names, git SHAs, UUIDs.
    /// </summary>
    [TestMethod]
    [Timeout(120000, CooperativeCancellation = true)]
    [DataRow(CollectorHost.JsonTransport, DisplayName = "OTLP/HTTP JSON")]
    [DataRow(CollectorHost.ProtobufTransport, DisplayName = "OTLP/HTTP protobuf")]
    [DataRow(CollectorHost.GrpcTransport, DisplayName = "OTLP/gRPC")]
    public async Task OrdinaryTelemetry_InEveryCarrierAndTextField_IsStoredByteIdentical(string transport)
    {
        var planted = new List<string>();
        var path = Path.Combine(
            context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"),
            "ordinary-" + transport + ".duckdb");
        _ = await IngestAndAskAsync(path, transport, Plant, Plant);

        var stored = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, text) in Cells(path)) Collect(text, stored);
        // A planted key follows its template's name (k8s.pod.label.<text>); every other text stands alone.
        var missing = planted.Where(text => !stored.Contains(text) && !stored.Contains("k8s.pod.label." + text)).ToArray();
        Assert.IsEmpty(missing, "not stored as sent: " + string.Join(" | ", missing.Take(10)));
        Assert.IsGreaterThan(100, planted.Count);

        string Plant(string place)
        {
            var text = "(" + place + ") " + s_ordinary[planted.Count % s_ordinary.Length];
            planted.Add(text);
            return text;
        }
    }

    /// <summary>Sends the three exports and the project export over the transport, then asks every tool everything.</summary>
    private async Task<List<(string Tool, string Text)>> IngestAndAskAsync(string path, string transport,
        Func<string, string> full, Func<string, string> brief)
    {
        var clock = new FakeTimeProvider(TestData.s_now);
        var time = TestData.Nanos(TestData.s_now.AddSeconds(-2));
        var project = brief("project");
        var answers = new List<(string Tool, string Text)>();
        await using (var host = await CollectorHost.StartAsync(path, context.CancellationToken, clock: clock))
        {
            await SendAsync(host, transport, Signals.Traces, Traces(time, full, brief));
            await SendAsync(host, transport, Signals.Logs, Logs(time, full, brief, null));
            await SendAsync(host, transport, Signals.Metrics, Metrics(time, full, brief));
            await SendAsync(host, transport, Signals.Logs, Logs(time, static place => "ordinary " + place,
                static place => "ordinary " + place, project));
            clock.Advance(TimeSpan.FromSeconds(1));

            var tools = new NamiToolService(host.Store);
            foreach (var projectId in new[] { ProjectScope.DefaultProjectId, CredentialRedaction.Redact(project).Trim() })
            {
                foreach (var (tool, input) in new (string, JsonObject)[]
                         {
                             ("sources_status", new JsonObject()), ("errors_recent", new JsonObject()),
                             ("traces_slowest", new JsonObject()), ("changes_recent", new JsonObject()),
                             ("logs_search", new JsonObject { ["query"] = "(" }),
                             ("traces_get", new JsonObject { ["trace_id"] = TraceId })
                         })
                {
                    string? cursor = null;
                    do
                    {
                        input["max_bytes"] = 262144;
                        if (cursor is not null) input["cursor"] = cursor;
                        using var document = JsonDocument.Parse(input.ToJsonString());
                        var result = await tools.InvokeAsync(tool, document.RootElement, projectId, context.CancellationToken);
                        answers.Add((tool, result.Content.GetRawText()));
                        cursor = !result.IsError && result.Content.TryGetProperty("next_cursor", out var next) &&
                                 next.ValueKind == JsonValueKind.String
                            ? next.GetString()
                            : null;
                    } while (cursor is not null);
                }
            }
        }

        return answers;
    }

    private async Task SendAsync(CollectorHost host, string transport, string signal, IMessage request)
    {
        switch (transport)
        {
            case CollectorHost.JsonTransport:
                {
                    using var response = await host.PostJsonAsync(signal, OtlpJson(request), context.CancellationToken);
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

    /// <summary>OTLP/JSON of a request: protobuf JSON with enums as numbers and trace and span ids in hexadecimal.</summary>
    internal static string OtlpJson(IMessage request)
    {
        // A value nested 16 deep is some 70 levels of protobuf JSON.
        var json = JsonNode.Parse(new JsonFormatter(JsonFormatter.Settings.Default.WithFormatEnumsAsIntegers(true)).Format(request),
            documentOptions: new JsonDocumentOptions { MaxDepth = 512 })!;
        Hex(json);
        return json.ToJsonString();

        static void Hex(JsonNode node)
        {
            switch (node)
            {
                case JsonObject item:
                    foreach (var (name, value) in item.ToArray())
                    {
                        if (value is null) continue;
                        if (name is "traceId" or "spanId" or "parentSpanId" && value is JsonValue id)
                            item[name] = Convert.ToHexStringLower(Convert.FromBase64String(id.GetValue<string>()));
                        else Hex(value);
                    }

                    break;
                case JsonArray list:
                    foreach (var value in list)
                        if (value is not null) Hex(value);
                    break;
            }
        }
    }

    /// <summary>Every cell of every text, JSON and BLOB column of the closed store, a BLOB as its decompressed text.</summary>
    private static List<(string Column, string Text)> Cells(string path)
    {
        using var connection = new DuckDBConnection($"DataSource={path};ACCESS_MODE=READ_ONLY");
        connection.Open();
        var columns = new List<(string Table, string Column, bool Blob)>();
        using (var list = connection.CreateCommand())
        {
            list.CommandText = """
                SELECT table_name, column_name, data_type FROM duckdb_columns()
                WHERE database_name = current_database() AND schema_name = 'main' AND NOT internal
                  AND (data_type LIKE 'VARCHAR%' OR data_type = 'JSON' OR data_type = 'BLOB')
                ORDER BY table_name, column_index
                """;
            using var reader = list.ExecuteReader();
            while (reader.Read()) columns.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2) == "BLOB"));
        }

        var cells = new List<(string, string)>();
        foreach (var (table, column, blob) in columns)
        {
            var name = table + "." + column;
            // A column without a row still counts as a column the gate must know.
            cells.Add((name, ""));
            using var read = connection.CreateCommand();
            // CA2100: the names are this store's own tables and columns, read from its catalog above.
            read.CommandText = blob
                ? $"SELECT \"{column}\" FROM \"{table}\" WHERE \"{column}\" IS NOT NULL"
                : $"SELECT CAST(\"{column}\" AS VARCHAR) FROM \"{table}\" WHERE \"{column}\" IS NOT NULL";
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                if (!blob)
                {
                    cells.Add((name, reader.GetString(0)));
                    continue;
                }

                using var stream = reader.GetStream(0);
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                cells.Add((name, Encoding.UTF8.GetString(copy.ToArray())));
                try
                {
                    copy.Position = 0;
                    using var brotli = new BrotliStream(copy, CompressionMode.Decompress, true);
                    using var text = new StreamReader(brotli, Encoding.UTF8);
                    cells.Add((name, text.ReadToEnd()));
                }
                catch (InvalidDataException)
                {
                    // Not compressed: the raw text above is the cell.
                }
            }
        }

        return cells;
    }

    /// <summary>Every string and member name inside a cell, through JSON that is nested in JSON strings.</summary>
    private static void Collect(string text, HashSet<string> strings)
    {
        if (!strings.Add(text)) return;
        var trimmed = text.AsSpan().TrimStart();
        if (trimmed.Length == 0 || trimmed[0] is not ('{' or '[' or '"')) return;
        try
        {
            using var document = JsonDocument.Parse(text);
            Walk(document.RootElement);
        }
        catch (JsonException)
        {
            // Text that only starts like JSON.
        }

        void Walk(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var member in element.EnumerateObject())
                    {
                        strings.Add(member.Name);
                        Walk(member.Value);
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray()) Walk(item);
                    break;
                case JsonValueKind.String:
                    Collect(element.GetString()!, strings);
                    break;
            }
        }
    }

    private const string TraceId = "0af7651916cd43dd8448eb211c80319c";

    private static KeyValue Text(string key, string value) =>
        new() { Key = key, Value = new AnyValue { StringValue = value } };

    // The attribute shapes of one carrier: a string, an array item, a map member (value and key), exception text, a
    // key whose own text is planted, a key the registry does not know, and a JSON document as a string.
    private static IEnumerable<KeyValue> Attributes(string carrier, Func<string, string> full, Func<string, string> brief)
    {
        yield return Text("http.route", full(carrier + ".string"));
        yield return new KeyValue
        {
            Key = "http.request.header.x-trace",
            Value = new AnyValue { ArrayValue = new ArrayValue { Values = { new AnyValue { StringValue = full(carrier + ".array") } } } }
        };
        yield return new KeyValue
        {
            Key = "feature_flag.result.value",
            Value = new AnyValue
            {
                KvlistValue = new KeyValueList
                {
                    Values = { Text("error.type", full(carrier + ".map")), Text(brief(carrier + ".map.key"), "ordinary") }
                }
            }
        };
        yield return Text("exception.message", full(carrier + ".exception"));
        yield return Text("k8s.pod.label." + brief(carrier + ".key"), "ordinary");
        yield return Text("acme.unknown", full(carrier + ".unknown"));
        yield return Text("tool_parameters",
            new JsonObject { ["full_command"] = full(carrier + ".json"), ["timeout"] = 120000 }.ToJsonString());
    }

    private static Resource ResourceOf(string signal, Func<string, string> full, Func<string, string> brief, string? project)
    {
        var resource = new Resource
        {
            Attributes = { Text("service.name", brief(signal + ".service")), Attributes(signal + ".resource", full, brief) },
            EntityRefs =
            {
                new EntityRef
                {
                    Type = brief(signal + ".entity.type"), SchemaUrl = brief(signal + ".entity.schema"),
                    IdKeys = { "service.name" }
                }
            }
        };
        if (project is not null) resource.Attributes.Add(Text("nami.project.id", project));
        return resource;
    }

    private static InstrumentationScope ScopeOf(string signal, Func<string, string> full, Func<string, string> brief) => new()
    {
        Name = brief(signal + ".scope.name"), Version = brief(signal + ".scope.version"),
        Attributes = { Attributes(signal + ".scope", full, brief) }
    };

    private static ExportTraceServiceRequest Traces(ulong time, Func<string, string> full, Func<string, string> brief)
    {
        var span = new Span
        {
            TraceId = ByteString.CopyFrom(Convert.FromHexString(TraceId)),
            SpanId = ByteString.CopyFrom(Convert.FromHexString("b7ad6b7169203331")),
            Name = full("span.name"), TraceState = brief("span.tracestate"),
            StartTimeUnixNano = time, EndTimeUnixNano = time + 1_000_000,
            Status = new Status { Code = Status.Types.StatusCode.Error, Message = full("span.status") },
            Attributes =
            {
                Attributes("span", full, brief), Text("session.id", brief("span.session")),
                Text("gen_ai.provider.name", brief("span.provider")), Text("gen_ai.operation.name", brief("span.operation")),
                Text("gen_ai.output.type", brief("span.output")), Text("gen_ai.request.model", brief("span.request_model")),
                Text("gen_ai.response.model", brief("span.response_model"))
            },
            Events =
            {
                new Span.Types.Event { Name = full("event.name"), TimeUnixNano = time, Attributes = { Attributes("event", full, brief) } },
                new Span.Types.Event
                {
                    Name = "exception", TimeUnixNano = time,
                    Attributes = { Text("exception.type", brief("event.exception.type")), Text("exception.stacktrace", full("event.stacktrace")) }
                }
            },
            Links =
            {
                new Span.Types.Link
                {
                    TraceId = ByteString.CopyFrom(Convert.FromHexString("1af7651916cd43dd8448eb211c80319c")),
                    SpanId = ByteString.CopyFrom(Convert.FromHexString("c7ad6b7169203331")),
                    TraceState = brief("link.tracestate"), Attributes = { Attributes("link", full, brief) }
                }
            }
        };
        return new ExportTraceServiceRequest
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
                    Resource = ResourceOf("traces", full, brief, null), SchemaUrl = brief("traces.resource.schema"),
                    ScopeSpans =
                    {
                        new ScopeSpans
                        {
                            Scope = ScopeOf("traces", full, brief), SchemaUrl = brief("traces.scope.schema"), Spans = { span }
                        }
                    }
                }
            }
        };
    }

    private static ExportLogsServiceRequest Logs(ulong time, Func<string, string> full, Func<string, string> brief, string? project)
    {
        var text = new LogRecord
        {
            TimeUnixNano = time, SeverityText = brief("log.severity"), EventName = brief("log.event"),
            Body = new AnyValue { StringValue = full("log.body") },
            Attributes = { Attributes("log", full, brief), Text("session.id", brief("log.session")) }
        };
        var structured = new LogRecord
        {
            TimeUnixNano = time + 1,
            Body = new AnyValue
            {
                ArrayValue = new ArrayValue
                {
                    Values =
                    {
                        new AnyValue { StringValue = full("log.body.array") },
                        new AnyValue { KvlistValue = new KeyValueList { Values = { Text("error.type", full("log.body.map")) } } }
                    }
                }
            },
            Attributes = { Text("event.name", brief("log.event.attribute")) }
        };
        return new ExportLogsServiceRequest
        {
            ResourceLogs =
            {
                new ResourceLogs
                {
                    Resource = ResourceOf("logs", full, brief, project), SchemaUrl = brief("logs.resource.schema"),
                    ScopeLogs =
                    {
                        new ScopeLogs
                        {
                            Scope = ScopeOf("logs", full, brief), SchemaUrl = brief("logs.scope.schema"),
                            LogRecords = { text, structured }
                        }
                    }
                }
            }
        };
    }

    private static ExportMetricsServiceRequest Metrics(ulong time, Func<string, string> full, Func<string, string> brief)
    {
        var point = new NumberDataPoint
        {
            AsInt = 7, StartTimeUnixNano = time, TimeUnixNano = time + 1_000_000,
            Attributes = { Attributes("point", full, brief) },
            Exemplars =
            {
                new Exemplar { AsInt = 1, TimeUnixNano = time, FilteredAttributes = { Attributes("exemplar", full, brief) } }
            }
        };
        var metric = new Metric
        {
            Name = brief("metric.name"), Unit = brief("metric.unit"), Description = full("metric.description"),
            Metadata = { Attributes("metric.metadata", full, brief) },
            Sum = new Sum { AggregationTemporality = AggregationTemporality.Delta, IsMonotonic = true, DataPoints = { point } }
        };
        return new ExportMetricsServiceRequest
        {
            ResourceMetrics =
            {
                new ResourceMetrics
                {
                    Resource = ResourceOf("metrics", full, brief, null), SchemaUrl = brief("metrics.resource.schema"),
                    ScopeMetrics =
                    {
                        new ScopeMetrics
                        {
                            Scope = ScopeOf("metrics", full, brief), SchemaUrl = brief("metrics.scope.schema"),
                            Metrics = { metric }
                        }
                    }
                }
            }
        };
    }

    /// <summary>
    ///     The credential corpus of one transport. Each credential is built around a core that names its transport,
    ///     shape and place (ZQ, the transport's letter, two digits of shape, X, three digits of place) and that occurs
    ///     nowhere outside a credential, so a stored core is a stored credential and says which one.
    /// </summary>
    private sealed class Corpus(char transport)
    {
        private static readonly string[] s_shapes =
        [
            "Bearer value", "Basic value", "URL user information", "password=", "token=", "api_key=", "secret=",
            "connection-string password", "JSON Web Token", "sk-ant- token", "ghp_ token", "github_pat_ token",
            "glpat- token", "xoxb- token", "xoxp- token", "AKIA key id", "URL query"
        ];

        private readonly List<string> _places = [];

        internal int Planted => _places.Count;

        /// <summary>The Basic values and JWT payloads, which hold their core in base64.</summary>
        internal List<(string Name, string Text)> Encoded { get; } = [];

        internal string Describe(string core)
        {
            var shape = int.Parse(core.AsSpan(3, 2), CultureInfo.InvariantCulture);
            var place = int.Parse(core.AsSpan(6, 3), CultureInfo.InvariantCulture);
            return $"{s_shapes[shape]} planted in {_places[place]}";
        }

        private string CoreOf(int shape, int place) =>
            string.Create(CultureInfo.InvariantCulture, $"ZQ{transport}{shape:00}X{place:000}ZZZZZZZ");

        /// <summary>A short text for a key, a name or another small field: the place and one assignment.</summary>
        internal string Short(string place)
        {
            var index = _places.Count;
            _places.Add(place);
            return "(" + place + ") token=" + CoreOf(4, index);
        }

        /// <summary>Every shape of the corpus, one per line, for a value, a body, a name or a message.</summary>
        internal string Full(string place)
        {
            var index = _places.Count;
            _places.Add(place);
            string C(int shape) => CoreOf(shape, index);
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("svc:" + C(1)));
            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"sub\":\"" + C(8) + "\"}")).TrimEnd('=')
                .Replace('+', '-').Replace('/', '_');
            Encoded.Add(("Basic value planted in " + place, basic));
            Encoded.Add(("JSON Web Token planted in " + place, payload));
            return string.Join('\n',
                "(" + place + ")",
                "curl -H \"Authorization: Bearer " + C(0) + "\" https://api.example.test/v1/items",
                "Proxy-Authorization: Basic " + basic,
                "git clone https://builder:" + C(2) + "@git.example.test/nami.git",
                "level=error password=" + C(3) + " attempt=3",
                "retry with token=" + C(4) + " later",
                "api_key=" + C(5),
                "secret=" + C(6),
                "Server=db.example.test;User Id=svc;Password=" + C(7) + ";Encrypt=true",
                "session eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." + payload + "." + C(8) + C(8) + " expired",
                "key sk-" + "ant-api03-" + C(9) + C(9),
                "push with gh" + "p_" + C(10) + C(10),
                "push with github" + "_pat_" + C(11) + C(11),
                "push with gl" + "pat-" + C(12) + C(12),
                "posted as xox" + "b-" + C(13) + "-" + C(13),
                "posted as xox" + "p-" + C(14) + "-" + C(14),
                "signed with AK" + "IA" + C(15),
                "GET https://api.example.test/v1/items?signature=" + C(16) + "&page=2");
        }
    }
}
