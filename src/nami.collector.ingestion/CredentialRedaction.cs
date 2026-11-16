using System.Buffers;
using System.Text.RegularExpressions;

namespace Nami.Collector.Ingestion;

/// <summary>
///     The one credential stage (observer's rule, 16 Nov 2026): no token or password is stored or shown by nami.
///     Telemetry is stored as it was sent; this stage and the size bounds are the only two things ingest does to a
///     record. Every string nami keeps of an OTLP export passes <see cref="Redact" /> before any bound, whatever
///     key or field carries it: attribute keys and values of every carrier, array items, map members, log bodies,
///     names, status messages, scope and schema text. No key and no field is exempt. Where a string holds a
///     credential, the credential is replaced by <c>[REDACTED]</c> and the rest of the string stays as sent.
///     It removes what its patterns recognise: user information, query and fragment of a URL, each with the
///     replacement in its place, so the removal shows; the value of an assignment to a credential name (password=,
///     token:, api_key=, secret=, authorization: ...); the value of a command-line flag with such a name
///     (--password x, --api-key=x); the password of curl -u, of mysql -p and of sshpass -p; the value after a
///     Bearer or Basic scheme; JSON Web Tokens; tokens with a known prefix; and the whole value of an attribute or
///     JSON member whose own name is a credential name, a number, a list and a map under it included; a PEM
///     private key block; an AWS secret next to its key id. A user name is no credential and stays. A string that is a JSON document is redacted leaf by leaf,
///     so it stays JSON. A credential of no such shape is not recognised; this lowers the risk and is no guarantee.
///     The outcome depends on the text alone: every pattern matches without backtracking, in time linear in the
///     text, so no wall-clock deadline exists that a loaded machine could miss.
///     ILogger classification/redaction does not cover incoming values; this stage does.
/// </summary>
internal static class CredentialRedaction
{
    internal const string Replacement = "[REDACTED]";
    internal const string FailureReplacement = "[SANITIZATION_FAILED]";

    private const RegexOptions Linear = RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;
    private const RegexOptions LinearIgnoreCase = Linear | RegexOptions.IgnoreCase;

    private static Regex Pattern(string pattern, RegexOptions options = LinearIgnoreCase) =>
        new(pattern, options, Regex.InfiniteMatchTimeout);

    private static readonly Regex s_embeddedUrls = Pattern("(?:[a-z][a-z0-9+.-]*://|//)[^\\s<>\"'\\x60]+");

    // The user information of a URL, whatever it holds (a quote, an encoded '@') and however its slashes are
    // written (https:\/\/ in escaped JSON): everything between the slashes and the '@' before the host.
    private static readonly Regex s_urlUserInfo = Pattern("(?<slashes>:(?:\\\\?/){2})[^\\s/?#@\\\\\"<>]+@", Linear);

    // A relative reference with a query or a fragment (GET /cb?access_token=...): the same rule as for an absolute
    // URL, the path stays and the query and the fragment go.
    private static readonly Regex s_relativeReferences =
        Pattern("(?<lead>\\A|[\\s\"'(=])(?<path>/[^\\s?#\"'<>\\x60]*)(?<rest>[?#][^\\s\"'<>\\x60]+)", Linear);

    // user:password@host: without a scheme, as rsync and scp take it.
    private static readonly Regex s_remoteLogins =
        Pattern("(?<keep>(?:\\A|[\\s\"'])[A-Za-z0-9._-]+:)[^\\s:@/\"']+(?<tail>@[A-Za-z0-9.-]+:)", Linear);

    // A credential name, what may follow it in a longer name (API_TOKEN_V2, SECRET_KEY_BASE), an optional closing
    // quote or bracket, ':' or '=' (also URL-encoded) and an optional scheme. A name that only ever means a secret
    // matches at the end of any longer name (DB_PASSWORD, accessToken, X-Amz-Signature); the short words pass, pwd
    // and sig need a separator before them, so bypass= and design= stay. tokens=5 stays because the name must end
    // at the word or go on after a separator; token_count= stays because of what follows (NoSecretSuffixes).
    private static readonly Regex s_credentialAssignments = Pattern(
        "(?:(?:\\A|[^A-Za-z0-9])(?:pass|pwd|sig)|authorization|passw(?:or)?d|passphrase|sshpass|secret|token|credentials?|cookie|signature|(?:api|access|account|private|encryption|signing|master|session|shared[-_ ]?access)[-_. ]?key)" +
        "(?<suffix>(?:[_.-][A-Za-z0-9]+)*)(?:\\\\?[\"'])?\\]?[ \\t]*(?<separator>[:=]|%3D)[ \\t]*(?<scheme>(?:Bearer|Basic|Token)(?:[ \\t]+|%20))?");

