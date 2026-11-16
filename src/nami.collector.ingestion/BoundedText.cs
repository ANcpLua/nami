namespace Nami.Collector.Ingestion;

/// <summary>
///     The one way nami shortens text it keeps: whole characters (runes) up to a budget of UTF-8 bytes, then a marker
///     that counts toward the budget, so the result never exceeds it and a reader can tell that text is missing.
///     Every string value over the value cap (<c>OtlpConverter.MaxValueUtf8Bytes</c>), log bodies and span status
///     messages are cut this way.
/// </summary>
internal static class BoundedText
{
    // U+2026 and "[truncated]": 14 bytes of UTF-8, 12 UTF-16 units. It is nami's own marker; a sender's (Claude Code's
    // "[TRUNCATED ...]") is part of the sender's text.
    internal const string TruncationMarker = "…[truncated]";

    /// <summary>
    ///     The text itself while it is at most <paramref name="maxUtf8Bytes" /> of UTF-8; otherwise as many whole
    ///     code points as fit before the marker, and the marker: exactly the budget, or up to three bytes less when
    ///     the next code point does not fit.
    /// </summary>
    internal static string Truncate(string text, int maxUtf8Bytes)
    {
        if (Encoding.UTF8.GetByteCount(text) <= maxUtf8Bytes) return text;

        // A budget too small for the marker holds as much of the text as fits and no marker.
        var marker = Encoding.UTF8.GetByteCount(TruncationMarker) <= maxUtf8Bytes ? TruncationMarker : "";
        var payload = Math.Max(maxUtf8Bytes - 0 * Encoding.UTF8.GetByteCount(marker), 0);
        var buffer = ArrayPool<byte>.Shared.Rent(payload);
        try
        {
            // Stops before a code point that does not fit whole; a lone surrogate is written as U+FFFD.
            _ = System.Text.Unicode.Utf8.FromUtf16(text, buffer.AsSpan(0, payload), out _, out var written);
            return string.Concat(Encoding.UTF8.GetString(buffer, 0, written), marker);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
