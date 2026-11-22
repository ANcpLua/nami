using System.Globalization;
using System.Reflection;
using Google.Protobuf;
using Microsoft.Extensions.Time.Testing;
using Nami.Collector.Ingestion;
using Nami.Collector.Storage;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using LogRecord = OpenTelemetry.Proto.Logs.V1.LogRecord;
using static Nami.IntegrationTests.OtlpAttribute;

namespace Nami.IntegrationTests;

/// <summary>
///     The one credential stage (observer's rule, 16 Nov 2026): what <c>CredentialRedaction.Redact</c> removes and
///     what it leaves, and that the converter passes every string it keeps through it, whatever key or field carries
///     the string. Every credential here is made up in this file. The gate over the three transports, the stored
///     columns and the tools is <c>CredentialGateTests</c>.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class CredentialRedactionTests
{
    private const string R = "[REDACTED]";

    // Made-up values in the shapes the stage knows. Each is put together from parts, so that no line of this file
    // holds a whole token.
    private const string Opaque = "Zq7" + "mK2xWv9" + "LpT4nB8s";
    private const string Jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9" + "." + "eyJzdWIiOiJzeW50aGV0aWMifQ" + "." +
                               "c2lnbmF0dXJlLW5vdC1yZWFs";
    private const string Anthropic = "sk-" + "ant-api03-" + "SYNTHETICSYNTHETICSYNTHETIC00";
    private const string GitHub = "gh" + "p_" + "SYNTHETICsyntheticSYNTHETIC0123456789";
    private const string GitHubPat = "github" + "_pat_" + "11SYNTHETIC0synthetic00_SYNTHETICsyntheticSYNTHETIC0123456789";
    private const string GitLab = "gl" + "pat-" + "SYNTHETICsynthetic00";
    private const string SlackBot = "xox" + "b-" + "000000000000-000000000000-SYNTHETICsynthetic000000";
    private const string SlackUser = "xox" + "p-" + "000000000000-000000000000-SYNTHETICsynthetic000000";
    private const string Aws = "AK" + "IA" + "SYNTHETIC0000000";

    [TestMethod]
    [DataRow("curl -H \"Authorization: Bearer " + Opaque + "\" https://api.example.test/v1/items",
        "curl -H \"Authorization: Bearer " + R + "\" https://api.example.test/v1/items",
        DisplayName = "Bearer value in a command")]
    [DataRow("Authorization: Basic dXNlcjpwYXNzd29yZA==\nretry in 5 s", "Authorization: Basic " + R + "\nretry in 5 s",
        DisplayName = "Basic value after its header name")]
    [DataRow("proxy got Basic dXNlcjpwYXNzd29yZA== and refused", "proxy got Basic " + R + " and refused",
        DisplayName = "Basic value alone")]
    [DataRow("upstream sent Bearer " + Opaque + " twice", "upstream sent Bearer " + R + " twice",
        DisplayName = "Bearer value alone")]
    [DataRow("git clone https://builder:" + Opaque + "@git.example.test/nami.git && cd nami",
        "git clone https://" + R + "@git.example.test/nami.git && cd nami", DisplayName = "URL user information")]
    [DataRow("GET https://api.example.test/v1/items?api_key=" + Opaque + "&page=2 failed",
        "GET https://api.example.test/v1/items?" + R + " failed", DisplayName = "URL query")]
    [DataRow("connect mongodb://svc:" + Opaque + "@db1.example.test:27017,db2.example.test:27017/orders failed",
        "connect mongodb://" + R + " failed", DisplayName = "URL the stage cannot take apart")]
    [DataRow("level=error msg=\"login failed\" password=" + Opaque + " attempt=3",
        "level=error msg=\"login failed\" password=" + R + " attempt=3", DisplayName = "password= in a logfmt line")]
    [DataRow("retrying with token=" + Opaque + " after 2 s", "retrying with token=" + R + " after 2 s",
        DisplayName = "token= in a sentence")]
    [DataRow("OPENAI_API_KEY=" + Opaque + " python3 run.py --fast", "OPENAI_API_KEY=" + R + " python3 run.py --fast",
        DisplayName = "api_key= as an environment prefix of a command")]
    [DataRow("export DB_PASSWORD='" + Opaque + "' && ./migrate.sh", "export DB_PASSWORD='" + R + "' && ./migrate.sh",
        DisplayName = "quoted password in a longer variable name")]
    [DataRow("{\"client_secret\":\"" + Opaque + "\",\"grant_type\":\"client_credentials\"}",
        "{\"client_secret\":\"" + R + "\",\"grant_type\":\"client_credentials\"}", DisplayName = "secret in JSON text")]
    [DataRow("secret=" + Opaque, "secret=" + R, DisplayName = "secret= alone")]
    [DataRow("password: " + Opaque + " and more\nnext line", "password: " + R + "\nnext line",
        DisplayName = "value after a colon runs to the end of its line")]
    [DataRow("Server=db.example.test;Database=orders;User Id=svc;Password=two words " + Opaque + ";Encrypt=true",
        "Server=db.example.test;Database=orders;User Id=svc;Password=" + R + ";Encrypt=true",
        DisplayName = "connection-string password with a blank")]
    [DataRow("Server=db.example.test;Encrypt=true;Password=two words " + Opaque,
        "Server=db.example.test;Encrypt=true;Password=" + R, DisplayName = "connection-string password in the last pair")]
    [DataRow("session " + Jwt + " expired", "session " + R + " expired", DisplayName = "JSON Web Token")]
    [DataRow("key " + Anthropic + " rejected", "key " + R + " rejected", DisplayName = "sk-ant- token")]
    [DataRow("push with " + GitHub + " failed", "push with " + R + " failed", DisplayName = "ghp_ token")]
    [DataRow("push with " + GitHubPat + " failed", "push with " + R + " failed", DisplayName = "github_pat_ token")]
    [DataRow("push with " + GitLab + " failed", "push with " + R + " failed", DisplayName = "glpat- token")]
    [DataRow("posted as " + SlackBot + " to #ops", "posted as " + R + " to #ops", DisplayName = "xoxb- token")]
    [DataRow("posted as " + SlackUser + " to #ops", "posted as " + R + " to #ops", DisplayName = "xoxp- token")]
    [DataRow("signed with " + Aws + " in eu-central-1", "signed with " + R + " in eu-central-1",
        DisplayName = "AKIA access key id")]
    public void Redact_ACredential_IsReplaced_AndTheRestStays(string text, string expected)
    {
        var redacted = CredentialRedaction.Redact(text);

        Assert.AreEqual(expected, redacted);
        Assert.AreEqual(expected, CredentialRedaction.Redact(redacted), "a second pass changes nothing");
    }

    [TestMethod]
    [DataRow("SELECT id, total FROM orders WHERE status = 'open' AND created_at > $1 ORDER BY created_at DESC LIMIT 20",
        DisplayName = "SQL")]
    [DataRow("https://api.example.test/v1/orders/42", DisplayName = "URL without credentials")]
    [DataRow("fetched http://localhost:8080/health in 3 ms", DisplayName = "URL in a sentence")]
    [DataRow("https://opentelemetry.io/schemas/1.45.0", DisplayName = "schema URL")]
    [DataRow("/home/builder/src/nami/OtlpConverter.cs:412", DisplayName = "file path")]
    [DataRow("C:\\Users\\builder\\src\\app.cs", DisplayName = "Windows path")]
    [DataRow("GET /api/orders/{id}", DisplayName = "span name of a route")]
    [DataRow("claude_code.tool", DisplayName = "span name of Claude Code")]
    [DataRow("gen_ai.usage.input_tokens", DisplayName = "attribute name with tokens")]
    [DataRow("tokens=5", DisplayName = "token counter")]
    [DataRow("input_tokens: 1200, output_tokens: 87, cache_read_tokens: 0", DisplayName = "token counters in a line")]
    [DataRow("claude_code.tool_result", DisplayName = "Claude Code event name")]
    [DataRow("claude_code.api_request", DisplayName = "Claude Code event name of a request")]
    [DataRow("4185b11a38b2a139176df82c0e9f068a2af2e41c", DisplayName = "git SHA")]
    [DataRow("0f8fad5b-d9cb-469f-a165-70867728950e", DisplayName = "UUID")]
    [DataRow("2026-11-16T01:15:00.000Z", DisplayName = "timestamp")]
    [DataRow("10.0.0.5", DisplayName = "IP address")]
    [DataRow("at Nami.Collector.Ingestion.OtlpConverter.ConvertLogs(ExportLogsServiceRequest otlp)",
        DisplayName = "stack frame")]
    [DataRow("Basic authentication failed for the bearer token request", DisplayName = "prose about schemes")]
    [DataRow("exemplar.map.key and example.com.au and exception.type.name", DisplayName = "dotted names whose first part decodes like a JOSE header")]
    [DataRow("//button[text()='Submit']", DisplayName = "XPath step")]
    [DataRow("cache key=orders:42 hit after 3 ms", DisplayName = "an assignment to a name that is no credential")]
    [DataRow("ls -la /tmp && git status --short", DisplayName = "command without credentials")]
    [DataRow("{\"status\":\"ok\",\"items\":3,\"author\":\"Jane\",\"tokenizer\":\"bpe\"}", DisplayName = "JSON text")]
    [DataRow("rojo=00f067aa0ba902b7,congo=t61rcWkgMzE", DisplayName = "trace state")]
    [DataRow("{ \"max_tokens\": 1024,  \"model\": \"claude-haiku-5-5\", \"stream\": true }", DisplayName = "JSON text with its own spacing")]
    [DataRow("SELECT * FROM orders WHERE user_id = 42 AND total > 10", DisplayName = "SQL that compares a user column")]
    [DataRow("user=svc uid=1000 username=builder logged in", DisplayName = "user names and ids are no credentials")]
    [DataRow("ssh -p 2222 builder@host.example.test uptime", DisplayName = "-p as a port of ssh")]
    [DataRow("nc -l -p 8080", DisplayName = "-p as a port of another tool")]
    [DataRow("mysql -h db.example.test -P 3306 -u root -p orders", DisplayName = "mysql asking for its password, and its port")]
    [DataRow("curl -u svc https://api.example.test/v1/items", DisplayName = "curl with a user name alone")]
    [DataRow("curl --user-agent probe:1.0 https://api.example.test/v1/items", DisplayName = "curl's user agent")]
    [DataRow("run --max-tokens 100 --token-file /run/secrets/t --bypass cache --password", DisplayName = "flags that carry no credential")]
    [DataRow("", DisplayName = "empty text")]
    public void Redact_OrdinaryTelemetry_ComesThroughAsSent(string text)
    {
        Assert.AreSame(text, CredentialRedaction.Redact(text));
    }

    /// <summary>
    ///     A string that is a JSON document (Claude Code's tool_parameters and tool_input, a request body) is redacted
    ///     leaf by leaf and stays JSON: a credential ends where its JSON string ends, whatever is escaped inside, and
    ///     the value of a member whose name is a credential name goes whole.
    /// </summary>
    [TestMethod]
    [DataRow("{\"full_command\":\"curl -H \\\"Authorization: Bearer " + Opaque + "\\\" https://api.example.test/v1\",\"timeout\":120000}",
        "{\"full_command\":\"curl -H \\\"Authorization: Bearer " + R + "\\\" https://api.example.test/v1\",\"timeout\":120000}",
        DisplayName = "a command inside tool_parameters")]
    [DataRow("{\"headers\":{\"Authorization\":\"Bearer " + Opaque + "\",\"cookie\":\"sid=" + Opaque + "\"},\"password\":\"two words\",\"retries\":3}",
        "{\"headers\":{\"Authorization\":\"" + R + "\",\"cookie\":\"" + R + "\"},\"password\":\"" + R + "\",\"retries\":3}",
        DisplayName = "members named like credentials")]
    [DataRow("[{\"role\":\"user\",\"content\":\"my key is " + Anthropic + ", use it\"}]",
        "[{\"role\":\"user\",\"content\":\"my key is " + R + ", use it\"}]", DisplayName = "a token inside a message")]
    public void Redact_JsonText_IsRedactedLeafByLeaf_AndStaysJson(string text, string expected)
    {
        var redacted = CredentialRedaction.Redact(text);

        Assert.AreEqual(expected, redacted);
        using var parsed = System.Text.Json.JsonDocument.Parse(redacted);
        Assert.AreEqual(expected, CredentialRedaction.Redact(redacted), "a second pass changes nothing");
    }

    [TestMethod]
    [DataRow("http.request.header.authorization", true)]
    [DataRow("db.password", true)]
    [DataRow("AWS_SECRET_ACCESS_KEY", true)]
    [DataRow("http.response.header.set-cookie", true)]
    [DataRow("access_token", true)]
    [DataRow("k8s.pod.uid", false)]
    [DataRow("user.id", false)]
    [DataRow("gen_ai.usage.input_tokens", false)]
    [DataRow("messaging.kafka.message.key", false)]
    [DataRow("session.id", false)]
    [DataRow("max_tokens", false)]
    [DataRow("tool_name", false)]
    public void NamesACredential_IsTrueForANameThatEndsInACredentialName(string name, bool expected)
    {
        Assert.AreEqual(expected, CredentialRedaction.NamesACredential(name));
    }

    /// <summary>
    ///     A credential on a command line: the value of a long flag whose name is or ends in a credential name, with
    ///     '=' or a blank before it; the password of <c>curl -u</c>; the value attached to <c>-p</c> of mysql,
    ///     mariadb and mysqldump; the value of <c>sshpass -p</c>. The rest of the command stays as sent.
    /// </summary>
    [TestMethod]
    [DataRow("psql --password " + Opaque + " -h db.example.test", "psql --password " + R + " -h db.example.test", DisplayName = "--password and a blank")]
    [DataRow("tool --PASSWORD " + Opaque, "tool --PASSWORD " + R, DisplayName = "upper case")]
    [DataRow("tool --passwd " + Opaque + " run", "tool --passwd " + R + " run", DisplayName = "--passwd")]
    [DataRow("tool --pass " + Opaque + " run", "tool --pass " + R + " run", DisplayName = "--pass and a blank")]
    [DataRow("tool --pass=" + Opaque + " run", "tool --pass=" + R + " run", DisplayName = "--pass=")]
    [DataRow("deploy --token " + Opaque + " --env prod", "deploy --token " + R + " --env prod", DisplayName = "--token")]
    [DataRow("deploy --api-key " + Opaque, "deploy --api-key " + R, DisplayName = "--api-key")]
    [DataRow("deploy --apikey " + Opaque, "deploy --apikey " + R, DisplayName = "--apikey")]
    [DataRow("deploy --secret " + Opaque, "deploy --secret " + R, DisplayName = "--secret")]
    [DataRow("login --client-secret " + Opaque + " --tenant t1", "login --client-secret " + R + " --tenant t1", DisplayName = "--client-secret")]
    [DataRow("migrate --db-password \"two words " + Opaque + "\" up", "migrate --db-password \"" + R + "\" up", DisplayName = "a quoted value of a longer flag")]
    [DataRow("--password " + Opaque, "--password " + R, DisplayName = "the flag at the start of the text")]
    [DataRow("curl -u svc:" + Opaque + " https://api.example.test/v1/items", "curl -u svc:" + R + " https://api.example.test/v1/items", DisplayName = "curl -u")]
    [DataRow("curl -s --user svc:" + Opaque + " https://api.example.test/", "curl -s --user svc:" + R + " https://api.example.test/", DisplayName = "curl --user")]
    [DataRow("curl --user=svc:" + Opaque + " https://api.example.test/", "curl --user=svc:" + R + " https://api.example.test/", DisplayName = "curl --user=")]
    [DataRow("curl -u \"svc:" + Opaque + "\" https://api.example.test/", "curl -u \"svc:" + R + "\" https://api.example.test/", DisplayName = "curl -u with quotes")]
    [DataRow("mysql -h db.example.test -u root -p" + Opaque + " orders", "mysql -h db.example.test -u root -p" + R + " orders", DisplayName = "mysql -p with its value attached")]
    [DataRow("mariadb -u root -p" + Opaque, "mariadb -u root -p" + R, DisplayName = "mariadb -p")]
    [DataRow("mysqldump -u root -p'" + Opaque + "' orders > dump.sql", "mysqldump -u root -p'" + R + "' orders > dump.sql", DisplayName = "mysqldump -p with quotes")]
    [DataRow("sshpass -p " + Opaque + " ssh builder@host.example.test", "sshpass -p " + R + " ssh builder@host.example.test", DisplayName = "sshpass -p")]
    public void Redact_ACredentialOnACommandLine_IsReplaced_AndTheRestStays(string text, string expected)
    {
        var redacted = CredentialRedaction.Redact(text);

        Assert.AreEqual(expected, redacted);
        Assert.AreEqual(expected, CredentialRedaction.Redact(redacted), "a second pass changes nothing");
    }

    /// <summary>
    ///     Under a credential name a number goes like a string (a PIN, a numeric password), bytes cannot be read for
    ///     what they hold and are dropped and counted, and in a list of arguments the item after a credential flag
    ///     goes whole. The same holds inside a JSON string. Other numbers, bytes and arguments stay.
    /// </summary>
    [TestMethod]
    public void Convert_NumbersBytesAndArgumentsThatAreCredentials_AreRemoved()
    {
        static AnyValue S(string text) => new() { StringValue = text };
        var span = new Span
        {
            TraceId = ByteString.CopyFrom(Convert.FromHexString("0af7651916cd43dd8448eb211c80319c")),
            SpanId = ByteString.CopyFrom(Convert.FromHexString("b7ad6b7169203331")),
            Name = "login", StartTimeUnixNano = 1_794_745_800_000_000_000, EndTimeUnixNano = 1_794_745_801_000_000_000,
            Attributes =
            {
                new KeyValue { Key = "db.password", Value = new AnyValue { IntValue = 20261116 } },
                new KeyValue { Key = "auth.session_token", Value = new AnyValue { DoubleValue = 20261116.5 } },
                new KeyValue { Key = "http.cookie", Value = new AnyValue { BytesValue = ByteString.CopyFromUtf8(Opaque) } },
                new KeyValue { Key = "acme.retries", Value = new AnyValue { IntValue = 3 } },
                new KeyValue { Key = "acme.payload", Value = new AnyValue { BytesValue = ByteString.CopyFromUtf8("plain") } },
                new KeyValue
                {
                    Key = "process.command_args",
                    Value = new AnyValue { ArrayValue = new ArrayValue { Values = { S("mysql"), S("--password"), S(Opaque), S("-h"), S("db.example.test") } } }
                },
                Text("tool_input", "{\"pin_token\":20261116,\"retries\":3,\"args\":[\"deploy\",\"--api-key\",\"" + Opaque + "\",\"prod\"]}")
            }
        };
        var batch = OtlpConverter.ConvertTraceRequest(new ExportTraceServiceRequest
        {
            ResourceSpans = { new ResourceSpans { ScopeSpans = { new ScopeSpans { Spans = { span } } } } }
        });

        var stored = Assert.ContainsSingle(batch.Spans);
        var attributes = stored.Attributes;
        Assert.AreEqual(R, attributes["db.password"].AsString());
        Assert.AreEqual(R, attributes["auth.session_token"].AsString());
        Assert.IsFalse(attributes.ContainsKey("http.cookie"), "bytes under a credential name are dropped");
        Assert.AreEqual(1U, stored.DroppedAttributesCount, "and counted");
        Assert.AreEqual(3L, attributes["acme.retries"].AsInt64());
        Assert.AreSequenceEqual("plain"u8.ToArray(), attributes["acme.payload"].AsBytes()!);
        string?[] arguments = [.. attributes["process.command_args"].AsArray()!.Select(static item => item.AsString())];
        Assert.AreSequenceEqual(new[] { "mysql", "--password", R, "-h", "db.example.test" }, arguments);
        Assert.AreEqual("{\"pin_token\":\"" + R + "\",\"retries\":3,\"args\":[\"deploy\",\"--api-key\",\"" + R + "\",\"prod\"]}",
            attributes["tool_input"].AsString());
    }

    /// <summary>
    ///     An attribute whose own name is a credential name loses its whole value, as a string and as a list of
    ///     strings (a captured header), and a value of no credential shape goes with it. Other keys keep theirs.
    /// </summary>
    [TestMethod]
    public void Convert_AnAttributeNamedLikeACredential_LosesItsValue()
    {
        var span = new Span
        {
            TraceId = ByteString.CopyFrom(Convert.FromHexString("0af7651916cd43dd8448eb211c80319c")),
            SpanId = ByteString.CopyFrom(Convert.FromHexString("b7ad6b7169203331")),
            Name = "GET /orders", StartTimeUnixNano = 1_794_745_800_000_000_000, EndTimeUnixNano = 1_794_745_801_000_000_000,
            Attributes =
            {
                new KeyValue
                {
                    Key = "http.request.header.authorization",
                    Value = new AnyValue { ArrayValue = new ArrayValue { Values = { new AnyValue { StringValue = "plainvalue" } } } }
                },
                Text("db.password", "plainvalue"), Text("k8s.pod.uid", "0f8fad5b-d9cb-469f-a165-70867728950e"),
                Text("gen_ai.request.model", "plainvalue")
            }
        };
        var batch = OtlpConverter.ConvertTraceRequest(new ExportTraceServiceRequest
        {
            ResourceSpans = { new ResourceSpans { ScopeSpans = { new ScopeSpans { Spans = { span } } } } }
        });

        var attributes = Assert.ContainsSingle(batch.Spans).Attributes;
        Assert.AreEqual(R, Assert.ContainsSingle(attributes["http.request.header.authorization"].AsArray()!).AsString());
        Assert.AreEqual(R, attributes["db.password"].AsString());
        Assert.AreEqual("0f8fad5b-d9cb-469f-a165-70867728950e", attributes["k8s.pod.uid"].AsString());
        Assert.AreEqual("plainvalue", attributes["gen_ai.request.model"].AsString());
    }

    /// <summary>
    ///     The false positives this stage accepts, pinned so that a change to any of them is seen: a URL loses its
    ///     query and fragment whatever they hold, with the replacement where each stood; a value after the word
    ///     token or pwd and a separator goes, which hits a parser message and the shell's working directory.
    /// </summary>
    [TestMethod]
    [DataRow("see https://docs.example.test/guide?lang=en#install", "see https://docs.example.test/guide?" + R + "#" + R,
        DisplayName = "query and fragment without a secret")]
    [DataRow("Unexpected token: } at line 3", "Unexpected token: " + R, DisplayName = "parser message")]
    [DataRow("PWD=/home/builder", "PWD=" + R, DisplayName = "the shell's working directory")]
    public void Redact_AcceptedFalsePositives_AreTheseOnes(string text, string expected)
    {
        Assert.AreEqual(expected, CredentialRedaction.Redact(text));
    }

    /// <summary>
    ///     Every text field and every attribute carrier of a trace, a log and a metric export holds its own made-up
    ///     token. After conversion and mapping to storage rows no string of any row holds one, and each field still
    ///     holds the words around it. A field the converter reads past the stage fails here by name.
    /// </summary>
    [TestMethod]
    public void Convert_EveryStringOfAnExport_PassesTheStage()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-15T12:35:00Z", CultureInfo.InvariantCulture));
        var planted = new List<string>();

        var traces = OtlpConverter.ConvertTraceRequest(Traces(Plant));
        Assert.AreEqual(0, traces.RejectedSpans, traces.RejectionMessage);
        var logs = OtlpConverter.ConvertLogs(Logs(Plant), clock);
        Assert.AreEqual(0, logs.RejectedLogRecords, logs.RejectionMessage);
        var metrics = OtlpConverter.ConvertMetrics(Metrics(Plant));
        Assert.AreEqual(0, metrics.RejectedDataPoints, metrics.RejectionMessage);
        var metricRows = MetricStorageMapper.ToStorageRows(metrics);
        var rows = IngestionStorageMapper.ToSpanStorageRows(traces).Cast<object>()
            .Concat(IngestionStorageMapper.ToLogStorageRows(logs))
            .Concat(metricRows.Series).Concat(metricRows.Observations).ToArray();
        var stored = rows.SelectMany(static row => row.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(static property => property.PropertyType == typeof(string))
            .Select(property => (Column: row.GetType().Name + "." + property.Name,
                Text: (string?)property.GetValue(row)))).Where(static cell => cell.Text is not null).ToArray();

        Assert.IsGreaterThan(40, planted.Count);
        foreach (var place in planted)
        {
            var leaked = stored.Where(cell => cell.Text!.Contains(Token(place), StringComparison.Ordinal))
                .Select(static cell => cell.Column).Distinct().ToArray();
            Assert.IsEmpty(leaked, $"the token planted in {place} is stored in {string.Join(", ", leaked)}");
        }

        // The words around each token stay, in a key as in a value.
        foreach (var place in planted)
        {
            Assert.IsTrue(stored.Any(cell => cell.Text!.Contains("(" + place + ")", StringComparison.Ordinal)),
                $"the text around the token planted in {place} is stored nowhere; its carrier holds: " +
                string.Join(" | ", stored.Where(cell => cell.Text!.Contains("(" + place[..place.IndexOf('.', StringComparison.Ordinal)], StringComparison.Ordinal))
                    .Select(static cell => cell.Column + "=" + cell.Text![..Math.Min(cell.Text!.Length, 1500)]).Take(2)));
        }

        string Plant(string place)
        {
            planted.Add(place);
            return "(" + place + ") token=" + Token(place);
        }
    }

    // A made-up token per place, in the ghp_ shape so that the assignment rule and the prefix rule both see it.
    private static string Token(string place)
    {
        var body = string.Concat(place.Where(char.IsAsciiLetterOrDigit));
        return "gh" + "p_" + (body + new string('0', 36))[..36];
    }

    // The attribute shapes of one carrier: a string, an array item, a map member (its value and its key), exception
    // text, a key whose own text holds a token, and a key the registry does not know.
    private static IEnumerable<KeyValue> Attributes(string carrier, Func<string, string> plant)
    {
        yield return Text("http.route", plant(carrier + ".string"));
        yield return new KeyValue
        {
            Key = "http.request.header.x-trace",
            Value = new AnyValue { ArrayValue = new ArrayValue { Values = { new AnyValue { StringValue = plant(carrier + ".array") } } } }
        };
        yield return new KeyValue
        {
            Key = "feature_flag.result.value",
            Value = new AnyValue
            {
                KvlistValue = new KeyValueList
                {
                    Values = { Text("error.type", plant(carrier + ".map")), Text(plant(carrier + ".map.key"), "ordinary") }
                }
            }
        };
        yield return Text("exception.message", plant(carrier + ".exception"));
        yield return Text("k8s.pod.label." + plant(carrier + ".key"), "ordinary");
        yield return Text("acme.unknown", plant(carrier + ".unknown"));
    }

    private static Resource ResourceOf(string signal, Func<string, string> plant)
    {
        var resource = new Resource
        {
            Attributes = { Text("service.name", plant(signal + ".service")), Attributes(signal + ".resource", plant) },
            EntityRefs =
            {
                new EntityRef
                {
                    Type = plant(signal + ".entity.type"), SchemaUrl = plant(signal + ".entity.schema"),
                    IdKeys = { "service.name" }
                }
            }
        };
        return resource;
    }

    private static InstrumentationScope ScopeOf(string signal, Func<string, string> plant) => new()
    {
        Name = plant(signal + ".scope.name"), Version = plant(signal + ".scope.version"),
        Attributes = { Attributes(signal + ".scope", plant) }
    };

    private static ExportTraceServiceRequest Traces(Func<string, string> plant)
    {
        var span = new Span
        {
            TraceId = ByteString.CopyFrom(Convert.FromHexString("0af7651916cd43dd8448eb211c80319c")),
            SpanId = ByteString.CopyFrom(Convert.FromHexString("b7ad6b7169203331")),
            Name = plant("span.name"), TraceState = plant("span.tracestate"),
            StartTimeUnixNano = 1_794_745_800_000_000_000, EndTimeUnixNano = 1_794_745_801_000_000_000,
            Status = new Status { Code = Status.Types.StatusCode.Error, Message = plant("span.status") },
            Attributes = { Attributes("span", plant) },
            Events =
            {
                new Span.Types.Event
                {
                    Name = plant("event.name"), TimeUnixNano = 1_794_745_800_500_000_000,
                    Attributes = { Attributes("event", plant) }
                }
            },
            Links =
            {
                new Span.Types.Link
                {
                    TraceId = ByteString.CopyFrom(Convert.FromHexString("1af7651916cd43dd8448eb211c80319c")),
                    SpanId = ByteString.CopyFrom(Convert.FromHexString("c7ad6b7169203331")),
                    TraceState = plant("link.tracestate"), Attributes = { Attributes("link", plant) }
                }
            }
        };
        return new ExportTraceServiceRequest
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
                    Resource = ResourceOf("traces", plant), SchemaUrl = plant("traces.resource.schema"),
                    ScopeSpans =
                    {
                        new ScopeSpans
                        {
                            Scope = ScopeOf("traces", plant), SchemaUrl = plant("traces.scope.schema"), Spans = { span }
                        }
                    }
                }
            }
        };
    }

    private static ExportLogsServiceRequest Logs(Func<string, string> plant)
    {
        var text = new LogRecord
        {
            TimeUnixNano = 1_794_745_800_000_000_000, SeverityText = plant("log.severity"),
            EventName = plant("log.event"), Body = new AnyValue { StringValue = plant("log.body") },
            Attributes = { Attributes("log", plant) }
        };
        var structured = new LogRecord
        {
            TimeUnixNano = 1_794_745_800_000_000_001,
            Body = new AnyValue
            {
                ArrayValue = new ArrayValue
                {
                    Values =
                    {
                        new AnyValue { StringValue = plant("log.body.array") },
                        new AnyValue { KvlistValue = new KeyValueList { Values = { Text("error.type", plant("log.body.map")) } } }
                    }
                }
            },
            // A producer that still names its events in the deprecated attribute.
            Attributes = { Text("event.name", plant("log.event.attribute")) }
        };
        return new ExportLogsServiceRequest
        {
            ResourceLogs =
            {
                new ResourceLogs
                {
                    Resource = ResourceOf("logs", plant), SchemaUrl = plant("logs.resource.schema"),
                    ScopeLogs =
                    {
                        new ScopeLogs
                        {
                            Scope = ScopeOf("logs", plant), SchemaUrl = plant("logs.scope.schema"),
                            LogRecords = { text, structured }
                        }
                    }
                }
            }
        };
    }

    private static ExportMetricsServiceRequest Metrics(Func<string, string> plant)
    {
        var point = new NumberDataPoint
        {
            AsInt = 7, StartTimeUnixNano = 1_794_745_800_000_000_000, TimeUnixNano = 1_794_745_801_000_000_000,
            Attributes = { Attributes("point", plant) },
            Exemplars =
            {
                new Exemplar
                {
                    AsInt = 1, TimeUnixNano = 1_794_745_800_500_000_000,
                    FilteredAttributes = { Attributes("exemplar", plant) }
                }
            }
        };
        var metric = new Metric
        {
            Name = plant("metric.name"), Unit = plant("metric.unit"), Description = plant("metric.description"),
            Metadata = { Attributes("metric.metadata", plant) },
            Sum = new Sum
            {
                AggregationTemporality = AggregationTemporality.Delta, IsMonotonic = true, DataPoints = { point }
            }
        };
        return new ExportMetricsServiceRequest
        {
            ResourceMetrics =
            {
                new ResourceMetrics
                {
                    Resource = ResourceOf("metrics", plant), SchemaUrl = plant("metrics.resource.schema"),
                    ScopeMetrics =
                    {
                        new ScopeMetrics
                        {
                            Scope = ScopeOf("metrics", plant), SchemaUrl = plant("metrics.scope.schema"),
                            Metrics = { metric }
                        }
                    }
                }
            }
        };
    }
}
