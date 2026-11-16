using System.Text;
using Google.Protobuf;
using Microsoft.AspNetCore.Http;
using Nami.Collector.Ingestion;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Nami.IntegrationTests;

/// <summary>
///     Ledger N80. On the way to Google.Protobuf's JSON parser a string and a member name are the token that was
///     sent, with no encoder in between. System.Text.Json's writer escapes a character outside the Basic
///     Multilingual Plane (four bytes) as twelve bytes under every encoder it has, so 16 MiB of them behind one
///     escape, or as a member name, cost over 200 MiB (evaluator 15, probes X9b and X12).
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class JsonStringCopyTests(TestContext context)
{
    private const int Large = 4 * 1024 * 1024;
    private const string FourBytes = "\U0001F600";
    private static readonly IngestLimits s_limits = new();

    /// <summary>
    ///     The pin of "no encoder": what needs no replacement leaves the normalizer byte for byte. Any writer of
    ///     System.Text.Json fails the first two rows (the four-byte character becomes \uD83D\uDE00), its default
    ///     encoder also the punctuation and the é, and a pass through a string the third (the escapes are resolved).
    /// </summary>
    [TestMethod]
    [DataRow("{\"k\":\"\\n\U0001F600<>&'+\u00E9\"}", DisplayName = "a string with an escape, a four-byte character and punctuation")]
    [DataRow("{\"\\t\U0001F600<>&'+\u00E9\":[1,true,null]}", DisplayName = "a member name with the same")]
    [DataRow("{\"na\\u006de\":\"\\uD83D\\uDE00\\/\\\"\\\\\\b\\f\\n\\r\\t\\u00e9\"}", DisplayName = "every escape stays the escape it was")]
    public void WhatNeedsNoReplacement_LeavesTheNormalizerByteForByte(string json)
    {
        var sent = Encoding.UTF8.GetBytes(json);

        var normalized = OtlpJsonIdNormalizer.NormalizeIdsToProtoJsonUtf8(sent);

        Assert.AreEqual(json, Encoding.UTF8.GetString(normalized));
        CollectionAssert.AreEqual(sent, normalized.ToArray());
    }

    /// <summary>
    ///     The copied token means what the sender wrote: the parser resolves the escapes, a member name spelled with
    ///     an escape is still the member, an id below such a name is still read as hex, and the two things the
    ///     parser would refuse the payload for (the escape of an unpaired surrogate, bytes that are not UTF-8)
    ///     arrive as U+FFFD.
    /// </summary>
    [TestMethod]
    public async Task AnEscapedString_ArrivesAsTheCharactersItStandsFor()
    {
        var head = "{\"resource\\u0053pans\":[{\"scopeSpans\":[{\"spans\":[{\"trace\\u0049d\":\"000102030405060708090a0b0c0d0e0f\",\"na\\u006de\":\"\\\"\\\\\\/\\b\\f\\n\\r\\t\\u00e9\\uD83D\\uDE00\","u8;
        var value = "\"attributes\":[{\"key\":\"lone \\uD800 \\uDC00\\uD800 \\uD83Dx\",\"value\":{\"stringValue\":\"\\u0041"u8;
        var tail = "\\uDBFF\"}}]}]}]}]}"u8;
        byte[] json = [.. head, .. value, 0xFF, 0xC3, .. tail];

        var span = (await ParseAsync(json)).ResourceSpans[0].ScopeSpans[0].Spans[0];

        Assert.AreEqual("\"\\/\b\f\n\r\t\u00E9" + FourBytes, span.Name);
        Assert.AreEqual("AAECAwQFBgcICQoLDA0ODw==", span.TraceId.ToBase64(), "Hex, although the member name carries an escape.");
        Assert.AreEqual("lone \uFFFD \uFFFD\uFFFD \uFFFDx", span.Attributes[0].Key);
        Assert.AreEqual("A\uFFFD\uFFFD\uFFFD", span.Attributes[0].Value.StringValue);
    }

    /// <summary>
    ///     The two shapes of the probes, at 4 MiB. Read into a buffer the request costs about two of its length,
    ///     normalized one more, and the parser's builder and string one more each for a four-byte character (two
    ///     UTF-16 units). Through the encoder the same requests cost over fifteen times their length.
    /// </summary>
    [TestMethod]
    [DataRow(false, DisplayName = "one escape, then four-byte characters, in a string value")]
    [DataRow(true, DisplayName = "four-byte characters as the name of an unknown member")]
    public async Task FourByteCharactersBehindAnEscapeOrInAName_CostAFewCopies(bool asName)
    {
        var text = string.Concat(Enumerable.Repeat(FourBytes, Large / 4));
        var json = Encoding.UTF8.GetBytes(asName
            ? "{\"resourceSpans\":[],\"" + text + "\":[]}"
            : "{\"resourceSpans\":[{\"scopeSpans\":[{\"spans\":[{\"name\":\"\\n" + text + "\"}]}]}]}");

        var before = GC.GetAllocatedBytesForCurrentThread();
        var parsed = await ParseAsync(json);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        context.WriteLine($"allocated {allocated} bytes for a request of {json.Length}: {(double)allocated / json.Length:F1} times");
        if (!asName) Assert.AreEqual("\n" + text, parsed.ResourceSpans[0].ScopeSpans[0].Spans[0].Name);
        Assert.IsLessThan(7L * json.Length, allocated);
    }

    private Task<ExportTraceServiceRequest> ParseAsync(byte[] json)
    {
        var request = new DefaultHttpContext().Request;
        request.Body = new MemoryStream(json, false);
        request.ContentLength = json.Length;
        return OtlpPayloadParser.ParseTraceRequestAsync(request, OtlpPayloadEncoding.Json, s_limits, context.CancellationToken);
    }
}
