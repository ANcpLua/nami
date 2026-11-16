using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Nami.Collector.Ingestion;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Nami.IntegrationTests;

/// <summary>
///     Ledger N91. Google.Protobuf's JSON parser reads the request through the normalizer, a buffer of characters
///     at a time; the normalized document is never held whole. It was up to three times the request, beside the
///     request and the parser's builder and string (evaluator 17: X10 and X13 at 140 to 146 MiB of 150).
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class JsonNormalizedWhileReadTests(TestContext context)
{
    // The characters the normalizer holds at a time; a token longer than this is written in pieces.
    private const int BufferChars = 16 * 1024;
    private static readonly IngestLimits s_limits = new();

    // What is sent, and what the parser is given for it.
    private static readonly (byte[] Sent, string Read)[] s_pieces =
    [
        ("é"u8.ToArray(), "é"),
        ("€"u8.ToArray(), "€"),
        ("\U0001F600"u8.ToArray(), "\U0001F600"),
        ("\\n"u8.ToArray(), "\\n"),
        ("\\\\"u8.ToArray(), "\\\\"),
        ("\\u00e9"u8.ToArray(), "\\u00e9"),
        ("\\uD83D\\uDE00"u8.ToArray(), "\\uD83D\\uDE00"),
        ("\\uD800"u8.ToArray(), "\\uFFFD"),
        ("\\uDC00\\uD83D\\u0041"u8.ToArray(), "\\uFFFD\\uFFFD\\u0041"),
        ([0x80], "�"),
        ([0xE2, 0x82], "�"),
        ([0xF0, 0x9F, 0x98, 0x5C, 0x6E], "�\\n")
    ];

    /// <summary>
    ///     Each thing a string can hold, at every place around the end of the buffer, as a string value and as a
    ///     member name: what is read is what was sent, a piece that ends one buffer and begins the next included.
    ///     The pieces: characters of two, three and four bytes, escapes, a pair of surrogate escapes, the escape of
    ///     an unpaired surrogate and bytes that are not UTF-8 (each U+FFFD).
    /// </summary>
    [TestMethod]
    [DataRow(false, DisplayName = "in a string value")]
    [DataRow(true, DisplayName = "in a member name")]
    public void WhateverLiesAcrossTheEndOfABuffer_IsReadAsItWasSent(bool asName)
    {
        var head = asName ? "{\"" : "{\"k\":\"";
        var tail = asName ? "\":[1,true,null]}" : "\",\"n\":[1,true,null]}";
        foreach (var (piece, read) in s_pieces)
        {
            for (var before = 0; before <= 16; before++)
            {
                // The piece is sent three times, so that one of them also follows itself across the end.
                var filler = new string('a', BufferChars - head.Length - before);
                byte[] sent = [.. Encoding.ASCII.GetBytes(head + filler), .. piece, .. piece, .. piece, .. Encoding.ASCII.GetBytes("z" + tail)];
                var expected = head + filler + read + read + read + "z" + tail;

                using var whole = OtlpJsonIdNormalizer.Read(sent);
                Assert.AreEqual(expected, whole.ReadToEnd(), $"{read}, {before} characters before the end of the buffer");
                Assert.AreEqual(expected, OneCharacterAtATime(sent));
            }
        }
    }

    /// <summary>A long text of every piece in turn, over many buffers.</summary>
    [TestMethod]
    public void ALongStringOfEveryPiece_IsReadAsItWasSent()
    {
        var sent = new List<byte>("{\"k\":\""u8.ToArray());
        var expected = new StringBuilder("{\"k\":\"");
        for (var i = 0; sent.Count < 12 * BufferChars; i++)
        {
            // At least one letter, so that no escape of one piece pairs with the escape of the next.
            var filler = new string('a', 1 + i * 7 % 13);
            var (piece, read) = s_pieces[i % s_pieces.Length];
            sent.AddRange(Encoding.ASCII.GetBytes(filler));
            sent.AddRange(piece);
            expected.Append(filler).Append(read);
        }

        sent.AddRange("\"}"u8.ToArray());
        expected.Append("\"}");

        using var whole = OtlpJsonIdNormalizer.Read(sent.ToArray());
        Assert.AreEqual(expected.ToString(), whole.ReadToEnd());
        Assert.AreEqual(expected.ToString(), OneCharacterAtATime(sent.ToArray()));
    }

    /// <summary>
    ///     A number is a token of any length too. An integer too long for a double is passed on as a string,
    ///     whatever its length; a number with a fraction stays the number it was.
    /// </summary>
    [TestMethod]
    public void ANumberLongerThanTheBuffer_IsWrittenInPieces()
    {
        var digits = new string('7', 3 * BufferChars + 5);
        var json = "{\"a\":[" + digits + ",-" + digits + "," + digits + ".5,0],\"traceId\":\"zz\"}";

        using var normalized = OtlpJsonIdNormalizer.Read(Encoding.ASCII.GetBytes(json));

        Assert.AreEqual("{\"a\":[\"" + digits + "\",\"-" + digits + "\"," + digits + ".5,0],\"traceId\":\"zz\"}",
            normalized.ReadToEnd());
    }

    /// <summary>
    ///     Reading 4 MiB of bytes that are not UTF-8, 12 MiB once replaced, through the normalizer allocates next to
    ///     nothing: the reader and its buffer.
    /// </summary>
    [TestMethod]
    public void TheNormalizedDocument_IsNeverHeldWhole()
    {
        const int Invalid = 4 * 1024 * 1024;
        byte[] json = [.. "{\"k\":\""u8, .. new byte[Invalid], .. "\"}"u8];
        json.AsSpan(6, Invalid).Fill(0x80);
        var buffer = new char[4096];
        // The pool's buffer is rented once before the measurement.
        OtlpJsonIdNormalizer.Read(json).Dispose();

        var before = GC.GetAllocatedBytesForCurrentThread();
        long read = 0, replaced = 0;
        using (var normalized = OtlpJsonIdNormalizer.Read(json))
        {
            for (var count = normalized.Read(buffer); count > 0; count = normalized.Read(buffer))
            {
                read += count;
                replaced += buffer.AsSpan(0, count).Count('�');
            }
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        context.WriteLine($"allocated {allocated} bytes to read {read} characters");
        Assert.AreEqual(Invalid + 8, read);
        Assert.AreEqual(Invalid, replaced);
        Assert.IsLessThan(64 * 1024, allocated);
    }

    /// <summary>
    ///     The parser stops at the first thing it refuses, and the normalizer has then not seen the rest. A request
    ///     the normalizer refuses is still refused for that: an id that is no hex, or a document that is no JSON,
    ///     far behind the place where the parser stopped. So a request is answered as it was when the whole
    ///     document was normalized before the parser read any of it.
    /// </summary>
    [TestMethod]
    [DataRow("\"traceId\":\"zz\"}]}]}]}", typeof(InvalidDataException), DisplayName = "an id that is no hex, behind what the parser refuses")]
    [DataRow("\"traceId\":\"\"}]}]}]}]", typeof(JsonException), DisplayName = "no JSON, behind what the parser refuses")]
    [DataRow("\"traceId\":\"\"}]}]}]}", null, DisplayName = "nothing the normalizer refuses: the parser's refusal")]
    public async Task WhatTheNormalizerRefuses_IsRefusedForThat_WhereverTheParserStopped(string end, Type? refusal)
    {
        // The parser refuses the time; the end of the document is several buffers further on.
        var json = Encoding.ASCII.GetBytes(
            "{\"resourceSpans\":[{\"scopeSpans\":[{\"spans\":[{\"startTimeUnixNano\":\"no number\",\"name\":\"" +
            new string('n', 5 * BufferChars) + "\"," + end);

        var thrown = await Assert.ThrowsAsync<Exception>(() => ParseAsync(json));

        context.WriteLine(thrown.GetType().FullName ?? "");
        if (refusal is null)
            Assert.IsInstanceOfType<Google.Protobuf.InvalidProtocolBufferException>(thrown);
        else
            Assert.IsTrue(refusal.IsInstanceOfType(thrown), $"{thrown.GetType()} is no {refusal}.");
    }

    /// <summary>
    ///     Ledger N92. An id is thirty-two hex characters at most. A longer token is refused on its length, counted
    ///     on its bytes: no string of it is built first, which was two bytes for each character of a token the
    ///     sender chose the length of. The refusal says what it said before, the characters the id had: one for a
    ///     character of up to three bytes, for an escape, for the escape of an unpaired surrogate and for each
    ///     sequence that is not UTF-8, two for a character of four bytes and for a pair of surrogate escapes.
    /// </summary>
    [TestMethod]
    [DataRow("ascii", 1, DisplayName = "letters")]
    [DataRow("two bytes", 1, DisplayName = "characters of two bytes")]
    [DataRow("four bytes", 2, DisplayName = "characters of four bytes")]
    [DataRow("not UTF-8", 11, DisplayName = "sequences that are not UTF-8")]
    [DataRow("escapes", 10, DisplayName = "escapes, pairs of them, unpaired surrogates and text between them")]
    public void AnIdLongerThanAnId_IsRefusedOnItsLength_AndNoStringOfItIsBuilt(string shape, int charactersEach)
    {
        const int Times = 512 * 1024;
        byte[] each = shape switch
        {
            "ascii" => "a"u8.ToArray(),
            "two bytes" => "\u00E9"u8.ToArray(),
            "four bytes" => "\U0001F600"u8.ToArray(),
            // One U+FFFD each: 80, E2 82, F0 9F 98 and FF, then C0 AF as two and ED A0 80 as three; and two letters.
            "not UTF-8" => [0x80, 0x61, 0xE2, 0x82, 0x61, 0xF0, 0x9F, 0x98, 0xFF, 0xC0, 0xAF, 0xED, 0xA0, 0x80],
            // a, a pair (two), an unpaired low and an unpaired high surrogate, a line feed, é, FF, E2 82 before an escape, a slash.
            _ => [.. "\\u0061\\uD83D\\uDE00\\uDC00\\uD800\\n\u00E9"u8, 0xFF, 0xE2, 0x82, .. "\\/"u8]
        };
        var characters = charactersEach * Times;
        var head = "{\"resourceSpans\":[{\"scopeSpans\":[{\"spans\":[{\"name\":\"s\",\"traceId\":\""u8;
        var json = new byte[head.Length + each.Length * Times + 1];
        head.CopyTo(json);
        for (var i = 0; i < Times; i++) each.CopyTo(json, head.Length + i * each.Length);
        json[^1] = (byte)'"';
        // The pool's buffer is rented once before the measurement.
        OtlpJsonIdNormalizer.Read(json).Dispose();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var refused = Assert.ThrowsExactly<InvalidDataException>(() =>
        {
            using var normalized = OtlpJsonIdNormalizer.Read(json);
            normalized.Skip();
        });
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        context.WriteLine($"allocated {allocated} bytes to refuse an id of {json.Length - head.Length - 1} bytes");
        Assert.AreEqual($"OTLP/JSON field 'traceId' must be 32 hex characters (16 bytes); got {characters} characters.", refused.Message);
        Assert.IsLessThan(64 * 1024, allocated);
    }

    /// <summary>
    ///     The length that is refused without a string is one no id has: an id spelled in escapes, six bytes for
    ///     each character, is still read, and one character more is refused as before.
    /// </summary>
    [TestMethod]
    public async Task AnIdSpelledInEscapes_IsStillRead()
    {
        var escaped = string.Concat("000102030405060708090a0b0c0d0e0f".Select(static digit => $"\\u{(int)digit:x4}"));
        string Json(string id) => "{\"resourceSpans\":[{\"scopeSpans\":[{\"spans\":[{\"traceId\":\"" + id + "\"}]}]}]}";

        var span = (await ParseAsync(Encoding.ASCII.GetBytes(Json(escaped)))).ResourceSpans[0].ScopeSpans[0].Spans[0];
        var refused = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => ParseAsync(Encoding.ASCII.GetBytes(Json(escaped + "\\u0030"))));

        Assert.AreEqual("AAECAwQFBgcICQoLDA0ODw==", span.TraceId.ToBase64());
        Assert.AreEqual("OTLP/JSON field 'traceId' must be 32 hex characters (16 bytes); got 33 characters.", refused.Message);
    }

    /// <summary>
    ///     The refusal leaves through the read that met it, and what was normalized before it in the same buffer is
    ///     not handed over afterwards: a reader after a refusal is at its end.
    /// </summary>
    [TestMethod]
    public void AfterARefusal_TheReaderGivesNothing()
    {
        using var normalized = OtlpJsonIdNormalizer.Read(
            "{\"k\":\"kept\",\"resourceSpans\":[{\"scopeSpans\":[{\"spans\":[{\"traceId\":\"zz\"}]}]}]}"u8.ToArray());

        Assert.ThrowsExactly<InvalidDataException>(() => normalized.Read());

        Assert.IsTrue(normalized.Refused);
        Assert.AreEqual(-1, normalized.Peek());
        Assert.AreEqual(-1, normalized.Read());
        Assert.AreEqual("", normalized.ReadToEnd());
    }

    private static string OneCharacterAtATime(byte[] sent)
    {
        using var normalized = OtlpJsonIdNormalizer.Read(sent);
        var read = new StringBuilder();
        while (normalized.Peek() is >= 0 and var next)
        {
            Assert.AreEqual(next, normalized.Read());
            read.Append((char)next);
        }

        Assert.AreEqual(-1, normalized.Read());
        return read.ToString();
    }

    private Task<ExportTraceServiceRequest> ParseAsync(byte[] json)
    {
        var request = new DefaultHttpContext().Request;
        request.Body = new MemoryStream(json, false);
        request.ContentLength = json.Length;
        return OtlpPayloadParser.ParseTraceRequestAsync(request, OtlpPayloadEncoding.Json, s_limits, context.CancellationToken);
    }
}
