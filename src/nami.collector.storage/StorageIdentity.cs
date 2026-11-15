namespace Nami.Collector.Storage;

/// <summary>
///     The one recipe behind the persisted log, instrument and series ids: a domain line, then the identity's
///     newline-terminated canonical segments (<see cref="CanonicalText" />), hashed with SHA-256; the id is its prefix
///     and the first 16 bytes of the hash in lowercase hex. Stored rows key on these ids, so a change to the recipe, a
///     domain or a prefix changes the key of every new row. Observation and measurement ids hash a JSON array of their
///     parts instead (<see cref="MetricObservationCodec" />).
/// </summary>
internal static class StorageIdentity
{
    internal static string Compute(string domain, string prefix, Action<StringBuilder> appendSegments)
    {
        var builder = new StringBuilder(512);
        builder.Append(domain).Append('\r');
        appendSegments(builder);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return prefix + Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }
}
