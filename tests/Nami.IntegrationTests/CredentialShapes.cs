using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Nami.Collector.Ingestion;
using OpenTelemetry.Proto.Common.V1;

namespace Nami.IntegrationTests;

/// <summary>
///     A corpus of made-up credential shapes, first collected by the evaluation of 16 Nov 2026. Every credential is
///     built around a core (ZQ, a transport letter, three digits of shape, X, three digits of place, ZZZZZZ) that
///     occurs nowhere else, so a core that is stored or answered is a credential that survived, and it names its
///     shape. Nothing here is a real secret. Listed = a shape the review brief named; the others are extras.
/// </summary>
internal static class CredentialShapes
{
    internal sealed record Plain(int Shape, bool Listed, string Name, string Text);
    internal sealed record Keyed(int Shape, bool Listed, string Name, KeyValue Attribute, bool TopLevelOnly = false);
    internal sealed record Set(List<Plain> Plain, List<Plain> Json, List<Keyed> Keyed, List<(string Name, string Text)> Needles);

    private static readonly List<string> s_names = [];

    internal static string NameOf(int shape) => shape switch
    {
        999 => "short token= text",
        998 => "bytes body",
        _ => shape < s_names.Count ? s_names[shape] : "shape " + shape
    };

    internal static string Core(char transport, int shape, int place) =>
        string.Create(CultureInfo.InvariantCulture, $"ZQ{transport}{shape:000}X{place:000}ZZZZZZ");

