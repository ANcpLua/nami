namespace Nami.Collector.Ingestion;

/// <summary>
/// The length-prefixed segment every canonical identity is written in: the value's length, a colon and the value,
/// so no value can run into the next one. A missing value is the length -1. Storage identities end each segment
/// with a newline.
/// </summary>
internal static class CanonicalText
{
    internal static StringBuilder AppendSegment(this StringBuilder builder, string? value, bool terminate = false)
    {
        if (value is null) builder.Append("-1:");
        else builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
        return terminate ? builder.Append('\n') : builder;
    }
}
