namespace Nami.Collector.Ingestion;

/// <summary>
///     The one way nami shortens text it keeps: whole characters (runes) up to a budget of UTF-8 bytes, then a marker
///     that counts toward the budget, so the result never exceeds it and a reader can tell that text is missing.
///     Log bodies and the values the registry bounds (<c>ValueLimits.MaxUtf8Bytes</c>) are cut this way.
/// </summary>
internal static class BoundedText
{
    internal const string TruncationMarker = "...[truncated]";

    internal static string Truncate(string text, int maxUtf8Bytes)
    {
        if (Encoding.UTF8.GetByteCount(text) <= maxUtf8Bytes) return text;

        // A budget too small for the marker holds as much of the text as fits and no marker.
        var marker = Encoding.UTF8.GetByteCount(TruncationMarker) <= maxUtf8Bytes ? TruncationMarker : "";
        var remainingBytes = maxUtf8Bytes - Encoding.UTF8.GetByteCount(marker);
        var builder = new StringBuilder(Math.Min(text.Length, Math.Max(maxUtf8Bytes, 0)));
        Span<char> utf16 = stackalloc char[2];
        foreach (var rune in text.EnumerateRunes().TakeWhile(rune => rune.Utf8SequenceLength <= remainingBytes))
        {
            builder.Append(utf16[..rune.EncodeToUtf16(utf16)]);
            remainingBytes -= rune.Utf8SequenceLength;
        }

        return builder.Append(marker).ToString();
    }
}