    // Words after a credential word that say the value is about the credential and is none itself.
    private static readonly FrozenSet<string> s_noSecretSuffixes = FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
        "count", "counts", "total", "limit", "limits", "usage", "used", "budget", "len", "length", "size", "type",
        "types", "kind", "name", "names", "file", "path", "dir", "url", "uri", "endpoint", "policy", "ttl", "expiry",
        "expires", "expiration", "expired", "ref", "header", "prefix", "suffix", "format", "enabled", "required",
        "valid", "exists", "present", "age", "rotation", "lifetime", "timeout", "store", "provider", "source");

    // A long command-line flag whose name is, or ends in, a credential name, and '=' or blanks before its value:
    // --password x, --db-password=x, --api-key x, --client-secret=x, --secret-access-key x. A flag starts a word.
    private const string CredentialFlagName =
        "--(?:[a-z0-9]+[-_])*(?:password|passwd|passphrase|pass|token|secret|bearer|(?:api|access|secret|private|encryption|account)[-_]?key)";

    private static readonly Regex s_credentialFlags =
        Pattern("(?:\\A|[\\s\"'\\x60(\\[,=])" + CredentialFlagName + "(?:=|[ \\t]+)");

    private static readonly Regex s_credentialFlagAlone = Pattern("\\A" + CredentialFlagName + "\\z");

    // Tools that take a password in a short flag. A flag is read only on a line that names its tool, and in its
    // case: -p is a port elsewhere and -P is mysql's port.
    private static readonly Regex s_curlUser = Pattern("(?:[ \\t]-[A-Za-z]*u[ \\t]*|[ \\t]--user(?:=|[ \\t]+))", Linear);
    private static readonly Regex s_attachedPassword = Pattern("[ \\t]-p", Linear);
    private static readonly Regex s_passwordArgument = Pattern("[ \\t]-p[ \\t]+", Linear);
    private static readonly Regex s_redisArgument = Pattern("[ \\t]-a[ \\t]+", Linear);
    private static readonly Regex s_awsValues = Pattern("\\baws_(?:secret_access_key|session_token)[ \\t]+");
    private static readonly Regex s_netrc = Pattern("\\blogin[ \\t]+[^\\s]+[ \\t]+password[ \\t]+");

    // name: X_TOKEN on one line and value: ... on the next, as Kubernetes writes an environment variable.
    private static readonly Regex s_namedValues = Pattern(
        "\\bname:[ \\t]*[\"']?[A-Za-z0-9_.-]*(?:passw(?:or)?d|secret|token|key|credentials?|pwd|pass)[A-Za-z0-9_.-]*[\"']?[ \\t]*\\r?\\n[ \\t]*-?[ \\t]*value:[ \\t]*");

    // SQL: ... WITH PASSWORD '...', IDENTIFIED BY '...'.
    private static readonly Regex s_sqlPasswords = Pattern("\\b(?:PASSWORD|IDENTIFIED[ \\t]+BY)[ \\t]+");

    private static readonly Regex s_xmlElements =
        Pattern("<(?:password|passwd|passphrase|secret|token|api[-_]?key)>");

    // An AWS secret access key next to its key id, as credentials.csv and a pasted pair have it.
    private static readonly Regex s_awsPairs =
        Pattern("\\bA[KS]IA[0-9A-Z]{16}[\"']?[ \\t]*[,;|\\t][ \\t]*[\"']?[A-Za-z0-9/+=]{30,}", Linear);

    private static readonly Regex s_authorizationValues =
        Pattern("(?<scheme>\\b(?:Bearer|Basic)[ \\t]+)(?<value>[A-Za-z0-9._~+/=%-]+)");

    // Three parts of a JSON Web Token, and the two more a JSON Web Encryption has.
    private static readonly Regex s_jwtCandidates =
        Pattern("\\b[A-Za-z0-9_-]{4,}\\.[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]*(?:\\.[A-Za-z0-9_-]+){0,2}", Linear);

    // OpenAI and Anthropic (sk-, sk-proj-, sk-ant-), Stripe, GitHub (ghp_, gho_, ghu_, ghs_, ghr_, github_pat_), npm,
    // Slack (xoxb-, xoxp-, ...), GitLab (glpat-), AWS access key ids (AKIA, ASIA), Google (AIza), Hugging Face (hf_)
    // and SendGrid (SG.x.y). A token glued to a name with an underscore (ANTHROPIC_sk-ant-...) is one too.
    private static readonly Regex s_knownApiTokens = Pattern(
        "(?:\\b|_)(?:sk-(?:proj-)?[A-Za-z0-9_-]{16,}|sk_(?:live|test)_[A-Za-z0-9]{16,}|gh[pousr]_[A-Za-z0-9_]{16,}|github_pat_[A-Za-z0-9_]{16,}|npm_[A-Za-z0-9]{16,}|xox[baprs]-[A-Za-z0-9-]{16,}|glpat-[A-Za-z0-9_-]{16,}|A[KS]IA[0-9A-Z]{16}|AIza[0-9A-Za-z_-]{30,}|hf_[A-Za-z0-9]{30,}|SG\\.[A-Za-z0-9_-]{16,}\\.[A-Za-z0-9_-]{16,})\\b",
        Linear);

    // A name that says its value is a credential, at the end of an attribute key or a JSON member name:
    // http.request.header.authorization, db.password, AWS_SECRET_ACCESS_KEY, set-cookie, db.pass, MYSQL_PWD,
    // ssh.passphrase, SecretString. user and uid are not among them (k8s.pod.uid is no credential), neither is a bare
    // key (messaging.kafka.message.key) nor a count of tokens (input_tokens).
    private static readonly Regex s_credentialNames = Pattern(
        "(?:authorization|api[-_. ]?key|access[-_ ]?key(?:[-_ ]?id)?|account[-_ ]?key|shared[-_ ]?access[-_ ]?(?:key|signature)|secret(?:[-_ ]?(?:key|string))?|private[-_ ]?key|encryption[-_ ]?key|session[-_ ]?key|passw(?:or)?d|passphrase|credentials?|token|cookie|(?:\\A|[^A-Za-z0-9])(?:pass|pwd|sshpass))\\z");

    private static readonly SearchValues<string> s_credentialNameParts = SearchValues.Create(
        ["authorization", "key", "secret", "pass", "pwd", "token", "signature", "cookie", "credential"],
        StringComparison.OrdinalIgnoreCase);

    // Keys repeat on every record; senders choose them, so the table stops growing at a fixed size.
    private const int MaxRememberedNames = 4096;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> s_namesACredential =
        new(StringComparer.Ordinal);

    // What a text must contain before a pattern can match it. Ordinary telemetry (a method, a route, a number, a
    // name) contains none of it and leaves the stage after these scans, with no pattern run and nothing allocated.
    private static readonly SearchValues<char> s_urlParts = SearchValues.Create("@?#");
    private static readonly SearchValues<char> s_assignmentSeparators = SearchValues.Create(":=");

    private static readonly SearchValues<string> s_assignmentNames = SearchValues.Create(
        ["authorization", "key", "secret", "pass", "pwd", "token", "sig", "credential", "cookie"],
        StringComparison.OrdinalIgnoreCase);

    private static readonly SearchValues<string> s_schemes =
        SearchValues.Create(["bearer", "basic"], StringComparison.OrdinalIgnoreCase);

    private static readonly SearchValues<char> s_letters =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz");

    private static readonly SearchValues<string> s_tokenPrefixes = SearchValues.Create(
        ["sk-", "sk_live_", "sk_test_", "ghp_", "gho_", "ghu_", "ghs_", "ghr_", "github_pat_", "npm_", "xox", "glpat-",
            "AKIA", "ASIA", "AIza", "hf_", "SG."],
        StringComparison.Ordinal);

    private static readonly SearchValues<string> s_flagNames =
        SearchValues.Create(["pass", "token", "key", "secret", "bearer"], StringComparison.OrdinalIgnoreCase);

    private const string PemHeaderEnd = "PRIVATE KEY-----";

    /// <summary>
    ///     The text with every recognised credential replaced, and the same instance when it holds none.
    ///     <paramref name="attributeKey" /> is the attribute whose value the text is, or null for any other text; it
    ///     only names the text in the failure metric. A text the stage cannot process is replaced whole
    ///     (fail closed), never kept as sent.
    /// </summary>
    internal static string Redact(string text, string? attributeKey = null) => Redact(text, attributeKey, 0);

    /// <summary>
    ///     The value of an attribute: replaced whole when the attribute's own name is a credential name
    ///     (<c>http.request.header.authorization</c>, <c>db.password</c>), and through <see cref="Redact" />
    ///     otherwise.
    /// </summary>
    internal static string RedactValue(string? attributeKey, string text) =>
        attributeKey is not null && text.Length > 0 && NamesACredential(attributeKey)
            ? Replacement
            : Redact(text, attributeKey, 0);

    /// <summary>True when the name, or its last part, is a credential name such as password, token or api_key.</summary>
    internal static bool NamesACredential(string name)
    {
        if (!name.AsSpan().ContainsAny(s_credentialNameParts)) return false;
        if (s_namesACredential.TryGetValue(name, out var known)) return known;
        var names = s_credentialNames.IsMatch(name);
        if (s_namesACredential.Count < MaxRememberedNames) s_namesACredential.TryAdd(name, names);
        return names;
    }

    /// <summary>True when the bytes are text that holds a credential the stage recognises. Bytes that are no UTF-8 are not read.</summary>
    internal static bool HoldsACredential(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty || !System.Text.Unicode.Utf8.IsValid(bytes)) return false;
        var text = Encoding.UTF8.GetString(bytes);
        return !ReferenceEquals(Redact(text), text);
    }

    // A JSON document inside a JSON string (tool_input inside a request body) is redacted as JSON too, this deep.
    private const int MaxJsonNesting = 12;

    private static string Redact(string text, string? attributeKey, int nesting)
    {
        var span = text.AsSpan();
        var slash = span.Contains('/');
        var urls = slash && span.ContainsAny(s_urlParts);
        var encodedUrls = span.Contains("%2F%2F", StringComparison.OrdinalIgnoreCase);
        var named = span.ContainsAny(s_assignmentNames);
        var assignments = named && (span.ContainsAny(s_assignmentSeparators) ||
                                    span.Contains("%3D", StringComparison.OrdinalIgnoreCase));
        var schemes = span.ContainsAny(s_schemes);
        var firstDot = span.IndexOf('.');
        var tokens = firstDot >= 0 && span[(firstDot + 1)..].Contains('.');
        var prefixes = span.ContainsAny(s_tokenPrefixes);
        var flags = span.Contains("--", StringComparison.Ordinal) && span.ContainsAny(s_flagNames);
        var dashes = span.Contains(" -", StringComparison.Ordinal) || span.Contains("\t-", StringComparison.Ordinal);
        var pem = span.Contains(PemHeaderEnd, StringComparison.Ordinal);
        var logins = span.Contains('@') && span.Contains(':');
        var words = (named || span.Contains("IDENTIFIED", StringComparison.OrdinalIgnoreCase)) &&
                    (span.Contains(' ') || span.Contains('<'));
        // In a JSON document a member's name alone can say that its value is a credential ("cookie": "..."), and
        // the name can be written with escapes.
        var json = nesting < MaxJsonNesting && LooksLikeJson(span);
        // A list of arguments can hold a short password flag ("-p", "-a") and its value as two items.
        var members = json && (span.ContainsAny(s_credentialNameParts) || span.Contains("\\u", StringComparison.Ordinal) ||
                               span.Contains("\"-p\"", StringComparison.Ordinal) || span.Contains("\"-a\"", StringComparison.Ordinal));
        if (!(urls || encodedUrls || assignments || schemes || tokens || prefixes || flags || dashes || pem || logins ||
              words || members)) return text;

        try
        {
            if (json && TryRedactJson(text, nesting, out var redactedJson)) return redactedJson;
            var redacted = text;
            if (pem) redacted = RedactPem(redacted);
            if (logins)
            {
                redacted = s_urlUserInfo.Replace(redacted, static match => match.Groups["slashes"].Value + Replacement + "@");
                redacted = s_remoteLogins.Replace(redacted,
                    static match => match.Groups["keep"].Value + Replacement + match.Groups["tail"].Value);
            }

            if (encodedUrls) redacted = RedactEncodedUserInfo(redacted);
            if (urls)
            {
                redacted = s_embeddedUrls.Replace(redacted, RedactUrl);
                redacted = s_relativeReferences.Replace(redacted, RedactRelativeReference);
            }

            if (assignments) redacted = RedactAssignments(redacted);
            if (flags) redacted = RedactValuesAfter(redacted, s_credentialFlags, ValueShape.Word);
            if (dashes) redacted = RedactToolPasswords(redacted);
            if (words) redacted = RedactWordPatterns(redacted);
            if (schemes) redacted = s_authorizationValues.Replace(redacted, RedactAuthorizationValue);
            if (tokens) redacted = s_jwtCandidates.Replace(redacted, RedactJwt);
            if (prefixes)
            {
                redacted = s_awsPairs.Replace(redacted, Replacement);
                redacted = s_knownApiTokens.Replace(redacted,
                    static match => match.ValueSpan[0] == '_' ? "_" + Replacement : Replacement);
            }

            // A candidate that was no credential (a dotted name, a URL without secrets) is copied by Replace.
            return string.Equals(redacted, text, StringComparison.Ordinal) ? text : redacted;
        }
        catch (Exception)
        {
            // Never log the exception: a parser/regex exception can retain the input.
            return Failed(attributeKey, "processing_error");
        }
    }

    private static string Failed(string? attributeKey, string category)
    {
        // The metric names the registry's key, never a sender's own suffix of a template key.
        NamiCollectorMetrics.AttributeSanitizationFailed(
            attributeKey is null ? "" : AttributeCatalog.Find(attributeKey)?.Key ?? "", category);
        return FailureReplacement;
    }

    // A PEM private key, from its BEGIN line to its END line or, when that is missing, to the end of the text:
    // on several lines, on one line, or with its line ends written as \n inside a command or a JSON string.
    private static string RedactPem(string text)
    {
        const string begin = "-----BEGIN ";
        const string end = "-----END ";
        StringBuilder? result = null;
        var copied = 0;
        var from = 0;
        while (from < text.Length && text.IndexOf(begin, from, StringComparison.Ordinal) is var start and >= 0)
        {
            var header = text.IndexOf("-----", start + begin.Length, StringComparison.Ordinal);
            if (header < 0) break;
            from = header + 5;
            if (header - start > 64 || !text.AsSpan(start, from - start).EndsWith(PemHeaderEnd, StringComparison.Ordinal))
                continue;
            var footer = text.IndexOf(end, from, StringComparison.Ordinal);
            var close = footer < 0 ? -1 : text.IndexOf("-----", footer + end.Length, StringComparison.Ordinal);
            var stop = close < 0 ? text.Length : close + 5;
            result ??= new StringBuilder(text.Length);
            result.Append(text, copied, start - copied).Append(Replacement);
            copied = from = stop;
        }

        return result is null ? text : result.Append(text, copied, text.Length - copied).ToString();
    }

    // scheme%3A%2F%2Fuser%3Apassword%40host inside an encoded parameter (redirect_uri=...).
    private static string RedactEncodedUserInfo(string text)
    {
        const string slashes = "%2F%2F";
        StringBuilder? result = null;
        var copied = 0;
        var from = 0;
        while (text.IndexOf(slashes, from, StringComparison.OrdinalIgnoreCase) is var found and >= 0)
        {
            var start = found + slashes.Length;
            from = start;
            var window = text.AsSpan(start, Math.Min(512, text.Length - start));
            var at = window.IndexOf("%40", StringComparison.OrdinalIgnoreCase);
            if (at <= 0) continue;
            var user = window[..at];
            if (user.ContainsAny(" &\r\n") || user.Contains("%2F", StringComparison.OrdinalIgnoreCase)) continue;
            result ??= new StringBuilder(text.Length);
            result.Append(text, copied, start - copied).Append(Replacement);
            copied = from = start + at;
        }

        return result is null ? text : result.Append(text, copied, text.Length - copied).ToString();
    }

    private static string RedactUrl(Match match)
    {
        // Without '@', '?' or '#' a URL has no user information, query or fragment to remove.
        if (!match.ValueSpan.ContainsAny(s_urlParts)) return match.Value;

        var networkPath = match.Value.StartsWith("//", StringComparison.Ordinal);
        var candidate = networkPath ? "http:" + match.Value : match.Value;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || (uri.Host.Length is 0 && !uri.IsFile))
        {
            // No URL this stage can take apart, such as https://$USER:$PASS@$HOST/ in a shell command: everything
            // after the scheme goes, the text around it stays.
            return match.Value[..(match.Value.IndexOf("//", StringComparison.Ordinal) + 2)] + Replacement;
        }

        if (uri.UserInfo.Length is 0 && uri.Query.Length is 0 && uri.Fragment.Length is 0)
            return match.Value;

        // The destination and the path stay. User information, the whole query and the whole fragment go, unnamed
        // secrets included, and each leaves the replacement where it stood, so a reader sees that something was
        // removed and where.
        var safe = uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);
        if (uri.UserInfo.Length > 0 && safe.IndexOf("//", StringComparison.Ordinal) is var slashes and >= 0)
            safe = safe.Insert(slashes + 2, Replacement + "@");
        if (uri.Query.Length > 0) safe += "?" + Replacement;
        if (uri.Fragment.Length > 0) safe += "#" + Replacement;
        return networkPath ? safe["http:".Length..] : safe;
    }

    private static string RedactRelativeReference(Match match)
    {
        var rest = match.Groups["rest"].ValueSpan;
        var query = rest[0] == '?';
        var fragment = rest.Contains('#');
        return string.Concat(match.Groups["lead"].Value, match.Groups["path"].Value, query ? "?" + Replacement : "",
            fragment ? "#" + Replacement : "");
    }

    // The line a position is on, and what the rules for the end of a value ask about it. Matches arrive in rising
    // order, so every search starts where the last one ended and a text is read a bounded number of times, however
    // long its lines are: 4 MiB of token=x on one line costs what 4 MiB of short lines cost.
    private struct LineCursor
    {
        private int _start;
        private int _end;
        private int _scanned;
        private int _nextSemicolon;
        private bool _semicolonBefore;
        private bool _known;

        internal int End => _end;

        internal void MoveTo(string text, int position)
        {
            if (!_known || position > _end)
            {
                var from = _known ? _end : 0;
                var back = text.AsSpan(from, position - from).LastIndexOfAny('\r', '\n');
                _start = back < 0 ? from : from + back + 1;
                var forward = text.AsSpan(position).IndexOfAny('\r', '\n');
                _end = forward < 0 ? text.Length : position + forward;
                _scanned = _start;
                _semicolonBefore = false;
                _nextSemicolon = -2;
                _known = true;
            }

            if (_semicolonBefore || _scanned >= position) return;
            _semicolonBefore = text.AsSpan(_scanned, position - _scanned).Contains(';');
            _scanned = position;
        }

        internal bool SemicolonBefore => _semicolonBefore;

        // The next ';' on the line at or after the position, or -1.
        internal int SemicolonAfter(string text, int position)
        {
            if (_nextSemicolon == -1 || _nextSemicolon >= position) return _nextSemicolon;
            var index = text.AsSpan(position, _end - position).IndexOf(';');
            return _nextSemicolon = index < 0 ? -1 : position + index;
        }

        internal bool LineContains(string text, string word) =>
            text.AsSpan(_start, _end - _start).Contains(word, StringComparison.OrdinalIgnoreCase);
    }

    private static string RedactAssignments(string text)
    {
        StringBuilder? result = null;
        var copied = 0;
        var line = new LineCursor();
        foreach (Match match in s_credentialAssignments.Matches(text))
        {
            if (match.Index < copied) continue;
            var start = match.Index + match.Length;
            if (start == text.Length) continue;
            if (NamesNoSecret(match.Groups["suffix"].ValueSpan)) continue;
            line.MoveTo(text, start);
            var separator = match.Groups["separator"].ValueSpan;
            var end = separator.Length > 1
                ? WordEnd(text, start, line.End)
                : SecretEnd(text, start, separator[0] == '=', match.Groups["scheme"].Success, ref line);
            // An empty quoted value ("password":"") holds nothing to remove.
            if (end == start || (end == start + 2 && text[start] is '"' or '\'' && text[start + 1] == text[start])) continue;
            result ??= new StringBuilder(text.Length);
            result.Append(text, copied, start - copied);
            var opening = text[start];
            var closing = opening == '{' ? '}' : opening;
            var quoted = opening is '"' or '\'' or '{' && end > start + 1 && text[end - 1] == closing;
            if (quoted) result.Append(opening);
            result.Append(Replacement);
            if (quoted) result.Append(closing);
            copied = end;
        }

        return result is null ? text : result.Append(text, copied, text.Length - copied).ToString();
    }

    // token_count, secret_name, password_policy: what follows the credential word says the value is none.
    private static bool NamesNoSecret(ReadOnlySpan<char> suffix)
    {
        while (suffix.Length > 1)
        {
            suffix = suffix[1..];
            var next = suffix.IndexOfAny("_.-");
            var word = next < 0 ? suffix : suffix[..next];
            if (s_noSecretSuffixes.GetAlternateLookup<ReadOnlySpan<char>>().Contains(word)) return true;
            suffix = next < 0 ? default : suffix[next..];
        }

        return false;
    }

    /// <summary>
    ///     Reads a list of arguments one item at a time and says for each whether it is the value of a credential
    ///     flag before it: the item after --password, after -p of docker login and sshpass, after -a of redis-cli.
    /// </summary>
    internal struct ArgumentList
    {
        private bool _docker;
        private bool _login;
        private bool _redis;
        private bool _sshpass;
        private bool _next;

        /// <summary>True when this item is a credential. <paramref name="item" /> is null for an item that is no string.</summary>
        internal bool IsCredential(string? item)
        {
            var credential = _next;
            _next = false;
            if (item is null) return credential;
            if (IsCredentialFlag(item)) _next = true;
            else if (item is "-p" && ((_docker && _login) || _sshpass)) _next = true;
            else if (item is "-a" && _redis) _next = true;
            _docker |= item.EndsWith("docker", StringComparison.Ordinal);
            _login |= item is "login";
            _redis |= item.EndsWith("redis-cli", StringComparison.Ordinal);
            _sshpass |= item.EndsWith("sshpass", StringComparison.Ordinal);
            return credential;
        }
    }

    /// <summary>True when the text is nothing but a flag that takes a credential: its value is the next argument.</summary>
    internal static bool IsCredentialFlag(string text) =>
        text.StartsWith("--", StringComparison.Ordinal) && s_credentialFlagAlone.IsMatch(text);

    private enum ValueShape
    {
        // A quoted value to its quote, any other to the end of its word.
        Word,

        // Only the part of the word after its first ':' (curl's user:password); a word without a colon stays.
        AfterColon,

        // Only a quoted value (SQL).
        Quoted,

        // Up to the next '<' (the text of an XML element).
        Element
    }

    // Replaces the value that follows each match. A word that starts with '-' after a blank is the next flag, not a
    // value. With tool the match counts only on a line that names the tool (and also, when given).
    private static string RedactValuesAfter(string text, Regex pattern, ValueShape shape, string? tool = null,
        string? also = null)
    {
        StringBuilder? result = null;
        var copied = 0;
        var line = new LineCursor();
        var checkedTo = -1;
        var onTool = false;
        foreach (Match match in pattern.Matches(text))
        {
            var start = match.Index + match.Length;
            if (start < copied || start >= text.Length) continue;
            if (tool is not null)
            {
                line.MoveTo(text, start);
                if (line.End != checkedTo)
                {
                    checkedTo = line.End;
                    onTool = line.LineContains(text, tool) && (also is null || line.LineContains(text, also));
                }

                if (!onTool) continue;
            }

            int from;
            int to;
            if (shape is ValueShape.Element)
            {
                var close = text.IndexOf('<', start);
                if (close <= start) continue;
                (from, to) = (start, close);
            }
            else
            {
                if (text[start] == '-' && text[start - 1] != '=') continue;
                var quoted = text[start] is '"' or '\'';
                if (shape is ValueShape.Quoted && !quoted) continue;
                var end = quoted ? QuoteEnd(text, start) : WordEnd(text, start, text.Length);
                if (end == start) continue;
                var closed = quoted && end > start + 1 && text[end - 1] == text[start];
                from = quoted ? start + 1 : start;
                to = closed ? end - 1 : end;
                if (shape is ValueShape.AfterColon)
                {
                    var colon = text.IndexOf(':', from, to - from);
                    if (colon < 0) continue;
                    from = colon + 1;
                }
            }

            if (from >= to) continue;
            result ??= new StringBuilder(text.Length);
            result.Append(text, copied, from - copied).Append(Replacement);
            copied = to;
        }

        return result is null ? text : result.Append(text, copied, text.Length - copied).ToString();
    }

    //   curl -u user:pass, -su user:pass, --user user:pass, --user=user:pass   the part after the colon
    //   mysql, mariadb, mysqldump -p<value>                                    the value attached to -p
    //   sshpass -p <value>, docker login -p <value>, redis-cli -a <value>      the value
    private static string RedactToolPasswords(string text)
    {
        var span = text.AsSpan();
        if (span.Contains("curl", StringComparison.OrdinalIgnoreCase))
            text = RedactValuesAfter(text, s_curlUser, ValueShape.AfterColon, "curl");
        if (span.Contains("mysql", StringComparison.Ordinal))
            text = RedactValuesAfter(text, s_attachedPassword, ValueShape.Word, "mysql");
        if (span.Contains("mariadb", StringComparison.Ordinal))
            text = RedactValuesAfter(text, s_attachedPassword, ValueShape.Word, "mariadb");
        if (span.Contains("sshpass", StringComparison.Ordinal))
            text = RedactValuesAfter(text, s_passwordArgument, ValueShape.Word, "sshpass");
        if (span.Contains("docker", StringComparison.Ordinal))
            text = RedactValuesAfter(text, s_passwordArgument, ValueShape.Word, "docker", "login");
        if (span.Contains("redis-cli", StringComparison.Ordinal))
            text = RedactValuesAfter(text, s_redisArgument, ValueShape.Word, "redis-cli");
        return text;
    }

    // Credentials that follow a word and a blank instead of a separator: aws configure set aws_secret_access_key
    // <value>, a netrc line, a Kubernetes name and value on two lines, SQL passwords, an XML element.
    private static string RedactWordPatterns(string text)
    {
        var span = text.AsSpan();
        if (span.Contains("aws_", StringComparison.OrdinalIgnoreCase))
            text = RedactValuesAfter(text, s_awsValues, ValueShape.Word);
        if (span.Contains("login", StringComparison.OrdinalIgnoreCase))
            text = RedactValuesAfter(text, s_netrc, ValueShape.Word);
        if (span.Contains("value:", StringComparison.OrdinalIgnoreCase))
            text = RedactValuesAfter(text, s_namedValues, ValueShape.Word);
        if (span.Contains('\'') && (span.Contains("PASSWORD ", StringComparison.OrdinalIgnoreCase) ||
                                    span.Contains("IDENTIFIED", StringComparison.OrdinalIgnoreCase)))
            text = RedactValuesAfter(text, s_sqlPasswords, ValueShape.Quoted);
        if (span.Contains('<') && span.Contains('>'))
            text = RedactValuesAfter(text, s_xmlElements, ValueShape.Element);
        return text;
    }

    // Where the value of an assignment ends. The rest of the text stays readable wherever the end is certain: a
    // quoted value ends at its quote, a value after Bearer or Basic and a value after '=' end at white space, as a
    // shell word and a logfmt field do. Two cases stay conservative, because such a value may hold blanks: the line
    // of a connection string, known by its ';', where a value runs to the next ';' or the line's end; and a value
    // after ':', which runs to the line's end.
    private static int SecretEnd(string text, int start, bool equalsSeparator, bool scheme, ref LineCursor line)
    {
        if (text[start] is '"' or '\'' or '{') return QuoteEnd(text, start);
        if (scheme) return WordEnd(text, start, line.End);
        if (!equalsSeparator) return line.End;

        // Unquoted connection-string passwords can contain spaces, commas, &, ? and #.
        // A conservative line/semicolon bound must not leave such suffixes behind.
        if (line.SemicolonAfter(text, start) is var semicolon and >= 0) return semicolon;
        return line.SemicolonBefore ? line.End : WordEnd(text, start, line.End);
    }

    // The end of a value that opens with a quote or a brace: after its closing mark, a doubled mark and an escaped
    // one being part of the value.
    private static int QuoteEnd(string text, int start)
    {
        var opening = text[start];
        var closing = opening == '{' ? '}' : opening;
        for (var index = start + 1; index < text.Length; index++)
        {
            if (text[index] == '\\' && index + 1 < text.Length)
            {
                index++;
                continue;
            }

            if (text[index] != closing) continue;
            if (!(index + 1 < text.Length && text[index + 1] == closing))
                return index + 1;
            index++;
        }

        // An unterminated quoted credential cannot safely expose its remainder.
        return text.Length;
    }

    private static int WordEnd(string text, int start, int lineEnd)
    {
        var end = start;
        // A backslash ends the word too: in text that escapes its quotes (\") the escape stays whole.
        while (end < lineEnd && text[end] is not (' ' or '\t' or '\r' or '\n' or '"' or '\'' or '`' or '\\')) end++;
        return end;
    }

    private static string RedactAuthorizationValue(Match match)
    {
        // "Basic authentication failed" and "bearer token" are prose: a lone lower-case word is no credential. After
        // an authorization name the assignment rule has removed the value already, whatever it looks like.
        var value = match.Groups["value"].ValueSpan;
        if (value.Length < 20 && !value.ContainsAnyExcept(s_letters) && !value[1..].ContainsAnyInRange('A', 'Z'))
            return match.Value;
        return match.Groups["scheme"].Value + Replacement;
    }

    private static string RedactJwt(Match match)
    {
        // The first byte of a JOSE header is '{', or white space before it: in base64 that is e, I, C or D.
        // Namespace-qualified names and stack frames also contain dots; nearly all of them stop here.
        if (match.ValueSpan[0] is not ('e' or 'I' or 'C' or 'D')) return match.Value;
        var header = match.ValueSpan[..match.ValueSpan.IndexOf('.')];
        if (header.Length > 4096) return Replacement;
        // Recognize a JWT by its JSON JOSE header rather than erasing ordinary type and method names.
        var base64 = header.ToString().Replace('-', '+').Replace('_', '/');
        if (base64.Length % 4 == 1) return match.Value;
        base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
        Span<byte> decoded = stackalloc byte[3072];
        if (!Convert.TryFromBase64String(base64, decoded, out var length)) return match.Value;
        var offset = 0;
        while (offset < length && decoded[offset] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') offset++;
        if (offset == length || decoded[offset] != (byte)'{') return match.Value;
        try
        {
            using var document = JsonDocument.Parse(decoded[..length].ToArray(),
                new JsonDocumentOptions { MaxDepth = 16 });
            return document.RootElement.TryGetProperty("alg", out var algorithm)
                   && algorithm.ValueKind == JsonValueKind.String
                ? Replacement
                : match.Value;
        }
        catch (JsonException)
        {
            // Bytes that start like a JOSE header and are no JSON: a dotted name such as exemplar.map.key or
            // example.com.au decodes that way. It is no token, and it stays as sent.
            return match.Value;
        }
    }

    private static bool LooksLikeJson(ReadOnlySpan<char> text)
    {
        var trimmed = text.TrimStart();
        return trimmed.Length > 1 && trimmed[0] is '{' or '[';
    }

    // A string that is a JSON object or array (Claude Code's tool_parameters and tool_input, a request body) is
    // redacted leaf by leaf: each string value and each member name passes the stage as decoded text, and the value
    // of a member whose name is a credential name goes whole. So an escaped quote cannot hide the end of a
    // credential, and the result is JSON again. With nothing to redact the text is returned as sent, byte for byte;
    // otherwise it is written anew, compact, with every other value as it was.
    private static bool TryRedactJson(string text, int nesting, out string redacted)
    {
        redacted = text;
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 64 });
            var buffer = new System.Buffers.ArrayBufferWriter<byte>(text.Length);
            var changed = false;
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
                   {
                       Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, MaxDepth = 64
                   }))
            {
                WriteRedacted(document.RootElement, writer, false, nesting + 1, ref changed);
            }

            if (changed) redacted = Encoding.UTF8.GetString(buffer.WrittenSpan);
            return true;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            // No JSON after all, or a string with a broken escape: the text is redacted as plain text.
            return false;
        }
    }

    private static void WriteRedacted(JsonElement element, Utf8JsonWriter writer, bool credential, int nesting,
        ref bool changed)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var member in element.EnumerateObject())
                {
                    var sent = member.Name;
                    var name = Redact(sent, null, nesting);
                    changed |= !ReferenceEquals(name, sent);
                    writer.WritePropertyName(name);
                    // Under a credential name everything goes, however deep: an object, a list, a number.
                    WriteRedacted(member.Value, writer, credential || NamesACredential(sent), nesting, ref changed);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                // In a list of arguments the value of a credential flag is the next item.
                var arguments = new ArgumentList();
                foreach (var item in element.EnumerateArray())
                {
                    var argument = arguments.IsCredential(item.ValueKind is JsonValueKind.String ? item.GetString() : null);
                    WriteRedacted(item, writer, credential || argument, nesting, ref changed);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                var text = element.GetString()!;
                var kept = credential && text.Length > 0 ? Replacement : Redact(text, null, nesting);
                changed |= !ReferenceEquals(kept, text);
                writer.WriteStringValue(kept);
                break;
            case JsonValueKind.Number when credential:
                // A numeric password or PIN under a credential name is a credential too.
                writer.WriteStringValue(Replacement);
                changed = true;
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
