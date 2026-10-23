using System.Buffers.Text;
using Nami.Collector.Storage;

namespace Nami.Collector.Hosting;

/// <summary>
/// The one opaque cursor scheme for every paged list (NAMI-A4). A cursor is the keyset position of
/// the last item served — never an offset — so a page boundary survives concurrent ingest. Each
/// list stamps its own kind into the payload, so a cursor from one list is rejected by another
/// instead of being misread as a position in it.
/// </summary>
/// <remarks>
/// Wire form: base64url (unpadded) of <c>v1|kind|field|field…</c>, each field percent-encoded so
/// a session or metric name may carry any character.
/// </remarks>
internal static class PageCursorCodec
{
    private const string Version = "v1";
    private const int MaxEncodedLength = 1024;
    private static readonly UTF8Encoding s_strictUtf8 = new(false, true);

    internal static string Encode(PageCursorKind kind, params ReadOnlySpan<string> fields)
    {
        var payload = new StringBuilder(Version).Append('|').Append(KindName(kind));
        foreach (var field in fields)
            payload.Append('|').Append(Uri.EscapeDataString(field));

        return Base64Url.EncodeToString(s_strictUtf8.GetBytes(payload.ToString()));
    }

    internal static bool TryDecode(string? encoded, PageCursorKind kind, int fieldCount, out string[] fields)
    {
        fields = [];
        if (string.IsNullOrEmpty(encoded) || encoded.Length > MaxEncodedLength) return false;

        string payload;
        try
        {
            payload = s_strictUtf8.GetString(Base64Url.DecodeFromChars(encoded));
        }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException)
        {
            return false;
        }

        var parts = payload.Split('|');
        if (parts.Length != fieldCount + 2 ||
            !string.Equals(parts[0], Version, StringComparison.Ordinal) ||
            !string.Equals(parts[1], KindName(kind), StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            fields = [.. parts.Skip(2).Select(Uri.UnescapeDataString)];
        }
        catch (UriFormatException)
        {
            return false;
        }

        return true;
    }

    internal static string Encode(PageCursorKind kind, TracePageCursor cursor) =>
        Encode(kind, Hex(cursor.ActivityUnixNano), cursor.TraceId);

    internal static bool TryDecode(string? encoded, PageCursorKind kind, out TracePageCursor cursor)
    {
        cursor = default;
        if (!TryDecode(encoded, kind, 2, out var fields) ||
            !TryParseHex(fields[0], out var activity) ||
            !ContractIdentifiers.IsTraceId(fields[1]))
        {
            return false;
        }

        cursor = new TracePageCursor(activity, fields[1]);
        return true;
    }

    internal static string Hex(ulong? value) => value.HasValue ? Hex(value.Value) : "";

    internal static string Hex(ulong value) => value.ToString("x16", CultureInfo.InvariantCulture);

    internal static bool TryParseHex(string value, out ulong parsed)
    {
        parsed = 0;
        return value.Length is 16 && ulong.TryParse(
            value,
            NumberStyles.AllowHexSpecifier,
            CultureInfo.InvariantCulture,
            out parsed);
    }

    private static string KindName(PageCursorKind kind) => kind switch
    {
        PageCursorKind.Traces => "traces",
        PageCursorKind.SessionTraces => "session-traces",
        PageCursorKind.TraceSpans => "trace-spans",
        PageCursorKind.Sessions => "sessions",
        PageCursorKind.Logs => "logs",
        PageCursorKind.Metrics => "metrics",
        PageCursorKind.MetricSeries => "metric-series",
        PageCursorKind.MetricPoints => "metric-points",
        PageCursorKind.SavedQueries => "saved-queries",
        PageCursorKind.Annotations => "annotations",
        PageCursorKind.SavedTraceRun => "saved-query-traces",
        PageCursorKind.SavedLogRun => "saved-query-logs",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}

internal enum PageCursorKind
{
    Traces,
    SessionTraces,
    TraceSpans,
    Sessions,
    Logs,
    Metrics,
    MetricSeries,
    MetricPoints,
    SavedQueries,
    Annotations,
    SavedTraceRun,
    SavedLogRun
}
