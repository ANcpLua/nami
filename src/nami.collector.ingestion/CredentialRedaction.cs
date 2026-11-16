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
///     JSON member whose own name is a credential name, a number included. A user name is no credential and stays. A string that is a JSON document is redacted leaf by leaf,
///     so it stays JSON. A credential of no such shape is not recognised; this lowers the risk and is no guarantee.
///     The outcome depends on the text alone: every pattern matches without backtracking, in time linear in the
///     text, so no wall-clock deadline exists that a loaded machine could miss.
///     ILogger classification/redaction does not cover incoming values; this stage does.
/// </summary>
internal static class CredentialRedaction
{
    internal const string Replacement = "[REDACTED]";
    internal const string FailureReplacement = "[SANITIZATION_FAILED]";

    private static readonly Regex s_embeddedUrls = new("""(?:[a-z][a-z0-9+.-]*://|//)[^\s<>"'\x60]+""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        Regex.InfiniteMatchTimeout);

    // A credential name, an optional closing quote, ':' or '=' and an optional Bearer or Basic scheme. The names that
    // only ever mean a secret (password, secret, token, the key names) also match at the end of a longer name, so an
    // environment variable such as DB_PASSWORD, GITHUB_TOKEN or AWS_SECRET_ACCESS_KEY is an assignment too; the word
    // boundary after the name keeps tokens=5, token_count and input_tokens out.
    private static readonly Regex s_credentialAssignments = new(
        """(?:\b(?:authorization|proxy[-_ ]?authorization|pwd|shared[-_ ]?access[-_ ]?(?:key|signature)|session[-_ ]?(?:token|key))|x[-_]?otlp[-_]?api[-_]?key|x[-_]?api[-_]?key|api[-_ ]?key|access[-_ ]?key(?:[-_ ]?id)?|account[-_ ]?key|client[-_ ]?secret|secret(?:[-_ ]?key)?|private[-_ ]?key|password|passwd|token)\b["']?[ \t]*(?<separator>[:=])[ \t]*(?<scheme>(?:Bearer|Basic)[ \t]+)?""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        Regex.InfiniteMatchTimeout);

    // A long command-line flag whose name is, or ends in, a credential name, and '=' or blanks before its value:
    // --password x, --db-password=x, --api-key x, --client-secret=x, --pass x. A flag starts a word.
    private const string CredentialFlagName =
        "--(?:[a-z0-9]+[-_])*(?:password|passwd|pass|token|api[-_]?key|secret)";

    private static readonly Regex s_credentialFlags = new(
        "(?:\\A|[\\s\"'\\x60(\\[,=])" + CredentialFlagName + "(?:=|[ \\t]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        Regex.InfiniteMatchTimeout);

    private static readonly Regex s_credentialFlagAlone = new("\\A" + CredentialFlagName + "\\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        Regex.InfiniteMatchTimeout);

    // Tools that take a password in a short flag. The flag is read only on the command of its tool, and in its
    // case: -p is a port elsewhere and -P is mysql's port.
    //   curl -u user:pass, --user user:pass, --user=user:pass   the part after the colon
    //   mysql, mariadb, mysqldump -p<value>                      the value attached to -p
    //   sshpass -p <value>                                       the value
    private static readonly Regex s_curlUser = new(
        """\b[Cc][Uu][Rr][Ll]\b[^\r\n;|&]*(?:[ \t]-u[ \t]*|[ \t]--user(?:=|[ \t]+))""",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, Regex.InfiniteMatchTimeout);

    private static readonly Regex s_attachedPassword = new(
        """\b(?:mysql|mariadb|mysqldump)\b[^\r\n;|&]*[ \t]-p""",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, Regex.InfiniteMatchTimeout);

    private static readonly Regex s_sshpass = new("""\bsshpass[ \t]+-p[ \t]*""",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, Regex.InfiniteMatchTimeout);

    private static readonly SearchValues<string> s_flagNames =
        SearchValues.Create(["pass", "token", "key", "secret"], StringComparison.OrdinalIgnoreCase);

    private static readonly SearchValues<string> s_passwordTools =
        SearchValues.Create(["curl", "mysql", "mariadb", "sshpass"], StringComparison.OrdinalIgnoreCase);

    private static readonly Regex s_authorizationValues = new(
        """(?<scheme>\b(?:Bearer|Basic)[ \t]+)(?<value>[A-Za-z0-9._~+/=-]+)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        Regex.InfiniteMatchTimeout);

    private static readonly Regex s_jwtCandidates = new("""\b[A-Za-z0-9_-]{4,}\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*""",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, Regex.InfiniteMatchTimeout);

    // OpenAI and Anthropic (sk-, sk-proj-, sk-ant-), Stripe, GitHub (ghp_, gho_, ghu_, ghs_, ghr_, github_pat_), npm,
    // Slack (xoxb-, xoxp-, ...), GitLab (glpat-) and AWS access key ids (AKIA, ASIA).
    private static readonly Regex s_knownApiTokens = new(
        """\b(?:sk-(?:proj-)?[A-Za-z0-9_-]{16,}|sk_(?:live|test)_[A-Za-z0-9]{16,}|gh[pousr]_[A-Za-z0-9_]{16,}|github_pat_[A-Za-z0-9_]{16,}|npm_[A-Za-z0-9]{16,}|xox[baprs]-[A-Za-z0-9-]{16,}|glpat-[A-Za-z0-9_-]{16,}|A[KS]IA[0-9A-Z]{16})\b""",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, Regex.InfiniteMatchTimeout);

    // A name that says its value is a credential, at the end of an attribute key or a JSON member name:
    // http.request.header.authorization, db.password, AWS_SECRET_ACCESS_KEY, set-cookie. user and uid are not among
    // them (k8s.pod.uid is no credential), and neither is a bare key (messaging.kafka.message.key).
    private static readonly Regex s_credentialNames = new(
        """(?:authorization|api[-_ ]?key|access[-_ ]?key(?:[-_ ]?id)?|account[-_ ]?key|shared[-_ ]?access[-_ ]?(?:key|signature)|client[-_ ]?secret|secret(?:[-_ ]?key)?|private[-_ ]?key|session[-_ ]?key|password|passwd|\bpwd|token|cookie)\z""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        Regex.InfiniteMatchTimeout);

    private static readonly SearchValues<string> s_credentialNameParts = SearchValues.Create(
        ["authorization", "key", "secret", "password", "passwd", "pwd", "token", "signature", "cookie"],
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
        ["authorization", "key", "secret", "password", "passwd", "pwd", "token", "signature"],
        StringComparison.OrdinalIgnoreCase);

    private static readonly SearchValues<string> s_schemes =
        SearchValues.Create(["bearer", "basic"], StringComparison.OrdinalIgnoreCase);

    private static readonly SearchValues<char> s_letters =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz");

    private static readonly SearchValues<string> s_tokenPrefixes = SearchValues.Create(
        ["sk-", "sk_live_", "sk_test_", "ghp_", "gho_", "ghu_", "ghs_", "ghr_", "github_pat_", "npm_", "xox", "glpat-",
            "AKIA", "ASIA"],
        StringComparison.Ordinal);

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

    // A JSON document inside a JSON string (tool_input inside a request body) is redacted as JSON too, this deep.
    private const int MaxJsonNesting = 3;

    private static string Redact(string text, string? attributeKey, int nesting)
    {
        var span = text.AsSpan();
        var urls = span.Contains("//", StringComparison.Ordinal) && span.ContainsAny(s_urlParts);
        var assignments = span.ContainsAny(s_assignmentSeparators) && span.ContainsAny(s_assignmentNames);
        var schemes = span.ContainsAny(s_schemes);
        var firstDot = span.IndexOf('.');
        var tokens = firstDot >= 0 && span[(firstDot + 1)..].Contains('.');
        var prefixes = span.ContainsAny(s_tokenPrefixes);
        var flags = span.Contains("--", StringComparison.Ordinal) && span.ContainsAny(s_flagNames);
        var tools = span.Contains(" -", StringComparison.Ordinal) && span.ContainsAny(s_passwordTools);
        // In a JSON document a member's name alone can say that its value is a credential ("cookie": "...").
        var members = nesting < MaxJsonNesting && span.ContainsAny(s_credentialNameParts) && LooksLikeJson(span);
        if (!(urls || assignments || schemes || tokens || prefixes || members || flags || tools)) return text;

        try
        {
            if (nesting < MaxJsonNesting && LooksLikeJson(span) && TryRedactJson(text, nesting, out var json)) return json;
            var redacted = text;
            if (urls) redacted = s_embeddedUrls.Replace(redacted, RedactUrl);
            if (assignments) redacted = RedactAssignments(redacted);
            if (flags) redacted = RedactValuesAfter(redacted, s_credentialFlags, false);
            if (tools)
            {
                redacted = RedactValuesAfter(redacted, s_curlUser, true);
                redacted = RedactValuesAfter(redacted, s_attachedPassword, false);
                redacted = RedactValuesAfter(redacted, s_sshpass, false);
            }

            if (schemes) redacted = s_authorizationValues.Replace(redacted, RedactAuthorizationValue);
            if (tokens) redacted = s_jwtCandidates.Replace(redacted, RedactJwt);
            if (prefixes) redacted = s_knownApiTokens.Replace(redacted, Replacement);
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

    private static string RedactAssignments(string text)
    {
        StringBuilder? result = null;
        var copied = 0;
        foreach (Match match in s_credentialAssignments.Matches(text))
        {
            if (match.Index < copied) continue;
            var start = match.Index + match.Length;
            if (start == text.Length) continue;
            var end = SecretEnd(text, start, match.Groups["separator"].Value == "=", match.Groups["scheme"].Success);
            if (end == start) continue;
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

    /// <summary>True when the text is nothing but a flag that takes a credential: its value is the next argument.</summary>
    internal static bool IsCredentialFlag(string text) =>
        text.StartsWith("--", StringComparison.Ordinal) && s_credentialFlagAlone.IsMatch(text);

    // Replaces the value that follows each match: a quoted value to its quote, any other to the end of its word.
    // A word that starts with '-' after a blank is the next flag, not a value. With afterColon only the part of the
    // value after its first ':' goes (curl's user:password), and a value without a colon stays.
    private static string RedactValuesAfter(string text, Regex pattern, bool afterColon)
    {
        StringBuilder? result = null;
        var copied = 0;
        foreach (Match match in pattern.Matches(text))
        {
            var start = match.Index + match.Length;
            if (start < copied || start >= text.Length) continue;
            if (text[start] == '-' && text[start - 1] != '=') continue;
            var end = SecretEnd(text, start, true, true);
            if (end == start) continue;
            var quoted = text[start] is '"' or '\'' && end > start + 1 && text[end - 1] == text[start];
            var from = quoted ? start + 1 : start;
            var to = quoted ? end - 1 : end;
            if (afterColon)
            {
                var colon = text.IndexOf(':', from, to - from);
                if (colon < 0) continue;
                from = colon + 1;
            }

            if (from >= to) continue;
            result ??= new StringBuilder(text.Length);
            result.Append(text, copied, from - copied).Append(Replacement);
            copied = to;
        }

        return result is null ? text : result.Append(text, copied, text.Length - copied).ToString();
    }

    // Where the value of an assignment ends. The rest of the text stays readable wherever the end is certain: a
    // quoted value ends at its quote, a value after Bearer or Basic and a value after '=' end at white space, as a
    // shell word and a logfmt field do. Two cases stay conservative, because such a value may hold blanks: the line
    // of a connection string, known by its ';', where a value runs to the next ';' or the line's end; and a value
    // after ':', which runs to the line's end.
    private static int SecretEnd(string text, int start, bool equalsSeparator, bool scheme)
    {
        var opening = text[start];
        if (opening is '"' or '\'' or '{')
        {
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

        var lineStart = text.AsSpan(0, start).LastIndexOfAny('\r', '\n') + 1;
        var lineEnd = text.AsSpan(start).IndexOfAny('\r', '\n') is var length and >= 0 ? start + length : text.Length;
        if (scheme) return WordEnd(text, start, lineEnd);
        if (!equalsSeparator) return lineEnd;

        // Unquoted connection-string passwords can contain spaces, commas, &, ? and #.
        // A conservative line/semicolon bound must not leave such suffixes behind.
        if (text.AsSpan(start, lineEnd - start).IndexOf(';') is var semicolon and >= 0) return start + semicolon;
        return text.AsSpan(lineStart, start - lineStart).Contains(';') ? lineEnd : WordEnd(text, start, lineEnd);
    }

    private static int WordEnd(string text, int start, int lineEnd)
    {
        var end = start;
        // A backslash ends the word too: in text that escapes its quotes (\") the escape stays whole.
        while (end < lineEnd && text[end] is not (' ' or '\t' or '"' or '\'' or '`' or '\\')) end++;
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
                    WriteRedacted(member.Value, writer, NamesACredential(sent), nesting, ref changed);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                // In a list of arguments the value of a credential flag is the next item.
                var afterFlag = false;
                foreach (var item in element.EnumerateArray())
                {
                    WriteRedacted(item, writer, credential || afterFlag, nesting, ref changed);
                    afterFlag = item.ValueKind is JsonValueKind.String && IsCredentialFlag(item.GetString()!);
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
