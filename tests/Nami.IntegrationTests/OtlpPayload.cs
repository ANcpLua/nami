namespace Nami.IntegrationTests;

/// <summary>Raw OTLP export bodies of an exact size, for the receivers' size limits.</summary>
internal static class OtlpPayload
{
    /// <summary>
    ///     A well-formed export of <paramref name="size" /> bytes: one unknown length-delimited field (127) of zeros,
    ///     its length written as a four-byte varint.
    /// </summary>
    internal static byte[] UnknownField(int size)
    {
        var payload = new byte[size];
        payload[0] = 0xfa;
        payload[1] = 0x07;
        var length = size - 6;
        for (var i = 0; i < 4; i++) payload[2 + i] = (byte)((length >> (7 * i)) | (i < 3 ? 0x80 : 0));
        return payload;
    }
}
