using System.Text.RegularExpressions;
using ProtoAnyValue = OpenTelemetry.Proto.Common.V1.AnyValue;

namespace Nami.Collector.Ingestion;

/// <summary>
/// Shared incoming-OTLP value policy for the registry's exception text keys.
/// This runs before any ingestion model can reach storage or an original-point journal.
/// ILogger classification/redaction does not cover those incoming values.
/// The outcome depends on the text alone: every pattern matches without backtracking, in time linear in
/// the bounded input, so no wall-clock deadline exists that a loaded machine could miss and turn
/// valid text into the failure marker.
/// </summary>
internal static partial class ExceptionTextSanitizer
{
    private const int MaxInputCharacters = 128 * 1024;
    private const int MaxPersistedUtf8Bytes = 32 * 1024;
    private const string Replacement = "[REDACTED]";
    private const string FailureReplacement = "[SANITIZATION_FAILED]";
    private static readonly UTF8Encoding s_strictUtf8 = new(false, true);

    internal static string Sanitize(string key, ProtoAnyValue? value)
    {
        if (value?.ValueCase is not ProtoAnyValue.ValueOneofCase.StringValue)
            return Failed(key, "invalid_type");
        var text = value.StringValue;
        if (text.Length > MaxInputCharacters)
            return Failed(key, "input_limit");

        try
        {
            _ = s_strictUtf8.GetByteCount(text);
            var sanitized = EmbeddedUrls().Replace(text, SanitizeUrl);
            sanitized = SanitizeAssignments(sanitized);
            sanitized = AuthorizationValues().Replace(sanitized,
                static match => match.Groups["scheme"].Value + Replacement);
            sanitized = JwtCandidates().Replace(sanitized, RedactJwt);
            sanitized = KnownApiTokens().Replace(sanitized, Replacement);
            return BoundPersistedText(sanitized);
        }
        catch (EncoderFallbackException)
        {
            return Failed(key, "invalid_text");
        }
        catch (UriFormatException)
        {
            return Failed(key, "invalid_url");
        }
        catch (Exception)
        {
            // Never log the exception: a parser/regex exception can retain the input.
            return Failed(key, "processing_error");
        }
    }

    private static string Failed(string key, string category)
    {
        NamiCollectorMetrics.AttributeSanitizationFailed(key, category);
        return FailureReplacement;
    }

    private static string SanitizeUrl(Match match)
    {
        var networkPath = match.Value.StartsWith("//", StringComparison.Ordinal);
        var candidate = networkPath ? "http:" + match.Value : match.Value;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || uri.Host.Length == 0 && !uri.IsFile)
        {
            // A '//' text that is no URL, such as the XPath step //button[text()='Submit'], has no user
            // information, query or fragment to remove; only a scheme URL or such a part fails closed.
            if (networkPath && !match.ValueSpan.ContainsAny('@', '?', '#'))
                return match.Value;
            throw new UriFormatException("An exception attribute contains an invalid URL.");
        }
        if (uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0)
            return match.Value;

