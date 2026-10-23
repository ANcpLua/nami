using Nami.Api.Contracts;
using ContractJson = Nami.Api.Contracts.ContractJson;

namespace Nami.Collector;

/// <summary>
/// The one rule every contract time is read by, the MCP's input schema's as the Collector's routes': RFC 3339 §5.6
/// date-time with <c>T</c> and <c>Z</c> in either case, a numeric offset with its colon, a second from 00 to 59 and a
/// fraction of any length, and no space in place of <c>T</c>. A leap second is refused: no instant either side reads
/// holds a second 60. https://www.rfc-editor.org/rfc/rfc3339#section-5.6
/// </summary>
internal static class ContractDateTime
{
    /// <summary>Whether <paramref name="text"/> is a contract date-time, whether or not a <see cref="DateTimeOffset"/> holds its instant.</summary>
    internal static bool IsValid(string text) => ContractJson.Validate("Common.Timestamp", System.Text.Json.Nodes.JsonValue.Create(text)) is null;

    /// <summary>
    /// A contract date-time's instant and its fraction's digits, or null when <paramref name="text"/> is none. The instant
    /// keeps the offset it was written with, or is UTC for an offset beyond the 14 hours a <see cref="DateTimeOffset"/>
    /// keeps; it is null outside the years 0001 to 9999. Digits past the seventh, below a tick, are left to the caller.
    /// </summary>
    internal static (DateTimeOffset? Instant, string Fraction)? Read(string text)
    {
        if (!IsValid(text)) return null;
        int Part(int start, int length = 2) => int.Parse(text.AsSpan(start, length), NumberStyles.None, CultureInfo.InvariantCulture);
        int year = Part(0, 4), month = Part(5), day = Part(8), hour = Part(11), minute = Part(14), second = Part(17);
        var offsetStart = text.IndexOfAny(['+', '-'], 19);
        var fractionEnd = offsetStart >= 0 ? offsetStart : text.Length - 1;
        var offset = TimeSpan.Zero;
        if (offsetStart >= 0)
        {
            int offsetHours = Part(offsetStart + 1), offsetMinutes = Part(offsetStart + 4);
            offset = new TimeSpan(offsetHours, offsetMinutes, 0) * (text[offsetStart] == '-' ? -1 : 1);
        }
        var fraction = text[19] == '.' ? text[20..fractionEnd] : "";
        if (year is 0) return (null, fraction);
        var ticks = fraction.Length is 0 ? 0 : long.Parse(fraction.Length > 7 ? fraction[..7] : fraction.PadRight(7, '0'), NumberStyles.None, CultureInfo.InvariantCulture);
        var local = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified).AddTicks(ticks);
        try
        {
            return (offset.Duration() <= TimeSpan.FromHours(14) ? new DateTimeOffset(local, offset) : new DateTimeOffset(local - offset, TimeSpan.Zero), fraction);
        }
        catch (ArgumentOutOfRangeException)
        {
            return (null, fraction);
        }
    }

    /// <summary>Whether a fraction is finer than the 100 ns tick an instant holds: a digit past the seventh that is not zero.</summary>
    internal static bool FinerThanTicks(string fraction) => fraction.Length > 7 && fraction.AsSpan(7).ContainsAnyExcept('0');

}
