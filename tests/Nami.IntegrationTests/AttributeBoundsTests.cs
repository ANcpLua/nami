using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Nami.Collector.Ingestion;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using LogRecord = OpenTelemetry.Proto.Logs.V1.LogRecord;

namespace Nami.IntegrationTests;

/// <summary>
///     The size bounds, the second of the two things ingest does to a record (observer's rulings, 16 Nov 2026): 256
///     KiB per attribute value, 128 attributes per record, 16 levels of nesting, 1 MiB of attributes per record.
///     The limits are nami's receiver policy; the attribute limits of the OpenTelemetry Common Specification are
///     written for SDKs and are a reference point only. nami differs from them in five points: UTF-8 bytes instead
///     of characters, depth 16 instead of 64, the record's megabyte also on resource and metric attributes, a
///     deterministic reduction with priority keys first, and its own in-band marker on a cut value. From the
///     specification nami takes the data model (https://opentelemetry.io/docs/specs/otel/common/#anyvalue), and it
///     stores all of it as sent: empty values, zero, empty strings, empty arrays, a null inside an array, key case.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class AttributeBoundsTests
{
    private const int Cap = OtlpConverter.MaxValueUtf8Bytes;
    private const string Marker = "…[truncated]";
    private const int MarkerBytes = 14;
    private const ulong Time = 1_794_745_800_000_000_000;

    /// <summary>
    ///     E1. A string over the cap is stored as exactly 262,144 bytes: 262,130 of the sender's text and the 14
    ///     bytes of the marker. It is cut between characters, never inside one. One cap for every key.
    /// </summary>
    [TestMethod]
    public void Value_AStringOverTheCap_IsStoredAsExactlyTheCap_CutBetweenCharacters()
    {
        Assert.AreEqual(MarkerBytes, Encoding.UTF8.GetByteCount(Marker));
        var atTheCap = new string('a', Cap);
        var span = Convert(Text("acme.at_the_cap", atTheCap), Text("acme.one_past", new string('a', Cap + 1)));
        Assert.AreEqual(atTheCap, span.Attributes["acme.at_the_cap"].AsString(), "a value of exactly the cap is kept whole");
        var cut = span.Attributes["acme.one_past"].AsString()!;
        Assert.AreEqual(new string('a', Cap - MarkerBytes) + Marker, cut);
        Assert.AreEqual(262_144, Encoding.UTF8.GetByteCount(cut));

        // A two-byte character straddles the last byte of the sender's text: it goes whole.
        var straddling = Convert(Text("exception.message", new string('a', Cap - MarkerBytes - 1) + "é" + new string('a', 100)))
            .Attributes["exception.message"].AsString()!;
        Assert.AreEqual(new string('a', Cap - MarkerBytes - 1) + Marker, straddling);
        Assert.AreEqual(Cap - 1, Encoding.UTF8.GetByteCount(straddling));
        Assert.IsFalse(straddling.Contains('\uFFFD'));
        Assert.AreEqual(0U, span.DroppedAttributesCount, "a cut value is no dropped attribute");
    }

    /// <summary>
    ///     The cut never writes half a code point: a character of two, three or four bytes (the last a surrogate
    ///     pair) that straddles the last byte of the payload goes whole, and one that just fits stays.
    /// </summary>
    [TestMethod]
    [DataRow("é", 2, DisplayName = "A two-byte character")]
    [DataRow("€", 3, DisplayName = "A three-byte character")]
    [DataRow("😀", 4, DisplayName = "A four-byte character, a surrogate pair")]
    public void Value_ACharacterAcrossTheCut_GoesWhole_AndOneThatFitsStays(string character, int bytes)
    {
        Assert.AreEqual(bytes, Encoding.UTF8.GetByteCount(character));
        const int payload = Cap - MarkerBytes;
        for (var room = 1; room < bytes; room++)
        {
            var cut = Convert(Text("acme.text", new string('a', payload - room) + character + new string('a', 100)))
                .Attributes["acme.text"].AsString()!;
            Assert.AreEqual(new string('a', payload - room) + Marker, cut, $"{room} of {bytes} bytes would fit");
            Assert.AreEqual(Cap - room, Encoding.UTF8.GetByteCount(cut));
        }

        var kept = Convert(Text("acme.text", new string('a', payload - bytes) + character + new string('a', 100)))
            .Attributes["acme.text"].AsString()!;
        Assert.AreEqual(new string('a', payload - bytes) + character + Marker, kept);
        Assert.AreEqual(262_144, Encoding.UTF8.GetByteCount(kept));
    }

    /// <summary>
    ///     A lone surrogate is no character. The JSON writer does not refuse it: it writes the escape of U+FFFD in
    ///     its place and says nothing. Ingest therefore replaces it itself, before anything else reads the text, and
    ///     counts the replacement; the three bytes of U+FFFD count toward the value cap.
    /// </summary>
    [TestMethod]
    public void Value_ALoneSurrogate_BecomesTheReplacementCharacter_OnAValueAKeyAndACutValue()
    {
        const string lone = "a\uD800b\uDC00";
        var written = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(written))
            writer.WriteStringValue(lone);
        Assert.AreEqual("\"a\\uFFFDb\\uFFFD\"", Encoding.UTF8.GetString(written.WrittenSpan), "the writer replaces silently");

        var span = Convert(Text("acme.lone", lone), Text("acme.\uD83D", "key"), Text("acme.pair", "ok 😀"),
            Text("acme.long", new string('\uDFFF', Cap)));

        Assert.AreEqual("a\uFFFDb\uFFFD", span.Attributes["acme.lone"].AsString());
        Assert.AreEqual("key", span.Attributes["acme.\uFFFD"].AsString());
        Assert.AreEqual("ok 😀", span.Attributes["acme.pair"].AsString(), "a whole pair is a character and stays");
        var cut = span.Attributes["acme.long"].AsString()!;
        Assert.AreEqual(new string('\uFFFD', (Cap - MarkerBytes) / 3) + Marker, cut, "each replacement counts three bytes");
        Assert.IsLessThanOrEqualTo(Cap, Encoding.UTF8.GetByteCount(cut));
        Assert.IsNotNull(Nami.Collector.Storage.IngestionStorageMapper.ToSpanStorageRows(Batch(
            Text("acme.lone", lone), Text("acme.\uD83D", "key")))[0].AttributesJson, "the row can be written");
    }

    /// <summary>
    ///     The record's megabyte is measured on what nami stores: JSON with its escaping, Base64, keys and
    ///     structure. A value within its own cap can therefore be too large for the record. A bytes value of 262,144
    ///     bytes is 349,528 characters of Base64, so the third does not fit; 262,144 control characters are six
    ///     bytes each in JSON. Both are dropped and counted, and what is stored stays within the megabyte.
    /// </summary>
    [TestMethod]
    public void Record_IsMeasuredAsStored_Base64AndEscapesCount()
    {
        var bytes = ByteString.CopyFrom(new byte[Cap]);
        var three = Batch(new KeyValue { Key = "acme.a", Value = new AnyValue { BytesValue = bytes } },
            new KeyValue { Key = "acme.b", Value = new AnyValue { BytesValue = bytes } },
            new KeyValue { Key = "acme.c", Value = new AnyValue { BytesValue = bytes } });
        var span = Assert.ContainsSingle(three.Spans);
        Assert.IsTrue(span.Attributes.ContainsKey("acme.a") && span.Attributes.ContainsKey("acme.b"));
        Assert.IsFalse(span.Attributes.ContainsKey("acme.c"), "of equal sizes the first keys in ordinal order are kept");
        Assert.AreEqual(1U, span.DroppedAttributesCount);
        var row = Nami.Collector.Storage.IngestionStorageMapper.ToSpanStorageRows(three)[0];
        Assert.IsLessThanOrEqualTo(OtlpConverter.MaxRecordAttributeBytes, Encoding.UTF8.GetByteCount(row.AttributesJson!));

        var control = Convert(Text("acme.control", new string('\u0001', Cap)), Text("acme.small", "fits"));
        Assert.IsFalse(control.Attributes.ContainsKey("acme.control"), "1,572,864 bytes of JSON do not fit one record");
        Assert.AreEqual("fits", control.Attributes["acme.small"].AsString());
        Assert.AreEqual(1U, control.DroppedAttributesCount);
    }

    /// <summary>
    ///     The bytes the converter charges to a record are the bytes storage writes for it, for every kind of value
    ///     and every character the writer escapes. If the two drift apart, the megabyte is no longer the megabyte.
    /// </summary>
    [TestMethod]
    public void Record_TheBytesChargedByTheConverter_AreTheBytesStorageWrites()
    {
        AnyValue S(string text) => new() { StringValue = text };
        var nested = new AnyValue
        {
            KvlistValue = new KeyValueList
            {
                Values =
                {
                    new KeyValue { Key = "quote\"key", Value = S("v") }, new KeyValue { Key = "empty" },
                    new KeyValue { Key = "list", Value = new AnyValue { ArrayValue = new ArrayValue { Values = { S("a"), new AnyValue(), new AnyValue { IntValue = -5 }, new AnyValue { KvlistValue = new KeyValueList() } } } } }
                }
            }
        };
        KeyValue[] sent =
        [
            Text("acme.plain", "GET /orders/42?page=2"), Text("acme.escaped", "say \"hi\" \\ <b> & 'x' + `y`"),
            Text("acme.control", "tab\tline\nbell\u0007del\u007F"), Text("acme.latin", "Grüße aus Wien"),
            Text("acme.cjk", "日本語"), Text("acme.emoji", "ok 😀"), Text("acme.empty", ""), Text("key with \"quote\" and é", "v"),
            new KeyValue { Key = "acme.none" }, new KeyValue { Key = "acme.true", Value = new AnyValue { BoolValue = true } },
            new KeyValue { Key = "acme.false", Value = new AnyValue { BoolValue = false } },
            new KeyValue { Key = "acme.zero", Value = new AnyValue { IntValue = 0 } },
            new KeyValue { Key = "acme.min", Value = new AnyValue { IntValue = long.MinValue } },
            new KeyValue { Key = "acme.max", Value = new AnyValue { IntValue = long.MaxValue } },
            new KeyValue { Key = "acme.tenth", Value = new AnyValue { DoubleValue = 0.1 } },
            new KeyValue { Key = "acme.whole", Value = new AnyValue { DoubleValue = 5 } },
            new KeyValue { Key = "acme.huge", Value = new AnyValue { DoubleValue = 1e300 } },
            new KeyValue { Key = "acme.tiny", Value = new AnyValue { DoubleValue = -4.9e-324 } },
            new KeyValue { Key = "acme.nan", Value = new AnyValue { DoubleValue = double.NaN } },
            new KeyValue { Key = "acme.inf", Value = new AnyValue { DoubleValue = double.PositiveInfinity } },
            new KeyValue { Key = "acme.ninf", Value = new AnyValue { DoubleValue = double.NegativeInfinity } },
            new KeyValue { Key = "acme.bytes0", Value = new AnyValue { BytesValue = ByteString.Empty } },
            new KeyValue { Key = "acme.bytes1", Value = new AnyValue { BytesValue = ByteString.CopyFrom(251) } },
            new KeyValue { Key = "acme.bytes2", Value = new AnyValue { BytesValue = ByteString.CopyFrom(251, 255) } },
            new KeyValue { Key = "acme.bytes3", Value = new AnyValue { BytesValue = ByteString.CopyFrom(251, 255, 62) } },
            new KeyValue { Key = "acme.array", Value = new AnyValue { ArrayValue = new ArrayValue { Values = { S("a\"b"), S(""), new AnyValue { DoubleValue = 2.5 } } } } },
            new KeyValue { Key = "acme.empty_array", Value = new AnyValue { ArrayValue = new ArrayValue() } },
            new KeyValue { Key = "acme.empty_map", Value = new AnyValue { KvlistValue = new KeyValueList() } },
            new KeyValue { Key = "acme.nested", Value = nested }
        ];

        for (var count = 1; count <= sent.Length; count++)
        {
            var attributes = new Google.Protobuf.Collections.RepeatedField<KeyValue> { sent.Take(count) };
            var log = Assert.ContainsSingle(OtlpConverter.ConvertLogs(new ExportLogsServiceRequest
            {
                ResourceLogs = { new ResourceLogs { ScopeLogs = { new ScopeLogs { LogRecords = { new LogRecord { TimeUnixNano = Time, Attributes = { attributes } } } } } } }
            }, TimeProvider.System).Logs);
            var stored = Nami.Collector.Storage.PersistedAttributePolicy.SerializeAttributes(log.Attributes)!;

            Assert.AreEqual(Encoding.UTF8.GetByteCount(stored), OtlpConverter.StoredBytesOf(attributes),
                $"after {sent[count - 1].Key}: {stored}");
        }
    }

    /// <summary>
    ///     The four bounds are settings (<c>IngestLimits</c>): a caller that passes its own gets them, for that
    ///     call only, in the converter and in the mapping of a log body.
    /// </summary>
    [TestMethod]
    public void Limits_ThatACallerPasses_HoldForThatCallOnly()
    {
        var limits = new IngestLimits { MaxValueUtf8Bytes = 64, MaxAttributesPerRecord = 2, MaxValueNesting = 1, MaxRecordAttributeBytes = 4096 };
        var deep = new AnyValue { ArrayValue = new ArrayValue { Values = { new AnyValue { ArrayValue = new ArrayValue { Values = { new AnyValue { StringValue = "deep" } } } } } } };
        var request = new ExportTraceServiceRequest
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
                    ScopeSpans =
                    {
                        new ScopeSpans
                        {
                            Spans =
                            {
                                new Span
                                {
                                    TraceId = Id(16, 1), SpanId = Id(8, 1), Name = "span", StartTimeUnixNano = Time, EndTimeUnixNano = Time + 1,
                                    Attributes = { Text("acme.a", new string('a', 100)), new KeyValue { Key = "acme.b", Value = deep }, Text("acme.c", new string('c', 200)) }
                                }
                            }
                        }
                    }
                }
            }
        };

        var bounded = Assert.ContainsSingle(OtlpConverter.ConvertTraceRequest(request, limits).Spans);
        Assert.AreEqual(new string('a', 64 - MarkerBytes) + Marker, bounded.Attributes["acme.a"].AsString());
        Assert.AreEqual(OtlpAttributeValueKind.Empty, Assert.ContainsSingle(bounded.Attributes["acme.b"].AsArray()!).Kind, "nested deeper than one");
        Assert.IsFalse(bounded.Attributes.ContainsKey("acme.c"), "two attributes at most, the smallest first");
        Assert.AreEqual(1U, bounded.DroppedAttributesCount);

        var byDefault = Assert.ContainsSingle(OtlpConverter.ConvertTraceRequest(request).Spans);
        Assert.AreEqual(new string('a', 100), byDefault.Attributes["acme.a"].AsString(), "the next call has the defaults again");
        Assert.AreEqual(new string('c', 200), byDefault.Attributes["acme.c"].AsString());

        var logs = OtlpConverter.ConvertLogs(new ExportLogsServiceRequest
        {
            ResourceLogs = { new ResourceLogs { ScopeLogs = { new ScopeLogs { LogRecords = { new LogRecord { TimeUnixNano = Time, Body = new AnyValue { StringValue = new string('b', 100) } } } } } } }
        }, TimeProvider.System, limits);
        var body = Assert.ContainsSingle(Nami.Collector.Storage.IngestionStorageMapper.ToLogStorageRows(logs)).Body;
        using var stored = JsonDocument.Parse(body!);
        Assert.AreEqual(new string('b', 64 - MarkerBytes) + Marker, stored.RootElement.GetString());
    }

    /// <summary>
    ///     E1. The cap is on the whole value: an array or a map counts every element, key and separator. One that is
    ///     over is dropped whole and counted; half a structure is no structure. One that fits is kept as sent.
    /// </summary>
    [TestMethod]
    [DataRow(false, DisplayName = "An array of three strings of 100 KiB")]
    [DataRow(true, DisplayName = "A map of three strings of 100 KiB")]
    public void Value_AnArrayOrMapOverTheCap_IsDroppedWholeAndCounted(bool map)
    {
        var part = new string('a', 100 * 1024);
        AnyValue Structure(int parts)
        {
            var value = new AnyValue();
            if (map)
            {
                value.KvlistValue = new KeyValueList();
                for (var i = 0; i < parts; i++) value.KvlistValue.Values.Add(Text($"k{i}", part));
            }
            else
            {
                value.ArrayValue = new ArrayValue();
                for (var i = 0; i < parts; i++) value.ArrayValue.Values.Add(new AnyValue { StringValue = part });
            }

            return value;
        }

        var span = Convert(new KeyValue { Key = "acme.over", Value = Structure(3) },
            new KeyValue { Key = "acme.within", Value = Structure(2) }, Text("acme.small", "fits"));

        Assert.IsFalse(span.Attributes.ContainsKey("acme.over"));
        Assert.AreEqual(1U, span.DroppedAttributesCount);
        var within = span.Attributes["acme.within"];
        Assert.AreEqual(2, within.AsArray()?.Count ?? within.AsKeyValueList()!.Count);
        Assert.AreEqual("fits", span.Attributes["acme.small"].AsString());
    }

    /// <summary>
    ///     A bytes value over the cap is dropped whole and counted until a record can say in a field of its own
    ///     that a value was cut (ledger N45): a marker cannot sit inside binary data, and a cut value must never look
    ///     complete. A log body is cut like any value.
    /// </summary>
    [TestMethod]
    public void Value_BytesOverTheCap_AreDroppedWholeAndCounted_AndALogBodyIsCutLikeAnyValue()
    {
        var bytes = new byte[Cap + 1];
        Array.Fill(bytes, (byte)7);
        var span = Convert(
            new KeyValue { Key = "acme.bytes", Value = new AnyValue { BytesValue = ByteString.CopyFrom(bytes) } },
            new KeyValue { Key = "acme.fits", Value = new AnyValue { BytesValue = ByteString.CopyFrom(bytes.AsSpan(0, Cap)) } });
        Assert.IsFalse(span.Attributes.ContainsKey("acme.bytes"));
        Assert.HasCount(Cap, span.Attributes["acme.fits"].AsBytes()!);
        Assert.AreEqual(1U, span.DroppedAttributesCount);

        var log = Assert.ContainsSingle(OtlpConverter.ConvertLogs(new ExportLogsServiceRequest
        {
            ResourceLogs =
            {
                new ResourceLogs
                {
                    ScopeLogs =
                    {
                        new ScopeLogs
                        {
                            LogRecords = { new LogRecord { TimeUnixNano = Time, Body = new AnyValue { StringValue = new string('b', Cap + 1) } } }
                        }
                    }
                }
            }
        }, TimeProvider.System).Logs);
        Assert.AreEqual(new string('b', Cap - MarkerBytes) + Marker, log.Body.AsString());
    }

    /// <summary>
    ///     A structured log body is a value like any other: within 256 KiB of decoded size it is stored as sent, with
    ///     its structure, however many bytes its JSON takes, and those bytes count toward the record. Over the cap it
    ///     is dropped whole: an empty body, counted in the record's dropped_attributes_count.
    /// </summary>
    [TestMethod]
    public void Body_AStructureWithinTheCap_IsStoredAsSent_AndOneOverItIsDroppedAndCounted()
    {
        LogIngestionBatch Convert(int items, AnyValue item)
        {
            var body = new AnyValue { ArrayValue = new ArrayValue() };
            for (var i = 0; i < items; i++) body.ArrayValue.Values.Add(item);
            return OtlpConverter.ConvertLogs(new ExportLogsServiceRequest
            {
                ResourceLogs = { new ResourceLogs { ScopeLogs = { new ScopeLogs { LogRecords = { new LogRecord { TimeUnixNano = Time, Body = body } } } } } }
            }, TimeProvider.System);
        }

        // 20,000 integers are well within the cap as values and take more than half a megabyte of JSON as stored.
        var within = Convert(20_000, new AnyValue { IntValue = 7 });
        Assert.AreEqual(0U, Assert.ContainsSingle(within.Logs).DroppedAttributesCount);
        var stored = Assert.ContainsSingle(Nami.Collector.Storage.IngestionStorageMapper.ToLogStorageRows(within)).Body!;
        Assert.IsGreaterThan(Cap, Encoding.UTF8.GetByteCount(stored), "the stored JSON is larger than the cap of the decoded value");
        using var document = JsonDocument.Parse(stored);
        Assert.AreEqual(JsonValueKind.Array, document.RootElement.ValueKind, "the structure is kept");
        Assert.AreEqual(20_000, document.RootElement.GetArrayLength());

        var over = Convert(3, new AnyValue { StringValue = new string('a', 100 * 1024) });
        var dropped = Assert.ContainsSingle(over.Logs);
        Assert.AreEqual(OtlpAttributeValueKind.Empty, dropped.Body.Kind);
        Assert.AreEqual(1U, dropped.DroppedAttributesCount);
        Assert.IsNull(Assert.ContainsSingle(Nami.Collector.Storage.IngestionStorageMapper.ToLogStorageRows(over)).Body);
    }

    /// <summary>
    ///     A value Claude Code cut itself ends in its own marker, <c>[TRUNCATED ...]</c>, counted in UTF-16 code units
    ///     (61,440 by default, at most 184,320 bytes). That is the sender's text: while it fits nami's cap it is
    ///     stored byte for byte, and over the cap nami's marker follows what is kept of it.
    /// </summary>
    [TestMethod]
    public void Value_ThatClaudeCodeAlreadyCut_StaysAsSentWhileItFits_AndCarriesBothMarkersOverTheCap()
    {
        const string theirs = "[TRUNCATED 1200 chars]";
        var byDefault = new string('é', 61_440 - theirs.Length) + theirs;
        Assert.AreEqual(61_440, byDefault.Length);
        Assert.AreEqual(byDefault, Convert(Text("tool_input", byDefault)).Attributes["tool_input"].AsString());

        var raised = new string('a', 100_000) + theirs + new string('é', 162_144);
        var stored = Convert(Text("tool_input", raised)).Attributes["tool_input"].AsString()!;
        Assert.Contains(theirs, stored);
        Assert.EndsWith(Marker, stored);
        Assert.IsLessThanOrEqualTo(Cap, Encoding.UTF8.GetByteCount(stored));
    }

    /// <summary>
    ///     E2. A span, a span event, a span link, a log record and a scope keep 128 attributes; the rest are dropped
    ///     and added to the count the sender reported. Among attributes of equal size the keys in ordinal order stay.
    ///     A resource is exempt from the count.
    /// </summary>
    [TestMethod]
    public void Count_AttributesPastTheHundredAndTwentyEighth_AreDroppedAndCounted_ExceptOnAResource()
    {
        var many = Enumerable.Range(0, 130).Reverse().Select(static i => Text($"acme.k{i:000}", "v")).ToArray();
        var span = new Span
        {
            TraceId = Id(16, 1), SpanId = Id(8, 1), Name = "wide", StartTimeUnixNano = Time, EndTimeUnixNano = Time + 1,
            DroppedAttributesCount = 5, Attributes = { many },
            Events = { new Span.Types.Event { Name = "e", TimeUnixNano = Time, Attributes = { many } } },
            Links = { new Span.Types.Link { TraceId = Id(16, 2), SpanId = Id(8, 2), Attributes = { many } } }
        };
        var batch = OtlpConverter.ConvertTraceRequest(new ExportTraceServiceRequest
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
                    Resource = new Resource { Attributes = { many } },
                    ScopeSpans = { new ScopeSpans { Scope = new InstrumentationScope { Name = "s", Attributes = { many } }, Spans = { span } } }
                }
            }
        });

        Assert.AreEqual(0L, batch.RejectedSpans, batch.RejectionMessage);
        var stored = Assert.ContainsSingle(batch.Spans);
        Assert.HasCount(128 + 1, stored.Attributes, "128 of the sender's and the service name ingest adds");
        Assert.IsTrue(stored.Attributes.ContainsKey("acme.k127") && !stored.Attributes.ContainsKey("acme.k128"),
            "of equal sizes the first 128 keys in ordinal order, whatever order they were sent in");
        Assert.AreEqual(5U + 2, stored.DroppedAttributesCount);
        Assert.HasCount(128, Assert.ContainsSingle(stored.Events).Attributes);
        Assert.AreEqual(2U, stored.Events[0].DroppedAttributesCount);
        Assert.HasCount(128, Assert.ContainsSingle(stored.Links).Attributes);
        Assert.AreEqual(2U, stored.Links[0].DroppedAttributesCount);
        Assert.HasCount(128, stored.ScopeAttributes);
        Assert.AreEqual(2U, stored.ScopeDroppedAttributesCount);
        Assert.HasCount(130, stored.ResourceAttributes, "a resource is exempt from the count");
        Assert.AreEqual(0U, stored.ResourceDroppedAttributesCount);
    }

    /// <summary>
    ///     E2. One record keeps 1 MiB of attributes, keys and structure included; a resource has its own megabyte.
    ///     Over it the record is reduced in an order that does not depend on the order sent: the session, usage and
    ///     cost keys first, then the smallest attribute first, then by key. Three shuffles of one record store the
    ///     same row, byte for byte.
    /// </summary>
    [TestMethod]
    public void Record_OverOneMebibyte_IsReducedTheSameWayInEveryOrder_AndKeepsThePriorityKeys()
    {
        var large = new string('a', 200 * 1024);
        KeyValue[] sent =
        [
            Text("acme.a", large), Text("acme.b", large), Text("acme.c", large), Text("acme.d", large), Text("acme.e", large),
            Text("acme.f", large), Text("session.id", "5b8e0c71"), Text("cost_usd", "0.0033"),
            new KeyValue { Key = "gen_ai.usage.input_tokens", Value = new AnyValue { IntValue = 1200 } },
            Text("input_tokens", new string('7', 150 * 1024)), Text("acme.small", "fits"), Text("acme.medium", new string('m', 1024))
        ];
        KeyValue[][] orders = [sent, [.. sent.Reverse()], [.. sent.OrderBy(static a => a.Key.Length).ThenByDescending(static a => a.Key, StringComparer.Ordinal)]];

        var rows = orders.Select(order =>
        {
            var span = new Span
            {
                TraceId = Id(16, 1), SpanId = Id(8, 1), Name = "heavy", StartTimeUnixNano = Time, EndTimeUnixNano = Time + 1,
                Attributes = { order }
            };
            var batch = OtlpConverter.ConvertTraceRequest(new ExportTraceServiceRequest
            {
                ResourceSpans = { new ResourceSpans { Resource = new Resource { Attributes = { order } }, ScopeSpans = { new ScopeSpans { Spans = { span } } } } }
            });
            Assert.AreEqual(0L, batch.RejectedSpans, batch.RejectionMessage);
            return Assert.ContainsSingle(Nami.Collector.Storage.IngestionStorageMapper.ToSpanStorageRows(batch));
        }).ToArray();

        foreach (var row in rows.Skip(1))
        {
            Assert.AreEqual(rows[0].AttributesJson, row.AttributesJson, "the same attributes in another order store the same row");
            Assert.AreEqual(rows[0].ResourceJson, row.ResourceJson);
            Assert.AreEqual(rows[0].DroppedAttributesCount, row.DroppedAttributesCount);
            Assert.AreEqual(rows[0].ResourceDroppedAttributesCount, row.ResourceDroppedAttributesCount);
        }

        var kept = Nami.Collector.Storage.StoredAttributeJson.Attributes(rows[0].AttributesJson).Select(static a => a.Key).ToArray();
        string[] expected =
        [
            "acme.a", "acme.b", "acme.c", "acme.d", "acme.medium", "acme.small", "cost_usd", "gen_ai.usage.input_tokens",
            "input_tokens", "service.name", "session.id"
        ];
        Assert.AreSequenceEqual(expected, kept, "the priority keys, then the smallest first, then by key");
        Assert.AreEqual(2U, rows[0].DroppedAttributesCount);
        Assert.AreEqual(2U, rows[0].ResourceDroppedAttributesCount);
        Assert.IsLessThanOrEqualTo(OtlpConverter.MaxRecordAttributeBytes, Encoding.UTF8.GetByteCount(rows[0].AttributesJson!));
    }

    /// <summary>
    ///     E2. When the session, usage and cost attributes alone do not fit, the record is refused through partial
    ///     success, with a message, and its sibling is kept: nami stores no record without them.
    /// </summary>
    [TestMethod]
    public void Record_WhosePriorityAttributesAloneDoNotFit_IsRefused_AndItsSiblingIsKept()
    {
        var large = new string('a', Cap);
        var heavy = new Span
        {
            TraceId = Id(16, 2), SpanId = Id(8, 2), Name = "heavy", StartTimeUnixNano = Time, EndTimeUnixNano = Time + 1,
            Attributes =
            {
                Text("gen_ai.usage.a", large), Text("gen_ai.usage.b", large), Text("gen_ai.usage.c", large),
                Text("gen_ai.usage.d", large), Text("gen_ai.usage.e", large), Text("acme.small", "fits")
            }
        };
        var innocent = new Span { TraceId = Id(16, 1), SpanId = Id(8, 1), Name = "innocent", StartTimeUnixNano = Time, EndTimeUnixNano = Time + 1 };

        var batch = OtlpConverter.ConvertTraceRequest(new ExportTraceServiceRequest
        {
            ResourceSpans = { new ResourceSpans { ScopeSpans = { new ScopeSpans { Spans = { innocent, heavy } } } } }
        });

        Assert.AreEqual(1L, batch.RejectedSpans);
        Assert.Contains("session, usage and cost attributes", batch.RejectionMessage!);
        Assert.AreEqual("innocent", Assert.ContainsSingle(batch.Spans).Name);
    }

    /// <summary>
    ///     Engine audit A3: OTLP bounds neither the length of an array nor the size of a key-value list, and an empty
        ///     value costs two bytes on the wire. Such a value does not fit the cap of one value: it is dropped and
    ///     counted without being read to its end, and the span and its sibling are kept.
    /// </summary>
    [TestMethod]
    [DataRow(false, 1_000_000, DisplayName = "An array of a million empty values")]
    [DataRow(true, 300_000, DisplayName = "A key-value list of 300,000 keys")]
    public void Record_AValueThatExpandsWithoutBound_IsDroppedAndCounted_AndItsSpanIsKept(bool keyed, int count)
    {
        var value = new AnyValue();
        if (keyed)
        {
            value.KvlistValue = new KeyValueList();
            for (var i = 0; i < count; i++)
                value.KvlistValue.Values.Add(new KeyValue { Key = $"acme.k{i}", Value = new AnyValue() });
        }
        else
        {
            value.ArrayValue = new ArrayValue();
            for (var i = 0; i < count; i++) value.ArrayValue.Values.Add(new AnyValue());
        }

        var wide = new Span
        {
            TraceId = Id(16, 2), SpanId = Id(8, 2), Name = "wide", StartTimeUnixNano = Time, EndTimeUnixNano = Time + 1,
            Attributes = { new KeyValue { Key = "gen_ai.input.messages", Value = value }, Text("acme.small", "fits") }
        };
        var innocent = new Span { TraceId = Id(16, 1), SpanId = Id(8, 1), Name = "innocent", StartTimeUnixNano = Time, EndTimeUnixNano = Time + 1 };
        var bytes = new ExportTraceServiceRequest
        {
            ResourceSpans = { new ResourceSpans { ScopeSpans = { new ScopeSpans { Spans = { innocent, wide } } } } }
        }.ToByteArray();
        Assert.IsTrue(bytes.Length < 16 * 1024 * 1024, "The export fits the OTLP/HTTP size bound.");

        var batch = OtlpConverter.ConvertTraceRequest(ExportTraceServiceRequest.Parser.ParseFrom(bytes));

        Assert.AreEqual(0L, batch.RejectedSpans, batch.RejectionMessage);
        Assert.HasCount(2, batch.Spans);
        var stored = batch.Spans.Single(static span => span.Name == "wide");
        Assert.IsFalse(stored.Attributes.ContainsKey("gen_ai.input.messages"));
        Assert.AreEqual("fits", stored.Attributes["acme.small"].AsString());
        Assert.AreEqual(1U, stored.DroppedAttributesCount);
    }

    /// <summary>
    ///     The judge of a structure over the value cap is the only bound that says no here: an array of 60,000 empty
    ///     strings is 240,004 bytes as sent and 180,001 bytes as stored JSON, within the cap of one value and the
    ///     budget of the record, so a converter whose judge never says "over" stores it. The request is converted as
    ///     parsed, with no bounded parse in front, so no sentinel stands in for the judge either.
    /// </summary>
    [TestMethod]
    public void Judge_AnArrayOnlyTheJudgeRefuses_IsDroppedAndCounted()
    {
        var value = new AnyValue { ArrayValue = new ArrayValue() };
        for (var i = 0; i < 60_000; i++) value.ArrayValue.Values.Add(new AnyValue { StringValue = "" });
        Assert.IsLessThan(new IngestLimits().MaxValueUtf8Bytes, value.CalculateSize(), "Within the cap as sent.");

        var stored = Convert(new KeyValue { Key = "acme.judged", Value = value }, Text("acme.small", "fits"));

        Assert.IsFalse(stored.Attributes.ContainsKey("acme.judged"), "Only the judge refuses this value.");
        Assert.AreEqual("fits", stored.Attributes["acme.small"].AsString());
        Assert.AreEqual(1U, stored.DroppedAttributesCount);
    }

    /// <summary>
    ///     A value ingest replaces while it keeps the attribute is no dropped attribute, but it is a loss. The counter
    ///     <c>nami.collector.values.replaced</c> counts it once per replaced value, by reason: <c>depth</c> for a
    ///     structure at the nesting bound that became an empty value, <c>invalid_utf16</c> for a lone surrogate that
    ///     became U+FFFD.
    /// </summary>
    [TestMethod]
    public void Replaced_Values_AreCountedOncePerValue_ByReason()
    {
        var deep = new AnyValue { StringValue = "leaf" };
        for (var level = 0; level < OtlpConverter.MaxValueNesting + 1; level++)
            deep = new AnyValue { ArrayValue = new ArrayValue { Values = { deep } } };
        var thread = Environment.CurrentManagedThreadId;
        var counted = new Dictionary<string, long>(StringComparer.Ordinal);
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, own) =>
        {
            if (instrument.Meter.Name == "nami.collector" && instrument.Name == "nami.collector.values.replaced")
                own.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
        {
            // The meter is the process's; conversion is synchronous, so this thread's measurements are this test's.
            if (Environment.CurrentManagedThreadId != thread) return;
            var reason = (string)tags.ToArray().Single(static tag => tag.Key == "nami.value.replacement.reason").Value!;
            counted[reason] = counted.GetValueOrDefault(reason) + measurement;
        });
        listener.Start();

        var span = Convert(Text("acme.two", "a\uD800b\uDC00"), Text("acme.one", "\uDBFF"), Text("acme.clean", "ok 😀"),
            new KeyValue { Key = "acme.deep", Value = deep }, new KeyValue { Key = "acme.deep_too", Value = deep });

        Assert.AreEqual(3L, counted.GetValueOrDefault("invalid_utf16"), "three lone surrogates in two values");
        Assert.AreEqual(2L, counted.GetValueOrDefault("depth"), "two structures at the nesting bound");
        Assert.HasCount(2, counted);
        Assert.AreEqual(0U, span.DroppedAttributesCount, "a replaced value is no dropped attribute");
        Assert.HasCount(5 + 1, span.Attributes);
    }

    /// <summary>
    ///     Two keys that differ only in a credential are one key once the credential is gone. One attribute is
    ///     stored, and the other is counted as dropped.
    /// </summary>
    [TestMethod]
    public void Keys_ThatBecomeOneKeyAfterTheStage_CountTheAttributeThatIsGone()
    {
        var span = Convert(Text("https://svc:ZQB001X000ZZZZZZ@host.example.test/", "first"),
            Text("https://svc:ZQB002X000ZZZZZZ@host.example.test/", "second"), Text("acme.kept", "fits"));

        Assert.AreEqual("second", span.Attributes["https://[REDACTED]@host.example.test/"].AsString());
        Assert.AreEqual("fits", span.Attributes["acme.kept"].AsString());
        Assert.AreEqual(1U, span.DroppedAttributesCount);
    }

    /// <summary>
    ///     The bound before materialization leaves a sentinel in place of a structure that could never fit. It can
    ///     sit on every carrier of all three signals; wherever it sits, its attribute is dropped and counted (a log
    ///     body becomes an empty value), the neighbour is kept, and the sentinel reaches no stored column. A metric
    ///     point and an exemplar have no dropped count, so there only nami's counter says it.
    /// </summary>
    [TestMethod]
    public void Sentinel_OnEveryCarrier_IsDroppedAndCounted_AndReachesNoColumn()
    {
        KeyValue[] Pair() => [Text("acme.over", OtlpConverter.OversizedValueSentinel), Text("acme.kept", "fits")];
        var nested = new KeyValue
        {
            Key = "acme.nested",
            Value = new AnyValue { ArrayValue = new ArrayValue { Values = { new AnyValue { StringValue = "a" }, new AnyValue { StringValue = OtlpConverter.OversizedValueSentinel } } } }
        };

        var traces = OtlpConverter.ConvertTraceRequest(new ExportTraceServiceRequest
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
                    Resource = new Resource { Attributes = { Pair() } },
                    ScopeSpans =
                    {
                        new ScopeSpans
                        {
                            Scope = new InstrumentationScope { Name = "s", Attributes = { Pair() } },
                            Spans =
                            {
                                new Span
                                {
                                    TraceId = Id(16, 1), SpanId = Id(8, 1), Name = "span", StartTimeUnixNano = Time, EndTimeUnixNano = Time + 1,
                                    Attributes = { Pair(), nested },
                                    Events = { new Span.Types.Event { Name = "e", TimeUnixNano = Time, Attributes = { Pair() } } },
                                    Links = { new Span.Types.Link { TraceId = Id(16, 2), SpanId = Id(8, 2), Attributes = { Pair() } } }
                                }
                            }
                        }
                    }
                }
            }
        });
        var span = Assert.ContainsSingle(traces.Spans);
        Assert.AreEqual(2U, span.DroppedAttributesCount, "the attribute that is the sentinel and the one that holds it");
        Assert.AreEqual(1U, span.ResourceDroppedAttributesCount);
        Assert.AreEqual(1U, span.ScopeDroppedAttributesCount);
        Assert.AreEqual(1U, Assert.ContainsSingle(span.Events).DroppedAttributesCount);
        Assert.AreEqual(1U, Assert.ContainsSingle(span.Links).DroppedAttributesCount);
        foreach (var kept in new[] { span.Attributes, span.ResourceAttributes, span.ScopeAttributes, span.Events[0].Attributes, span.Links[0].Attributes })
        {
            Assert.AreEqual("fits", kept["acme.kept"].AsString());
            Assert.IsFalse(kept.ContainsKey("acme.over"));
        }

        var logs = OtlpConverter.ConvertLogs(new ExportLogsServiceRequest
        {
            ResourceLogs =
            {
                new ResourceLogs
                {
                    Resource = new Resource { Attributes = { Pair() } },
                    ScopeLogs =
                    {
                        new ScopeLogs
                        {
                            Scope = new InstrumentationScope { Name = "s", Attributes = { Pair() } },
                            LogRecords =
                            {
                                new LogRecord { TimeUnixNano = Time, Body = new AnyValue { StringValue = OtlpConverter.OversizedValueSentinel }, Attributes = { Pair() } }
                            }
                        }
                    }
                }
            }
        }, TimeProvider.System);
        var log = Assert.ContainsSingle(logs.Logs);
        Assert.AreEqual(OtlpAttributeValueKind.Empty, log.Body.Kind);
        Assert.AreEqual(2U, log.DroppedAttributesCount, "the attribute, and the body, which has no count of its own");
        Assert.AreEqual(1U, log.ResourceDroppedAttributesCount);
        Assert.AreEqual(1U, log.ScopeDroppedAttributesCount);
        Assert.AreEqual("fits", log.Attributes["acme.kept"].AsString());

        var metrics = OtlpConverter.ConvertMetrics(new OpenTelemetry.Proto.Collector.Metrics.V1.ExportMetricsServiceRequest
        {
            ResourceMetrics =
            {
                new OpenTelemetry.Proto.Metrics.V1.ResourceMetrics
                {
                    Resource = new Resource { Attributes = { Pair() } },
                    ScopeMetrics =
                    {
                        new OpenTelemetry.Proto.Metrics.V1.ScopeMetrics
                        {
                            Scope = new InstrumentationScope { Name = "s", Attributes = { Pair() } },
                            Metrics =
                            {
                                new OpenTelemetry.Proto.Metrics.V1.Metric
                                {
                                    Name = "acme.requests", Metadata = { Pair() },
                                    Sum = new OpenTelemetry.Proto.Metrics.V1.Sum
                                    {
                                        AggregationTemporality = OpenTelemetry.Proto.Metrics.V1.AggregationTemporality.Delta, IsMonotonic = true,
                                        DataPoints =
                                        {
                                            new OpenTelemetry.Proto.Metrics.V1.NumberDataPoint
                                            {
                                                AsInt = 7, StartTimeUnixNano = Time, TimeUnixNano = Time + 1, Attributes = { Pair() },
                                                Exemplars = { new OpenTelemetry.Proto.Metrics.V1.Exemplar { AsInt = 1, TimeUnixNano = Time, FilteredAttributes = { Pair() } } }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        });
        Assert.AreEqual(0L, metrics.RejectedDataPoints, metrics.RejectionMessage);
        var metricRows = Nami.Collector.Storage.MetricStorageMapper.ToStorageRows(metrics);

        const string mark = "nami:value-over-the-cap";
        object[] rows =
        [
            .. Nami.Collector.Storage.IngestionStorageMapper.ToSpanStorageRows(traces),
            .. Nami.Collector.Storage.IngestionStorageMapper.ToLogStorageRows(logs), .. metricRows.Series, .. metricRows.Observations
        ];
        Assert.HasCount(4, rows);
        foreach (var row in rows)
        {
            var kept = 0;
            foreach (var property in row.GetType().GetProperties().Where(static p => p.PropertyType == typeof(string)))
            {
                var text = (string?)property.GetValue(row) ?? "";
                Assert.DoesNotContain(mark, text, $"{row.GetType().Name}.{property.Name}");
                if (text.Contains("acme.kept", StringComparison.Ordinal)) kept++;
            }

            Assert.IsGreaterThan(0, kept, $"{row.GetType().Name} keeps the neighbour of what was dropped");
        }
    }

    /// <summary>
    ///     What the data model of the OpenTelemetry Common Specification allows and a bound must not take away: an
    ///     empty value, zero, an empty string, an empty array, an empty map, a null inside an array, the case of a key.
    /// </summary>
    [TestMethod]
    public void Kept_EmptyValuesZeroEmptyStringsEmptyArraysNullsAndKeyCase_AreStoredAsSent()
    {
        var span = Convert(
            new KeyValue { Key = "acme.empty", Value = new AnyValue() }, new KeyValue { Key = "acme.absent" },
            new KeyValue { Key = "acme.zero", Value = new AnyValue { IntValue = 0 } },
            new KeyValue { Key = "acme.false", Value = new AnyValue { BoolValue = false } }, Text("acme.empty_string", ""),
            new KeyValue { Key = "acme.empty_array", Value = new AnyValue { ArrayValue = new ArrayValue() } },
            new KeyValue { Key = "acme.empty_map", Value = new AnyValue { KvlistValue = new KeyValueList() } },
            new KeyValue
            {
                Key = "acme.nulls",
                Value = new AnyValue { ArrayValue = new ArrayValue { Values = { new AnyValue { StringValue = "a" }, new AnyValue(), new AnyValue { IntValue = 2 } } } }
            },
            Text("Acme.CamelCase", "upper"), Text("acme.camelcase", "lower"), Text("acme.digits", "0042"));

        Assert.AreEqual(OtlpAttributeValueKind.Empty, span.Attributes["acme.empty"].Kind);
        Assert.AreEqual(OtlpAttributeValueKind.Empty, span.Attributes["acme.absent"].Kind);
        Assert.AreEqual(0L, span.Attributes["acme.zero"].AsInt64());
        Assert.AreEqual(false, span.Attributes["acme.false"].AsBool());
        Assert.AreEqual("", span.Attributes["acme.empty_string"].AsString());
        Assert.IsEmpty(span.Attributes["acme.empty_array"].AsArray()!);
        Assert.IsEmpty(span.Attributes["acme.empty_map"].AsKeyValueList()!);
        var nulls = span.Attributes["acme.nulls"].AsArray()!;
        Assert.HasCount(3, nulls);
        Assert.AreEqual(OtlpAttributeValueKind.Empty, nulls[1].Kind, "a null inside an array stays where it was");
        Assert.AreEqual("upper", span.Attributes["Acme.CamelCase"].AsString());
        Assert.AreEqual("lower", span.Attributes["acme.camelcase"].AsString());
        Assert.AreEqual("0042", span.Attributes["acme.digits"].AsString(), "a string of digits stays a string");
        Assert.AreEqual(0U, span.DroppedAttributesCount);
    }

    private static KeyValue Text(string key, string value) =>
        new() { Key = key, Value = new AnyValue { StringValue = value } };

    private static ByteString Id(int length, byte value) => ByteString.CopyFrom(Enumerable.Repeat(value, length).ToArray());

    private static SpanIngestionRecord Convert(params KeyValue[] attributes) => Assert.ContainsSingle(Batch(attributes).Spans);

    private static TraceIngestionBatch Batch(params KeyValue[] attributes)
    {
        var span = new Span
        {
            TraceId = Id(16, 1), SpanId = Id(8, 1), Name = "span", StartTimeUnixNano = Time, EndTimeUnixNano = Time + 1,
            Attributes = { attributes }
        };
        var batch = OtlpConverter.ConvertTraceRequest(new ExportTraceServiceRequest
        {
            ResourceSpans = { new ResourceSpans { ScopeSpans = { new ScopeSpans { Spans = { span } } } } }
        });
        Assert.AreEqual(0L, batch.RejectedSpans, batch.RejectionMessage);
        return batch;
    }
}