        // Match the existing diagnostic URL policy: retain the destination/path,
        // remove user information and the entire query/fragment, including unnamed secrets.
        var safe = uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);
        return networkPath ? safe["http:".Length..] : safe;
    }

    private static string SanitizeAssignments(string text)
    {
        StringBuilder? result = null;
        var copied = 0;
        foreach (Match match in CredentialAssignments().Matches(text))
        {
            if (match.Index < copied) continue;
            var start = match.Index + match.Length;
            if (start == text.Length) continue;
            var end = SecretEnd(text, start, match.Groups["separator"].Value == "=");
            if (end == start) continue;
            result ??= new StringBuilder(text.Length);
            result.Append(text, copied, start - copied);
            var opening = text[start];
            var closing = opening == '{' ? '}' : opening;
            var quoted = (opening is '"' or '\'' or '{') && end > start + 1 && text[end - 1] == closing;
            if (quoted) result.Append(opening);
            result.Append(Replacement);
            if (quoted) result.Append(closing);
            copied = end;
        }
        return result is null ? text : result.Append(text, copied, text.Length - copied).ToString();
    }

    private static int SecretEnd(string text, int start, bool connectionAssignment)
    {
        var opening = text[start];
        if (opening is '"' or '\'' or '{')
        {
            var closing = opening == '{' ? '}' : opening;
            for (var index = start + 1; index < text.Length; index++)
            {
                if (text[index] == '\\' && index + 1 < text.Length) { index++; continue; }
                if (text[index] != closing) continue;
                if (index + 1 < text.Length && text[index + 1] == closing) { index++; continue; }
                return index + 1;
            }
            // An unterminated quoted credential cannot safely expose its remainder.
            return text.Length;
        }

        var end = start;
        // Unquoted connection-string passwords can contain spaces, commas, &, ? and #.
        // A conservative line/semicolon bound must not leave such suffixes behind.
        while (end < text.Length && text[end] is not ('\r' or '\n') &&
               (!connectionAssignment || text[end] != ';')) end++;
        return end;
    }

    private static string RedactJwt(Match match)
    {
        var header = match.Value.AsSpan(0, match.Value.IndexOf('.'));
        if (header.Length > 4096) return Replacement;
        // Namespace-qualified stack frames also contain dots. Recognize a JWT by its
        // JSON JOSE header rather than erasing ordinary type and method names.
        var base64 = header.ToString().Replace('-', '+').Replace('_', '/');
        if (base64.Length % 4 == 1) return match.Value;
        base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
        Span<byte> decoded = stackalloc byte[3072];
        if (!Convert.TryFromBase64String(base64, decoded, out var length)) return match.Value;
        var offset = 0;
        while (offset < length && decoded[offset] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') offset++;
        if (offset == length || decoded[offset] != (byte)'{') return match.Value;
        // Parsing a JSON-looking header is actual sanitization work. If it fails,
        // the outer fail-closed policy replaces the attribute, never the original text.
        using var document = JsonDocument.Parse(decoded[..length].ToArray(),
            new JsonDocumentOptions { MaxDepth = 16 });
        return document.RootElement.TryGetProperty("alg", out var algorithm)
            && algorithm.ValueKind == JsonValueKind.String ? Replacement : match.Value;
    }

    private static string BoundPersistedText(string text)
    {
        if (s_strictUtf8.GetByteCount(text) <= MaxPersistedUtf8Bytes) return text;
        const string Marker = "…";
        var budget = MaxPersistedUtf8Bytes - 3;
        var characters = 0;
        var bytes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > budget) break;
            bytes += rune.Utf8SequenceLength;
            characters += rune.Utf16SequenceLength;
        }
        return text[..characters] + Marker;
    }

    [GeneratedRegex("""(?:[a-z][a-z0-9+.-]*://|//)[^\s<>"'\x60]+""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, Timeout.Infinite)]
    private static partial Regex EmbeddedUrls();

    [GeneratedRegex("""\b(?:authorization|proxy[-_ ]?authorization|x[-_]?otlp[-_]?api[-_]?key|x[-_]?api[-_]?key|api[-_ ]?key|access[-_ ]?key(?:[-_ ]?id)?|account[-_ ]?key|shared[-_ ]?access[-_ ]?(?:key|signature)|client[-_ ]?secret|secret(?:[-_ ]?key)?|private[-_ ]?key|password|passwd|pwd|user(?:[-_ ]?(?:id|name))?|uid|token|access[-_ ]?token|refresh[-_ ]?token|auth[-_ ]?token|bearer[-_ ]?token|session[-_ ]?(?:token|key)|id[-_ ]?token)\b["']?[ \t]*(?<separator>[:=])[ \t]*(?:(?:Bearer|Basic)[ \t]+)?""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, Timeout.Infinite)]
    private static partial Regex CredentialAssignments();

    [GeneratedRegex("""(?<scheme>\b(?:Bearer|Basic)[ \t]+)[A-Za-z0-9._~+/=-]+""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, Timeout.Infinite)]
    private static partial Regex AuthorizationValues();

    [GeneratedRegex("""\b[A-Za-z0-9_-]{4,}\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*""",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, Timeout.Infinite)]
    private static partial Regex JwtCandidates();

    [GeneratedRegex("""\b(?:sk-(?:proj-)?[A-Za-z0-9_-]{16,}|sk_(?:live|test)_[A-Za-z0-9]{16,}|gh[pousr]_[A-Za-z0-9_]{16,}|github_pat_[A-Za-z0-9_]{16,}|npm_[A-Za-z0-9]{16,}|xox[baprs]-[A-Za-z0-9-]{16,})\b""",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, Timeout.Infinite)]
    private static partial Regex KnownApiTokens();
}
