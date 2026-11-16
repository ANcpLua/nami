using System.Buffers;
using System.Text;
using Google.Protobuf;
using Microsoft.AspNetCore.Http;
using Nami.Collector.Ingestion;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Trace.V1;
using LogRecord = OpenTelemetry.Proto.Logs.V1.LogRecord;

namespace Nami.IntegrationTests;

/// <summary>
///     E4b. The value bounds are applied to the bytes of an export before its objects are built
///     (<see cref="OtlpValueBounds" />). nami receiver policy: an array or key-value list that is an attribute value
///     or a log body is dropped whole when its protobuf encoding is longer than the value cap or it holds more
///     nested values than a fifth of the cap; one nested deeper than the bound becomes an empty value. A dropped
///     structure is replaced by the sentinel string the converter counts in <c>dropped_attributes_count</c>.
///     Everything within the bounds stays byte for byte as it was sent.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class ValueBoundsTests(TestContext context)
{
    private static readonly IngestLimits s_limits = new();
    private static readonly int s_cap = s_limits.MaxValueUtf8Bytes;
    private static readonly int s_maxNested = OtlpValueBounds.MaxNestedValues(s_limits);

    private static AnyValue Sentinel => new() { StringValue = OtlpValueBounds.OversizedValueSentinel };

    [TestMethod]
    public void TheBoundsAreTheObservers_AndTheSentinelIsTheConvertersChannel()
    {
        Assert.AreEqual(262_144, s_cap);
        Assert.AreEqual(52_428, s_maxNested);
        Assert.AreEqual(16, s_limits.MaxValueNesting);
        Assert.AreEqual("\0nami:value-over-the-cap", OtlpValueBounds.OversizedValueSentinel);
        // The padding of an emptied value is a field AnyValue does not define.
        Assert.IsNull(AnyValue.Descriptor.FindFieldByNumber(15));
    }

    /// <summary>Every kind of value at the bounds, not over them: nothing is touched, in either encoding.</summary>
    [TestMethod]
    public void AnExportWithinTheBounds_IsLeftByteForByte_AsProtobufAndAsJson()
    {
        var export = Export(
            Text("plain"), new AnyValue { IntValue = long.MinValue }, new AnyValue { DoubleValue = 1.5 },
            new AnyValue { BoolValue = true }, new AnyValue { BytesValue = ByteString.CopyFrom(new byte[2 * s_cap]) },
            Text(new string('s', 2 * s_cap)), new AnyValue(),
            Empties(s_maxNested), Nested(s_limits.MaxValueNesting, false, Text("bottom")),
            Nested(s_limits.MaxValueNesting, true, Text("bottom")),
            ArrayOfExactly(s_cap), Mixed());
        foreach (var value in export.ResourceSpans[0].ScopeSpans[0].Spans[0].Attributes)
            Assert.IsFalse(OtlpValueBounds.IsOversizedStructure(value.Value, s_limits), value.Key);

        var protobuf = export.ToByteArray();
        var bounded = protobuf.ToArray();
        Assert.AreEqual(default, OtlpValueBounds.BoundProtobuf(bounded, ExportTraceServiceRequest.Descriptor, s_limits));
        Assert.AreSequenceEqual(protobuf, bounded);

        var json = Encoding.UTF8.GetBytes(JsonFormatter.Default.Format(export));
        var boundedJson = json.ToArray();
        Assert.AreEqual(default, OtlpValueBounds.BoundJson(boundedJson, s_limits));
        Assert.AreSequenceEqual(json, boundedJson);
    }

    /// <summary>
    ///     One over a bound: the structure becomes the sentinel, in its own length, and parses as exactly that string;
    ///     the attributes around it and the rest of the export parse as they were sent.
    /// </summary>
    [TestMethod]
    [DataRow("count", false, DisplayName = "an array of one value more than fit")]
    [DataRow("count", true, DisplayName = "a key-value list of one member more than fit")]
    [DataRow("nested-count", false, DisplayName = "arrays in arrays, one value more than fit together")]
    [DataRow("bytes", false, DisplayName = "an array one byte longer than the cap")]
    [DataRow("bytes", true, DisplayName = "a key-value list one byte longer than the cap")]
    public void AStructureOneOverABound_BecomesTheSentinelInPlace_AndNothingElseChanges(string bound, bool keyed)
    {
        var over = bound switch
        {
            "count" => keyed ? Members(s_maxNested + 1) : Empties(s_maxNested + 1),
            "nested-count" => new AnyValue
            {
                ArrayValue = new ArrayValue { Values = { Empties(s_maxNested / 2), Empties(s_maxNested / 2 - 1) } }
            },
            _ => keyed ? MapOfExactly(s_cap + 1) : ArrayOfExactly(s_cap + 1)
        };
        Assert.IsTrue(OtlpValueBounds.IsOversizedStructure(over, s_limits));
        var export = Export(Text("before"), over, Mixed(), Text("after"));
        var expected = export.Clone();
        expected.ResourceSpans[0].ScopeSpans[0].Spans[0].Attributes[1].Value = Sentinel;

        var protobuf = export.ToByteArray();
        var length = protobuf.Length;
        Assert.AreEqual(new OtlpValueBoundsResult(1, 0),
            OtlpValueBounds.BoundProtobuf(protobuf, ExportTraceServiceRequest.Descriptor, s_limits));
        Assert.HasCount(length, protobuf);
        Assert.AreEqual(expected, ExportTraceServiceRequest.Parser.ParseFrom(protobuf));

        var json = Encoding.UTF8.GetBytes(JsonFormatter.Default.Format(export));
        length = json.Length;
        Assert.AreEqual(new OtlpValueBoundsResult(1, 0), OtlpValueBounds.BoundJson(json, s_limits));
        Assert.HasCount(length, json);
        Assert.AreEqual(expected, JsonParser.Default.Parse<ExportTraceServiceRequest>(Encoding.UTF8.GetString(json)));
    }

    /// <summary>
    ///     The three forms of one value agree at the byte: the protobuf scan, the JSON scan and the rule on the parsed
    ///     value say the same for a structure of exactly the cap and of one byte more, whatever it holds. The JSON
    ///     text of such a value is far longer than its protobuf encoding, and the scan counts the latter.
    /// </summary>
    [TestMethod]
    [DataRow("strings")]
    [DataRow("escapes")]
    [DataRow("integers")]
    [DataRow("doubles and booleans")]
    [DataRow("bytes")]
    [DataRow("members")]
    public void AStructureAtTheCapAndOneByteOver_IsJudgedAlike_OnTheWireInJsonAndParsed(string content)
    {
        foreach (var size in new[] { s_cap - 1, s_cap, s_cap + 1 })
        {
            var value = content == "members" ? MapOfExactly(size) : ArrayOfExactly(size, content);
            Assert.AreEqual(size, value.CalculateSize());
            Assert.IsLessThanOrEqualTo(s_maxNested, CountNested(value));
            var over = size > s_cap;
            Assert.AreEqual(over, OtlpValueBounds.IsOversizedStructure(value, s_limits), $"{content} {size} parsed");

            var export = Export(value);
            var replaced = new OtlpValueBoundsResult(over ? 1 : 0, 0);
            Assert.AreEqual(replaced,
                OtlpValueBounds.BoundProtobuf(export.ToByteArray(), ExportTraceServiceRequest.Descriptor, s_limits),
                $"{content} {size} protobuf");
            var json = Encoding.UTF8.GetBytes(JsonFormatter.Default.Format(export));
            Assert.AreEqual(replaced, OtlpValueBounds.BoundJson(json, s_limits), $"{content} {size} JSON");
        }
    }

    /// <summary>
    ///     A structure nested deeper than the bound becomes an empty value where it starts; what is above it stays.
    ///     Two hundred levels are more than Google.Protobuf parses at all, so without this step the whole export
    ///     was refused.
    /// </summary>
    [TestMethod]
    [DataRow(17, false, DisplayName = "arrays, one level too deep")]
    [DataRow(17, true, DisplayName = "key-value lists, one level too deep")]
    [DataRow(200, false, DisplayName = "arrays, 200 levels")]
    [DataRow(200, true, DisplayName = "key-value lists, 200 levels")]
    public void AStructureNestedPastTheBound_BecomesAnEmptyValue_AndTheExportParses(int levels, bool keyed)
    {
        // Written by hand as bytes and as text: Google.Protobuf does not serialize what it could not parse back.
        var protobuf = ExportWire(Text("before").ToByteArray(), NestedWire(levels, keyed), Text("after").ToByteArray());
        var expected = Export(Text("before"), Nested(s_limits.MaxValueNesting, keyed, new AnyValue()), Text("after"));
        var length = protobuf.Length;

        Assert.AreEqual(new OtlpValueBoundsResult(0, 1),
            OtlpValueBounds.BoundProtobuf(protobuf, ExportTraceServiceRequest.Descriptor, s_limits));
        Assert.HasCount(length, protobuf);
        // The emptied value keeps its length as a field the parser reads no value from.
        Assert.AreEqual(expected, ExportTraceServiceRequest.Parser.WithDiscardUnknownFields(true).ParseFrom(protobuf));

        var json = Encoding.UTF8.GetBytes(JsonFormatter.Default.Format(Export(Text("before"), Text("\u0001"), Text("after")))
            .Replace(JsonFormatter.Default.Format(Text("\u0001")), NestedJson(levels, keyed), StringComparison.Ordinal));
        length = json.Length;
        Assert.AreEqual(new OtlpValueBoundsResult(0, 1), OtlpValueBounds.BoundJson(json, s_limits));
        Assert.HasCount(length, json);
        Assert.AreEqual(expected, JsonParser.Default.Parse<ExportTraceServiceRequest>(Encoding.UTF8.GetString(json)));
    }

    /// <summary>At the bound itself nothing changes: sixteen levels of structures, a value at the bottom.</summary>
    [TestMethod]
    [DataRow(false, DisplayName = "arrays")]
    [DataRow(true, DisplayName = "key-value lists")]
    public void AStructureNestedToTheBound_IsLeftByteForByte(bool keyed)
    {
        var protobuf = ExportWire(Text("before").ToByteArray(), NestedWire(s_limits.MaxValueNesting, keyed),
            Text("after").ToByteArray());
        var untouched = protobuf.ToArray();

        Assert.AreEqual(default, OtlpValueBounds.BoundProtobuf(protobuf, ExportTraceServiceRequest.Descriptor, s_limits));
        Assert.AreSequenceEqual(untouched, protobuf);
        Assert.AreEqual(Export(Text("before"), Nested(s_limits.MaxValueNesting, keyed, Text("bottom")), Text("after")),
            ExportTraceServiceRequest.Parser.ParseFrom(protobuf));
    }

    /// <summary>A log body is a value like an attribute's: over the cap it becomes the sentinel, too deep an empty value.</summary>
    [TestMethod]
    public void ALogBody_IsHeldToTheSameBounds()
    {
        var export = new ExportLogsServiceRequest
        {
            ResourceLogs = { new ResourceLogs { ScopeLogs = { new ScopeLogs { LogRecords =
            {
                new LogRecord { Body = Empties(s_maxNested + 1), SeverityText = "INFO" },
                new LogRecord { Body = Empties(s_maxNested), SeverityText = "WARN" }
            } } } } }
        };
        var expected = export.Clone();
        expected.ResourceLogs[0].ScopeLogs[0].LogRecords[0].Body = Sentinel;

        var protobuf = export.ToByteArray();
        Assert.AreEqual(new OtlpValueBoundsResult(1, 0),
            OtlpValueBounds.BoundProtobuf(protobuf, ExportLogsServiceRequest.Descriptor, s_limits));
        Assert.AreEqual(expected, ExportLogsServiceRequest.Parser.ParseFrom(protobuf));

        var json = Encoding.UTF8.GetBytes(JsonFormatter.Default.Format(export));
        Assert.AreEqual(new OtlpValueBoundsResult(1, 0), OtlpValueBounds.BoundJson(json, s_limits));
        Assert.AreEqual(expected, JsonParser.Default.Parse<ExportLogsServiceRequest>(Encoding.UTF8.GetString(json)));
    }

    /// <summary>
    ///     What a sender can do to get past the scan must not work: a structure that a later member of the oneof
    ///     replaces is built by the parser before it is discarded, and a JSON property name may be spelled with
    ///     escapes or in the proto spelling.
    /// </summary>
    [TestMethod]
    public void AStructureTheParserWouldBuildAndDiscard_OrOneUnderAnotherSpelling_IsCountedToo()
    {
        byte[] hidden = [.. Empties(s_maxNested + 1).ToByteArray(), .. Text("the value the parser ends with").ToByteArray()];
        var protobuf = ExportWire(hidden);
        Assert.AreEqual(Text("the value the parser ends with"),
            ExportTraceServiceRequest.Parser.ParseFrom(protobuf).ResourceSpans[0].ScopeSpans[0].Spans[0].Attributes[0].Value);
        Assert.AreEqual(new OtlpValueBoundsResult(1, 0),
            OtlpValueBounds.BoundProtobuf(protobuf, ExportTraceServiceRequest.Descriptor, s_limits));
        Assert.AreEqual(Export(Sentinel), ExportTraceServiceRequest.Parser.ParseFrom(protobuf));

        var plain = JsonFormatter.Default.Format(Export(Empties(s_maxNested + 1)));
        foreach (var spelling in new[] { "\"array_value\"", "\"\\u0061rrayValue\"", "\"arrayV\\u0061lue\"" })
        {
            var json = Encoding.UTF8.GetBytes(plain.Replace("\"arrayValue\"", spelling, StringComparison.Ordinal));
            Assert.AreEqual(new OtlpValueBoundsResult(1, 0), OtlpValueBounds.BoundJson(json, s_limits), spelling);
            Assert.AreEqual(Export(Sentinel), JsonParser.Default.Parse<ExportTraceServiceRequest>(Encoding.UTF8.GetString(json)));
        }
    }

    /// <summary>
    ///     Protobuf bytes the scan cannot read to the end are refused, so nothing it did not count reaches the parser;
    ///     JSON that is no well-formed document is left alone, so the parser's own failure stands.
    /// </summary>
    [TestMethod]
    public void AMalformedPayload_IsRefusedAsProtobuf_AndLeftAsItIsAsJson()
    {
        var protobuf = Export(Empties(s_maxNested + 1)).ToByteArray()[..^3];
        _ = Assert.ThrowsExactly<OtlpMalformedPayloadException>(() =>
            OtlpValueBounds.BoundProtobuf(protobuf, ExportTraceServiceRequest.Descriptor, s_limits));

        var json = Encoding.UTF8.GetBytes("{\"resourceSpans\":[{\"scopeSpans\":[{\"spans\":[{\"attributes\":[{\"key\":\"k\",\"value\":{\"arrayValue\":{\"values\":[{},{}");
        var untouched = json.ToArray();
        Assert.AreEqual(default, OtlpValueBounds.BoundJson(json, s_limits));
        Assert.AreSequenceEqual(untouched, json);
    }

    /// <summary>
    ///     Engine audit A3 at the parser, where the cost can be counted without measuring a process: one attribute of
    ///     a million empty values. Built, they are a million objects and some fifty megabytes; bounded first, the
    ///     export costs a few copies of its own bytes. Over OTLP/HTTP protobuf, OTLP/HTTP JSON and OTLP/gRPC.
    /// </summary>
    [TestMethod]
    [DataRow("protobuf")]
    [DataRow("json")]
    [DataRow("grpc")]
    public async Task AnAttributeOfAMillionEmptyValues_IsNeverBuilt(string transport)
    {
        const int Values = 1_000_000;
        var export = Export(Text("before"), Empties(Values), Text("after"));
        var expected = Export(Text("before"), Sentinel, Text("after"));
        var payload = transport == "json" ? OtlpJson(export) : export.ToByteArray();
        ExportTraceServiceRequest parsed;
        long allocated;
        if (transport == "grpc")
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            parsed = OtlpPayloadParser.ParseProtobuf(ExportTraceServiceRequest.Parser, ExportTraceServiceRequest.Descriptor,
                new ReadOnlySequence<byte>(payload), s_limits);
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        else
        {
            var request = new DefaultHttpContext().Request;
            request.Body = new MemoryStream(payload, false);
            request.ContentLength = payload.Length;
            var before = GC.GetAllocatedBytesForCurrentThread();
            var parse = OtlpPayloadParser.ParseTraceRequestAsync(request,
                transport == "json" ? OtlpPayloadEncoding.Json : OtlpPayloadEncoding.Protobuf, s_limits,
                context.CancellationToken);
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(parse.IsCompleted, "A MemoryStream completes every read synchronously, so the count covers the parse.");
            parsed = await parse;
        }

        Assert.AreEqual(expected, parsed);
        Assert.IsLessThan(8L * payload.Length, allocated, "A few copies of the payload, not an object per value.");
        Assert.IsLessThan(24L * Values, allocated, "Fewer bytes than the smallest objects of a million values take.");
    }

    // ------------------------------------------------------------------------------------------------ values

    private static ExportTraceServiceRequest Export(params AnyValue[] values)
    {
        var span = new Span
        {
            TraceId = ByteString.CopyFrom(Enumerable.Repeat((byte)7, 16).ToArray()),
            SpanId = ByteString.CopyFrom(Enumerable.Repeat((byte)7, 8).ToArray()),
            Name = "span", StartTimeUnixNano = 1_794_745_800_000_000_000, EndTimeUnixNano = 1_794_745_800_000_000_001
        };
        for (var i = 0; i < values.Length; i++) span.Attributes.Add(new KeyValue { Key = "acme.value." + i, Value = values[i] });
        return new ExportTraceServiceRequest
        {
            ResourceSpans = { new ResourceSpans { ScopeSpans = { new ScopeSpans { Spans = { span } } } } }
        };
    }

    // The export as OTLP/JSON: the protobuf JSON mapping, with trace and span ids in hex where it writes base64.
    private static byte[] OtlpJson(ExportTraceServiceRequest export)
    {
        var span = export.ResourceSpans[0].ScopeSpans[0].Spans[0];
        return Encoding.UTF8.GetBytes(JsonFormatter.Default.Format(export)
            .Replace(span.TraceId.ToBase64(), Convert.ToHexStringLower(span.TraceId.Span), StringComparison.Ordinal)
            .Replace(span.SpanId.ToBase64(), Convert.ToHexStringLower(span.SpanId.Span), StringComparison.Ordinal));
    }

    private static AnyValue Text(string text) => new() { StringValue = text };

    private static AnyValue Empties(int count)
    {
        var value = new AnyValue { ArrayValue = new ArrayValue() };
        for (var i = 0; i < count; i++) value.ArrayValue.Values.Add(new AnyValue());
        return value;
    }

    private static AnyValue Members(int count)
    {
        var value = new AnyValue { KvlistValue = new KeyValueList() };
        for (var i = 0; i < count; i++) value.KvlistValue.Values.Add(new KeyValue { Key = "k" });
        return value;
    }

    // levels structures inside one another, with bottom in the innermost.
    private static AnyValue Nested(int levels, bool keyed, AnyValue bottom)
    {
        var value = bottom;
        for (var i = 0; i < levels; i++)
        {
            value = keyed
                ? new AnyValue { KvlistValue = new KeyValueList { Values = { new KeyValue { Key = "k", Value = value } } } }
                : new AnyValue { ArrayValue = new ArrayValue { Values = { value } } };
        }

        return value;
    }

    // The protobuf encoding of Nested(levels, keyed, "bottom").
    private static byte[] NestedWire(int levels, bool keyed)
    {
        var body = Text("bottom").ToByteArray();
        for (var i = 0; i < levels; i++)
        {
            // AnyValue { array_value = 5 { values = 1 } }, AnyValue { kvlist_value = 6 { values = 1 { key = 1, value = 2 } } }.
            body = keyed
                ? Field(6, Field(1, [.. Field(1, "k"u8.ToArray()), .. Field(2, body)]))
                : Field(5, Field(1, body));
        }

        return body;
    }

    // The same value as JsonFormatter writes it.
    private static string NestedJson(int levels, bool keyed)
    {
        var text = new StringBuilder(JsonFormatter.Default.Format(Text("bottom")));
        for (var i = 0; i < levels; i++)
        {
            _ = keyed
                ? text.Insert(0, "{ \"kvlistValue\": { \"values\": [ { \"key\": \"k\", \"value\": ").Append(" } ] } }")
                : text.Insert(0, "{ \"arrayValue\": { \"values\": [ ").Append(" ] } }");
        }

        return text.ToString();
    }

    // The export of Export(values), with each value given as the bytes of its AnyValue.
    private static byte[] ExportWire(params byte[][] values)
    {
        var span = Export().ResourceSpans[0].ScopeSpans[0].Spans[0].ToByteArray().ToList();
        for (var i = 0; i < values.Length; i++)
        {
            // Span { attributes = 9 { key = 1, value = 2 } }.
            span.AddRange(Field(9, [.. Field(1, Encoding.UTF8.GetBytes("acme.value." + i)), .. Field(2, values[i])]));
        }

        // Export { resource_spans = 1 { scope_spans = 2 { spans = 2 } } }.
        return Field(1, Field(2, Field(2, [.. span])));
    }

    // One length-delimited field: tag, length, content.
    private static byte[] Field(int number, byte[] content)
    {
        List<byte> field = [(byte)((number << 3) | 2)];
        for (var rest = (uint)content.Length;; rest >>= 7)
        {
            if (rest < 0x80)
            {
                field.Add((byte)rest);
                break;
            }

            field.Add((byte)(rest | 0x80));
        }

        field.AddRange(content);
        return [.. field];
    }

    private static AnyValue Mixed() => new()
    {
        KvlistValue = new KeyValueList
        {
            Values =
            {
                new KeyValue { Key = "text", Value = Text("line one\nline \"two\" \u00e4\u20ac\ud83d\ude00") },
                new KeyValue { Key = "", Value = new AnyValue { IntValue = -1 } },
                new KeyValue { Key = "list", Value = new AnyValue { ArrayValue = new ArrayValue
                {
                    Values = { new AnyValue(), new AnyValue { BoolValue = false }, new AnyValue { DoubleValue = double.NaN },
                        new AnyValue { BytesValue = ByteString.CopyFromUtf8("bytes") }, Text("") }
                } } },
                new KeyValue { Key = "absent" }
            }
        }
    };

    // An array whose protobuf encoding is exactly size bytes: values of the given kind, then one string that fills
    // what is left.
    private static AnyValue ArrayOfExactly(int size, string content = "strings")
    {
        var value = new AnyValue { ArrayValue = new ArrayValue() };
        var items = value.ArrayValue.Values;
        for (var i = 0; i < 2000; i++)
        {
            items.Add(content switch
            {
                "escapes" => Text("tab\t quote\" backslash\\ newline\n \u0001 \u00e4 \u20ac \ud83d\ude00 <&>"),
                "integers" => new AnyValue { IntValue = i % 2 == 0 ? -i : (long)i << (i % 60) },
                "doubles and booleans" => i % 2 == 0 ? new AnyValue { DoubleValue = i * 0.5 } : new AnyValue { BoolValue = i % 3 == 0 },
                "bytes" => new AnyValue { BytesValue = ByteString.CopyFrom(new byte[i % 7]) },
                _ => Text(new string((char)('a' + i % 26), i % 40))
            });
        }

        return Filled(value, items.Add, size);
    }

    private static AnyValue MapOfExactly(int size)
    {
        var value = new AnyValue { KvlistValue = new KeyValueList() };
        var members = value.KvlistValue.Values;
        for (var i = 0; i < 2000; i++)
        {
            members.Add(i % 5 == 0
                ? new KeyValue { Key = i % 10 == 0 ? "" : "bare-" + i }
                : new KeyValue { Key = "k\u00e4y-" + i, Value = i % 2 == 0 ? Text("v" + i) : new AnyValue { IntValue = i } });
        }

        return Filled(value, filler => members.Add(new KeyValue { Key = "fill", Value = filler }), size);
    }

    // Adds one string so that value encodes to exactly size bytes.
    private static AnyValue Filled(AnyValue value, Action<AnyValue> add, int size)
    {
        var filler = Text("");
        add(filler);
        for (var attempt = 0; attempt < 8 && value.CalculateSize() != size; attempt++)
            filler.StringValue = new string('f', Math.Max(0, filler.StringValue.Length + size - value.CalculateSize()));
        Assert.AreEqual(size, value.CalculateSize());
        return value;
    }

    private static int CountNested(AnyValue value) => value.ValueCase switch
    {
        AnyValue.ValueOneofCase.ArrayValue => value.ArrayValue.Values.Sum(static item => 1 + CountNested(item)),
        AnyValue.ValueOneofCase.KvlistValue => value.KvlistValue.Values.Sum(static member =>
            1 + (member.Value is null ? 0 : CountNested(member.Value))),
        _ => 0
    };
}
