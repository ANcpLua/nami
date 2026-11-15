using System.Text.Json;

namespace Nami.Api.Contracts;

/// <summary>
/// Reads the tag of a tagged union for the generated union classifiers: positions the classifier's copy of the
/// reader on the value of one top-level property of the object it starts at. As with
/// <see cref="JsonElement.TryGetProperty(string, out JsonElement)"/>, the last occurrence wins.
/// </summary>
internal static class WireTag
{
    /// <summary>
    /// True when the object has the property and its value is a JSON string; the reader then stands on that value.
    /// Skips with <see cref="Utf8JsonReader.TrySkip"/>, because the copy may hold an incomplete buffer while streaming.
    /// </summary>
    public static bool TrySeek(ref Utf8JsonReader reader, ReadOnlySpan<byte> name)
    {
        if (reader.TokenType != JsonTokenType.StartObject) return false;
        Utf8JsonReader found = default;
        var hit = false;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var match = reader.ValueTextEquals(name);
            if (!reader.Read()) return false;
            if (match)
            {
                found = reader;
                hit = true;
            }

            if (!reader.TrySkip()) return false;
        }

        if (!hit) return false;
        reader = found;
        return reader.TokenType == JsonTokenType.String;
    }
}