    private static string B64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
    private static string B64Url(string text) => B64(text).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static Set Build(char transport, int place)
    {
        var plain = new List<Plain>();
        var json = new List<Plain>();
        var keyed = new List<Keyed>();
        var needles = new List<(string, string)>();
        var next = 0;

        int Shape(string name)
        {
            if (next == s_names.Count) lock (s_names) { if (next == s_names.Count) s_names.Add(name); }
            return next++;
        }

        void P(bool listed, string name, Func<string, string> make)
        {
            var shape = Shape(name);
            plain.Add(new Plain(shape, listed, name, make(Core(transport, shape, place))));
        }

        // A shape whose credential is held encoded: the needle is what must not be stored.
        void PE(bool listed, string name, Func<string, (string Text, string Needle)> make)
        {
            var shape = Shape(name);
            var (text, needle) = make(Core(transport, shape, place));
            needles.Add((name, needle));
            plain.Add(new Plain(shape, listed, name, text));
        }

        void J(bool listed, string name, Func<string, string> make)
        {
            var shape = Shape(name);
            json.Add(new Plain(shape, listed, name, make(Core(transport, shape, place))));
        }

        void K(bool listed, string name, Func<string, KeyValue> make, bool topLevelOnly = false)
        {
            var shape = Shape(name);
            keyed.Add(new Keyed(shape, listed, name, make(Core(transport, shape, place)), topLevelOnly));
        }

        const string hs256 = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9";
        string Payload(string c) => B64Url("{\"sub\":\"" + c + "\"}");

        // ---- JSON Web Tokens
        P(true, "JWT in a sentence", c => "session " + hs256 + "." + Payload(c) + "." + c + c + " expired");
        P(true, "JWT alg none, empty signature", c => "id " + "eyJhbGciOiJub25lIn0" + "." + c + c + ".");
        P(true, "JWT in a URL path", c => "GET https://app.example.test/verify/" + hs256 + "." + c + c + "." + c + " 200");
        P(false, "JWE, five parts", c => "jwe eyJhbGciOiJSU0EtT0FFUCIsImVuYyI6IkEyNTZHQ00ifQ" + ".aaaa.bbbb." + c + "cc." + c + "dd");
        P(true, "JWT after id_token=", c => "id_token=" + hs256 + "." + c + c + "." + c);
        // ---- provider tokens
        P(true, "sk-ant-api03 key", c => "key sk-" + "ant-api03-" + c + "_" + c + "-" + c + "AA rejected");
        P(true, "sk-ant-oat01 token", c => "oauth sk-" + "ant-oat01-" + c + c);
        P(true, "ghp_ token", c => "push with gh" + "p_" + c + c + "0000");
        P(true, "gho_ token", c => "push with gh" + "o_" + c + c + "0000");
        P(true, "github_pat_ token", c => "push with github" + "_pat_" + "11" + c + "_" + c + c);
        P(true, "sk-ant key glued to a name with an underscore", c => "ANTHROPIC_sk-" + "ant-api03-" + c + c);
        // ---- AWS key pairs
        P(true, "AWS pair as environment assignments", c => "AWS_ACCESS_KEY_ID=AK" + "IA" + c + " AWS_SECRET_ACCESS_KEY=" + c + "/Ab+" + c + "Cd aws s3 ls");
        P(true, "AWS credentials file", c => "[default]\naws_access_key_id = AK" + "IA" + c + "\naws_secret_access_key = " + c + "/Ab+" + c + "Cd");
        P(true, "AWS pair, bare, comma separated (credentials.csv)", c => "deploy,AK" + "IA" + c + "," + c.ToLowerInvariant() + "/Ab+" + c + "Cd");
        P(true, "aws configure set aws_secret_access_key <value>", c => "aws configure set aws_secret_access_key " + c + "/Ab+" + c);
        P(true, "AWS session token assignment", c => "AWS_SESSION_TOKEN=" + c + c + "//////////" + c);
        // ---- PEM private keys
        P(true, "PEM RSA private key block, lines", c => "-----BEGIN RSA PRIVATE KEY-----\nMIIEpAIBAAKCAQEA" + c + "\n" + c + c + "==\n-----END RSA PRIVATE KEY-----");
        P(true, "PEM OPENSSH private key, escaped newlines in a command", c => "echo \"-----BEGIN OPENSSH PRIVATE KEY-----\\nb3BlbnNzaC1rZXktdjEA" + c + "\\n-----END OPENSSH PRIVATE KEY-----\" > id_ed25519");
        P(true, "PEM PKCS8 private key block on one line", c => "-----BEGIN PRIVATE KEY----- MIIEvQIBADANBg" + c + c + " -----END PRIVATE KEY-----");
        P(true, "PEM EC private key in a stack of text", c => "loaded key:\n-----BEGIN EC PRIVATE KEY-----\nMHcCAQEEI" + c + "\n-----END EC PRIVATE KEY-----\nok");
        // ---- Authorization headers
        P(true, "Authorization: Bearer", c => "Authorization: Bearer " + c);
        P(true, "authorization:bearer, lower case, no blank", c => "authorization:bearer " + c);
        PE(true, "Authorization: Basic", c => ("Authorization: Basic " + B64("svc:" + c), B64("svc:" + c)));
        PE(true, "Proxy-Authorization: Basic", c => ("Proxy-Authorization: Basic " + B64("svc:" + c), B64("svc:" + c)));
        P(true, "curl -H 'Authorization: Bearer' single quotes", c => "curl -H 'Authorization: Bearer " + c + "' https://api.example.test/v1");
        PE(true, "curl -H \"Authorization: Basic\"", c => ("curl -H \"Authorization: Basic " + B64("svc:" + c) + "\" https://api.example.test/v1", B64("svc:" + c)));
        P(true, "curl --header=\"Authorization: Bearer\"", c => "curl --header=\"Authorization: Bearer " + c + "\" https://api.example.test/v1");
        P(true, "curl -H \"x-api-key: value\"", c => "curl -H \"x-api-key: " + c + "\" -H \"anthropic-version: 2023-06-01\" https://api.example.test/v1/messages");
        P(false, "curl -H \"Cookie: session=value\"", c => "curl -H \"Cookie: session=" + c + "\" https://app.example.test/");
        P(false, "Set-Cookie: sid=value header line", c => "Set-Cookie: sid=" + c + "; HttpOnly; Secure");
        P(true, "Authorization: Token value", c => "Authorization: Token " + c);
        P(true, "Bearer value alone with a URL-encoded part", c => "upstream sent Bearer abc%2F" + c + " twice");
        P(true, "Authorization=Bearer%20value", c => "Authorization=Bearer%20" + c);
        P(true, "HTTP request dump with CRLF", c => "GET /v1 HTTP/1.1\r\nHost: api.example.test\r\nAuthorization: Bearer " + c + "\r\nX-Request-Id: 1");
        P(true, "headers dict, python repr", c => "headers={'Authorization': 'Bearer " + c + "', 'Accept': '*/*'}");
        P(true, "headers object fragment, double quotes", c => "headers: {\"Authorization\": \"Bearer " + c + "\", \"Accept\": \"*/*\"}");
        P(true, "Authorization: Bearer with a tab", c => "Authorization: Bearer\t" + c);
        P(true, "bearer value alone, lower case scheme", c => "got bearer " + c + " back");
        P(true, "X-Amz-Security-Token header", c => "X-Amz-Security-Token: " + c);
        P(true, "Authorization: AWS4-HMAC-SHA256 with Signature", c => "Authorization: AWS4-HMAC-SHA256 Credential=AK" + "IA" + c + "/20261116/eu-central-1/s3/aws4_request, SignedHeaders=host, Signature=" + c.ToLowerInvariant());
        // ---- basic auth in URLs
        P(true, "URL user:password", c => "git clone https://builder:" + c + "@git.example.test/nami.git");
        P(true, "URL token as the user", c => "git clone https://" + c + "@github.example.test/org/repo.git");
        P(true, "postgres URL with password and query", c => "DATABASE_URL=postgres://svc:" + c + "@db.example.test:5432/orders?sslmode=require");
        P(true, "redis URL with password only", c => "redis://:" + c + "@cache.example.test:6379/0");
        P(true, "mongodb+srv URL", c => "mongodb+srv://svc:" + c + "@cluster0.example.test/orders?retryWrites=true");
        P(true, "URL with a quote inside the password", c => "open \"https://svc:pa'" + c + "@host.example.test/x\"");
        P(true, "URL with %40 in the password", c => "https://svc:p%40ss" + c + "@host.example.test/");
        P(true, "URL in upper case", c => "HTTPS://SVC:" + c + "@HOST.EXAMPLE.TEST/");
        P(true, "URL with user:password and an IPv6 host", c => "http://svc:" + c + "@[::1]:8080/x");
        P(true, "network-path reference //user:password@host", c => "src=//svc:" + c + "@cdn.example.test/a.js");
        P(true, "URL-encoded URL with user:password", c => "redirect_uri=https%3A%2F%2Fsvc%3A" + c + "%40host.example.test%2Fcb");
        P(true, "URL in markdown link", c => "see [repo](https://svc:" + c + "@git.example.test/r) for details");
        P(true, "scp-like user:password@host without a scheme", c => "rsync svc:" + c + "@host.example.test:/data .");
        // ---- tokens in queries
        P(true, "absolute URL, URL-encoded token in the query", c => "GET https://api.example.test/cb?access_token=ab%2B" + c + "%3D%3D&state=1");
        P(true, "relative URL, access_token= URL-encoded", c => "GET /oauth/cb?access_token=ab%2B" + c + "%3D&state=1");
        P(true, "relative URL, X-Amz-Signature=", c => "GET /dl/file?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Signature=" + c.ToLowerInvariant() + "&X-Amz-Expires=60");
        P(true, "relative URL, sig= (SAS)", c => "GET /container/blob?sv=2024-01-01&sig=" + c + "%3D&se=2026-11-17");
        P(false, "relative URL, code= (OAuth code)", c => "GET /oauth/cb?code=" + c + "&state=xyz");
        P(false, "relative URL, key= (API key)", c => "GET /maps/api/geocode/json?address=x&key=" + c);
        P(true, "relative URL, token= after a fragment", c => "GET /app#access_token=" + c + "&token_type=bearer");
        P(true, "URL-encoded assignment token%3D", c => "body=token%3D" + c + "%26x%3D1");
        P(true, "form body client_secret and refresh_token", c => "grant_type=refresh_token&client_secret=" + c + "&refresh_token=" + c);
        P(false, "secret in a URL path (webhook)", c => "POST https://hooks.example.test/services/T000/B000/" + c);
        P(false, "bot token in a URL path", c => "https://api.example.test/bot123456:" + c + "/sendMessage");
        // ---- connection strings
        P(true, "connection string Password=", c => "Server=db.example.test;Database=o;User Id=svc;Password=" + c + ";Encrypt=true");
        P(true, "connection string Pwd=", c => "Driver={ODBC Driver 18 for SQL Server};Server=db;Uid=svc;Pwd=" + c + ";");
        P(true, "connection string pwd= lower case, last pair", c => "Data Source=db.example.test;uid=svc;pwd=" + c);
        P(true, "Pwd= alone", c => "Pwd=" + c);
        P(true, "libpq keyword/value password=", c => "host=db.example.test user=svc password=" + c + " dbname=orders");
        P(true, "Azure storage AccountKey=", c => "DefaultEndpointsProtocol=https;AccountName=acct;AccountKey=" + c + "==;EndpointSuffix=core.windows.net");
        P(true, "service bus SharedAccessKey=", c => "Endpoint=sb://ns.example.test/;SharedAccessKeyName=root;SharedAccessKey=" + c + "=");
        P(true, "jdbc sqlserver ;password=", c => "jdbc:sqlserver://db.example.test:1433;databaseName=o;user=sa;password=" + c + ";");
        P(true, "jdbc mysql ?password=", c => "jdbc:mysql://db.example.test/o?user=svc&password=" + c);
        P(true, "StackExchange.Redis ,password=", c => "cache.example.test:6380,password=" + c + ",ssl=True");
        P(true, "connection string in an XML attribute", c => "<add name=\"db\" connectionString=\"Server=db;User Id=svc;Password=" + c + "\" />");
        P(true, "connection string Password with braces", c => "Server=db;Uid=svc;Pwd={pa;ss" + c + "};Encrypt=yes");
        P(true, "connection string with a blank before =", c => "Server=db;User Id=svc;Password = " + c + ";");
        P(false, "XML element <password>", c => "<password>" + c + "</password>");
        P(false, "XML appSettings key ApiKey value", c => "<add key=\"ApiKey\" value=\"" + c + "\" />");
        // ---- .env and export lines
        P(true, ".env X_TOKEN=", c => "STRIPE_TOKEN=" + c);
        P(true, "export X_TOKEN=", c => "export X_TOKEN=" + c);
        P(true, "export X_TOKEN=\"quoted\"", c => "export GITHUB_TOKEN=\"" + c + "\"");
        P(true, ".env DB_PASS=", c => "DB_PASS=" + c);
        P(true, ".env MYSQL_PWD=", c => "MYSQL_PWD=" + c);
        P(true, ".env SECRET_KEY_BASE=", c => "SECRET_KEY_BASE=" + c);
        P(true, ".env X_TOKEN_V2= (name goes on after the credential word)", c => "API_TOKEN_V2=" + c);
        P(false, ".env GPG_PASSPHRASE=", c => "GPG_PASSPHRASE=" + c);
        P(false, ".env ENCRYPTION_KEY=", c => "ENCRYPTION_KEY=" + c);
        P(false, ".env X_CREDENTIALS=", c => "REGISTRY_CREDENTIALS=svc:" + c);
        P(true, "docker run -e X_TOKEN=", c => "docker run -e API_TOKEN=" + c + " img");
        P(true, "PowerShell $env:X_TOKEN = 'value'", c => "$env:X_TOKEN = '" + c + "'");
        P(true, "set X_TOKEN=value (cmd)", c => "set X_TOKEN=" + c);
        P(true, "YAML x_token: value", c => "  x_token: " + c);
        P(true, "k8s env name/value on two lines", c => "- name: X_TOKEN\n  value: " + c);
        P(true, "export with a blank after the equals sign in quotes", c => "export X_TOKEN=\" " + c + "\"");
        P(true, "env X_TOKEN=value command", c => "env X_TOKEN=" + c + " ./run.sh");
        P(true, ".env line with a comment", c => "X_TOKEN=" + c + " # rotate monthly");
        P(true, "os.environ[\"API_KEY\"] = \"value\"", c => "os.environ[\"API_KEY\"] = \"" + c + "\"");
        P(true, "npmrc _authToken=", c => "//registry.example.test/:_authToken=" + c);
        P(true, "netrc machine login password value", c => "machine api.example.test login svc password " + c);
        // ---- command lines
        P(false, "SSHPASS=value sshpass -e", c => "SSHPASS=" + c + " sshpass -e ssh host.example.test");
        P(false, "docker login -u user -p value", c => "docker login -u svc -p " + c + " registry.example.test");
        P(false, "redis-cli -a value", c => "redis-cli -a " + c + " ping");
        P(false, "--secret-key value", c => "tool --secret-key " + c + " run");
        P(false, "--access-key value", c => "tool --access-key " + c + " run");
        P(false, "curl --oauth2-bearer value", c => "curl --oauth2-bearer " + c + " https://api.example.test/");
        P(true, "curl -d form with client_secret", c => "curl -d 'client_id=a&client_secret=" + c + "&grant_type=x' https://api.example.test/token");
        P(true, "curl -d JSON with password", c => "curl -d '{\"user\":\"svc\",\"password\":\"" + c + "\"}' https://api.example.test/login");
        P(true, "curl -u in upper case tool path", c => "/usr/bin/curl -su svc:" + c + " https://api.example.test/");
        P(true, "wget --http-password=", c => "wget --http-user=svc --http-password=" + c + " https://dl.example.test/a");
        P(true, "mysql --password=value", c => "mysql -u root --password=" + c + " orders");
        P(true, "PGPASSWORD=value psql", c => "PGPASSWORD=" + c + " psql -h db.example.test");
        P(true, "kubectl --from-literal=password=", c => "kubectl create secret generic db --from-literal=password=" + c);
        P(true, "--token value with a tab", c => "deploy --token\t" + c);
        P(true, "--password 'quoted value with a blank'", c => "login --password 'two " + c + "' --user svc");
        // ---- escaped and odd text
        P(true, "escaped JSON in text: {\\\"password\\\":\\\"value\\\"}", c => "request body: {\\\"user\\\":\\\"svc\\\",\\\"password\\\":\\\"" + c + "\\\"}");
        P(true, "escaped JSON in text: \\\"access_token\\\"", c => "response: {\\\"access_token\\\":\\\"" + c + "\\\",\\\"expires_in\\\":3600}");
        P(true, "broken JSON fragment access_token\":\"value\"", c => "... access_token\":\"" + c + "\",\"x\":1");
        P(true, "password=\"quoted\"", c => "login password=\"" + c + "\" ok");
        P(true, "PASSWORD=value upper case", c => "PASSWORD=" + c);
        P(true, "passwd: value", c => "passwd: " + c);
        P(true, "TOKEN = value with blanks", c => "TOKEN = " + c);
        P(true, "two assignments on one line", c => "user=svc password=" + c + " token=" + c);
        P(true, "api-key: value", c => "api-key: " + c);
        P(true, "apikey=value", c => "apikey=" + c);
        P(false, "Google-style API key AIza", c => "key AIzaSy" + c + c + "0000000 used");
        P(false, "SendGrid-style key SG.x.y", c => "mail SG." + c + "aaaaaa." + c + c + " sent");
        P(false, "hf_ token", c => "hub hf_" + c + c + " ok");
        P(false, "pgpass line", c => "db.example.test:5432:orders:svc:" + c);
        P(false, "password in prose", c => "the password is " + c + " for now");

        // ---- JSON documents (each its own string value)
        J(true, "JSON, password six levels deep", c => "{\"a\":{\"b\":{\"c\":{\"d\":{\"e\":{\"password\":\"" + c + "\"}}}}}}");
        J(true, "JSON, object under a credential name", c => "{\"password\":{\"value\":\"" + c + "\"}}");
        J(true, "JSON, array of objects under a credential name", c => "{\"secret\":[{\"v\":\"" + c + "\"}]}");
        J(false, "JSON, plural name tokens", c => "{\"tokens\":[\"" + c + "\"]}");
        J(true, "JSON, number under a credential name", c => "{\"pin_token\":" + NumberOf(5, place) + "}");
        J(true, "JSON, cookie member", c => "{\"headers\":{\"cookie\":\"sid=" + c + "\"}}");
        J(true, "JSON in JSON strings, 5 levels, password at the bottom", c => Nest("{\"password\":\"" + c + "\"}", 5));
        J(true, "JSON in JSON strings, 5 levels, cookie at the bottom", c => Nest("{\"cookie\":\"sid=" + c + "\"}", 5));
        J(true, "JSON in JSON strings, 5 levels, numeric password at the bottom", c => Nest("{\"password\":" + NumberOf(6, place) + "}", 5));
        J(true, "JSON in JSON strings, 5 levels, Bearer in a header list", c => Nest("{\"authorization\":[\"Bearer " + c + "\"]}", 5));
        J(false, "JSON, SecretString member", c => "{\"SecretString\":\"" + c + "\"}");
        J(false, "JSON, credentials member", c => "{\"credentials\":\"" + c + "\"}");
        J(false, "JSON, pass member", c => "{\"pass\":\"" + c + "\"}");
        J(false, "JSON, passphrase member", c => "{\"passphrase\":\"" + c + "\"}");
        J(true, "JSON, env ANTHROPIC_API_KEY", c => "{\"env\":{\"ANTHROPIC_API_KEY\":\"" + c + "\"}}");
        J(false, "JSON, argument list docker login -p", c => "{\"command\":[\"docker\",\"login\",\"-u\",\"svc\",\"-p\",\"" + c + "\"]}");
        J(false, "JSON, argument list --secret-key", c => "[\"tool\",\"--secret-key\",\"" + c + "\"]");
        J(true, "JSON, argument list --password", c => "[\"mysql\",\"-u\",\"root\",\"--password\",\"" + c + "\"]");
        J(true, "JSON, private_key member with a PEM block", c => "{\"type\":\"service_account\",\"private_key\":\"-----BEGIN PRIVATE KEY-----\\nMIIE" + c + "\\n-----END PRIVATE KEY-----\\n\"}");
        J(true, "JSON, PEM block under an ordinary name", c => "{\"content\":\"-----BEGIN RSA PRIVATE KEY-----\\nMIIE" + c + "\\n-----END RSA PRIVATE KEY-----\\n\"}");
        J(true, "JSON with leading blanks", c => "  \n {\"password\":\"" + c + "\"}");
        J(true, "JSON cut off after a password", c => "{\"password\":\"" + c + "\",\"x\":\"abc");
        J(false, "JSON cut off after a cookie", c => "{\"headers\":{\"cookie\":\"sid=" + c + "\"},\"x\":\"abc");
        J(true, "python repr of a dict with password", c => "{'user': 'svc', 'password': '" + c + "'}");
        J(true, "NDJSON, two documents", c => "{\"password\":\"" + c + "\"}\n{\"a\":1}");
        J(true, "JSON nested 70 arrays deep, password inside", c => new string('[', 70) + "{\"password\":\"" + c + "\"}" + new string(']', 70));
        J(false, "JSON nested 70 arrays deep, cookie inside", c => new string('[', 70) + "{\"cookie\":\"sid=" + c + "\"}" + new string(']', 70));
        J(true, "JSON, Authorization member with Basic", c => "{\"headers\":{\"Authorization\":\"Basic " + B64("svc:" + c) + "\"}}");
        needles.Add(("JSON, Authorization member with Basic", B64("svc:" + Core(transport, next - 1, place))));
        J(true, "JSON, member name itself a credential assignment", c => "{\"token=" + c + "\":1}");
        J(true, "JSON, connection string leaf", c => "{\"ConnectionStrings\":{\"Default\":\"Server=db;User Id=svc;Password=" + c + ";\"}}");
        J(true, "JSON, bash command with export", c => "{\"command\":\"export X_TOKEN=" + c + " && ./run.sh\",\"timeout\":1}");
        J(true, "JSON, password with unicode escapes in the name", c => "{\"pass\\u0077ord\":\"" + c + "\"}");
        J(true, "JSON, duplicate member names", c => "{\"password\":\"x\",\"password\":\"" + c + "\"}");

        // ---- attributes by key and structure
        K(true, "attribute db.password, string", c => Str("db.password", c));
        K(true, "attribute http.request.header.authorization, list", c => new KeyValue { Key = "http.request.header.authorization", Value = Arr(S("Bearer " + c)) });
        K(false, "attribute db.pass, string", c => Str("db.pass", c));
        K(false, "attribute MYSQL_PWD, string", c => Str("MYSQL_PWD", c));
        K(false, "attribute passphrase, string", c => Str("ssh.passphrase", c));
        K(true, "attribute http.request.header.cookie, list", c => new KeyValue { Key = "http.request.header.cookie", Value = Arr(S("sid=" + c)) });
        K(true, "attribute Authorization, capitalized key", c => Str("Authorization", c));
        K(true, "attribute X-API-KEY, upper case key", c => Str("X-API-KEY", c));
        K(true, "map under a credential-named attribute", c => new KeyValue { Key = "app.password", Value = Map(Str("value", c)) });
        K(true, "list of maps under a credential-named attribute", c => new KeyValue { Key = "app.secret", Value = Arr(Map(Str("v", c))) });
        K(false, "list under a plural name tokens", c => new KeyValue { Key = "app.tokens", Value = Arr(S(c)) });
        K(true, "map member named password, nested 6 maps deep", c => new KeyValue { Key = "app.config", Value = Map(new KeyValue { Key = "a", Value = Map(new KeyValue { Key = "b", Value = Map(new KeyValue { Key = "c", Value = Map(new KeyValue { Key = "d", Value = Map(Str("password", c)) }) }) }) }) });
        K(true, "bytes under an ordinary key holding password=value", c => Bytes("http.request.body.raw", "password=" + c, needles, "bytes under an ordinary key holding password=value"));
        K(true, "bytes under an ordinary key holding a Bearer header", c => Bytes("app.payload", "Authorization: Bearer " + c, needles, "bytes under an ordinary key holding a Bearer header"));
        K(true, "bytes under db.password", c => Bytes("db.blob.password", c, needles, "bytes under db.password"), true);
        K(true, "int under a credential-named key", _ => new KeyValue { Key = "user.pin_token", Value = new AnyValue { IntValue = long.Parse(NumberOf(7, place), CultureInfo.InvariantCulture) } });
        K(true, "double under a credential-named key", _ => new KeyValue { Key = "app.api_key", Value = new AnyValue { DoubleValue = double.Parse(NumberOf(8, place), CultureInfo.InvariantCulture) + 0.5 } });
        K(true, "int in a list under a credential-named key", _ => new KeyValue { Key = "app.otp_token", Value = Arr(new AnyValue { IntValue = long.Parse(NumberOf(9, place), CultureInfo.InvariantCulture) }) });
        K(false, "process.command_args: docker login -p value", c => new KeyValue { Key = "process.command_args", Value = Arr(S("docker"), S("login"), S("-u"), S("svc"), S("-p"), S(c)) });
        K(false, "argument list: --secret-key value", c => new KeyValue { Key = "app.args.a", Value = Arr(S("tool"), S("--secret-key"), S(c)) });
        K(true, "argument list: --password value", c => new KeyValue { Key = "app.args.b", Value = Arr(S("mysql"), S("--password"), S(c)) });
        K(true, "argument list: --password then a map", c => new KeyValue { Key = "app.args.c", Value = Arr(S("x"), S("--token"), Map(Str("v", c))) });
        K(true, "attribute key that is a URL with user:password", c => Str("https://svc:" + c + "@host.example.test/", "ordinary"));
        K(true, "map member key that is a Bearer header", c => new KeyValue { Key = "app.headers", Value = Map(Str("Authorization: Bearer " + c, "ordinary")) });
        K(true, "string of a JSON document under an ordinary key in a map", c => new KeyValue { Key = "app.request", Value = Map(Str("body", "{\"password\":\"" + c + "\"}")) });
        return new Set(plain, json, keyed, needles);
    }

    // 91511<kind><place:000>: a number planted under a credential name.
    internal const string NumberMark = "91511";

    private static string NumberOf(int kind, int place) =>
        string.Create(CultureInfo.InvariantCulture, $"91511{kind}{place:000}");

    private static string Nest(string document, int levels)
    {
        for (var level = 0; level < levels; level++)
            document = new JsonObject { ["inner"] = document, ["n"] = level }.ToJsonString(
                new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        return document;
    }

    private static AnyValue S(string text) => new() { StringValue = text };
    private static KeyValue Str(string key, string text) => new() { Key = key, Value = S(text) };

    private static AnyValue Arr(params AnyValue[] items)
    {
        var list = new ArrayValue();
        list.Values.Add(items);
        return new AnyValue { ArrayValue = list };
    }

    private static AnyValue Map(params KeyValue[] members)
    {
        var list = new KeyValueList();
        list.Values.Add(members);
        return new AnyValue { KvlistValue = list };
    }

    private static KeyValue Bytes(string key, string text, List<(string, string)> needles, string name)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        needles.Add((name, Convert.ToBase64String(bytes)));
        return new KeyValue { Key = key, Value = new AnyValue { BytesValue = ByteString.CopyFrom(bytes) } };
    }
}
