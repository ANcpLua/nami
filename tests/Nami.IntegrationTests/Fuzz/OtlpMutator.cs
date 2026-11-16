using System.Collections;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.Reflection;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using ProtoLogRecord = OpenTelemetry.Proto.Logs.V1.LogRecord;

namespace Nami.IntegrationTests.Fuzz;

/// <summary>One OTLP/HTTP request body as the collector receives it.</summary>
internal sealed record OtlpCase(FuzzSignal Signal, byte[] Body, string ContentType, string? ContentEncoding, string Note);

/// <summary>
///     Structure-aware mutation of OTLP export messages: it walks the generated descriptors, so every field of every
///     message is reachable, and adds OTLP-specific shapes (deep and wide attribute values, duplicate records and
///     keys, extreme timestamps, ids and metric shapes). The encoded case then goes out as protobuf or as OTLP/JSON,
///     with wire-level damage (truncation, invalid UTF-8, unknown fields, JSON type and literal changes, duplicate
///     keys, lone surrogates, gzip).
/// </summary>
internal sealed class OtlpMutator(Random random, ulong nowNanos)
{
    // Bounds the rendered case: the collector refuses more than 16 MiB, so larger cases only test the size bound.
    private const int MaxCaseBytes = 20 * 1024 * 1024;
    private const int MaxJsonDepth = 900;

    private static readonly string[] s_keys =
    [
        "service.name", "nami.project.id", "session.id", "service.version", "vcs.ref.head.revision",
        "deployment.environment.name", "cicd.pipeline.name", "exception.type", "exception.message",
        "exception.stacktrace", "error.type", "http.request.method", "http.response.status_code",
        "gen_ai.input.messages", "gen_ai.request.model", "gen_ai.usage.input_tokens", "user.id",
        "http.request.header.x-fuzz", "http.request.header.", "http.method", "http.status_code", "db.statement",
        "baggage.user", "BAGGAGE.x", "fuzz.unknown", "", " ", "\t", "a.b.c.d.e.f.g.h.i.j.k.l.m.n.o.p"
    ];

    private static readonly string[] s_strings =
    [
        "", " ", "\0", "\u0001", "unknown", "default", "checkout", "exception", "\uFFFD", "\u202Eevil", "\uD83D\uDE00",
        "e\u0301", "\u00E9", "a\r\nb", "{\"a\":1}", "'; DROP TABLE spans; --", "../../etc/passwd", "%s%n%x",
        "\\u0000", "token=abc password=hunter2 https://user:pw@example.com/?q=secret#frag", "-60m", "1970-01-01T00:00:00Z",
        "0af7651916cd43dd8448eb211c80319c", "SPAN_KIND_SERVER", "NaN", "Infinity", "null", "true", "18446744073709551616"
    ];

    private static readonly double[] s_doubles =
    [
        double.NaN, double.PositiveInfinity, double.NegativeInfinity, -0.0, 0, 1, -1, double.Epsilon,
        double.MaxValue, double.MinValue, 1e308, 1e-308, 0.1, 9007199254740993, -9007199254740993, 4.9e-324
    ];

    private static readonly long[] s_longs = [long.MinValue, long.MaxValue, 0, -1, 1, int.MaxValue, int.MinValue, 9007199254740993];

    private static readonly int[] s_ints = [int.MinValue, int.MaxValue, 0, -1, 1, 2, 3, 5, 6, 24, 25, 100, -10, 20, 21];

    private static readonly int[] s_depths = [8, 16, 24, 30, 31, 32, 33, 40, 44, 45, 46, 47, 48, 49, 50, 60, 64, 100, 300];

    internal OtlpCase Next(OtlpSeed seed)
    {
        var message = seed.Parse();
        var notes = new List<string> { seed.Name };
        var steps = 1 + random.Next(4);
        for (var i = 0; i < steps; i++)
        {
            try
            {
                notes.Add(random.Next(5) < 2 ? MutateSemantic(message) : MutateGeneric(message));
            }
            catch (Exception error) when (error is ArgumentException or InvalidCastException or InvalidOperationException)
            {
                // A generated value the generated setter refuses is no case; keep the message as it is.
            }
        }

        return Render(seed.Signal, message, notes);
    }

    /// <summary>Encodes a message, without mutation, the way an OTLP exporter would.</summary>
    internal static OtlpCase Encode(FuzzSignal signal, IMessage message, bool json) => json
        ? new OtlpCase(signal, Encoding.UTF8.GetBytes(OtlpJson(message, false)), "application/json", null, "json")
        : new OtlpCase(signal, message.ToByteArray(), "application/x-protobuf", null, "protobuf");

    private OtlpCase Render(FuzzSignal signal, IMessage message, List<string> notes)
    {
        byte[] body;
        string contentType;
        var depth = AnyValueDepth(message);
        if (random.Next(2) == 0 || depth * 2 + 12 > MaxJsonDepth)
        {
            body = message.ToByteArray();
            contentType = "application/x-protobuf";
            var damage = random.Next(10);
            if (damage < 2)
            {
                body = InjectInvalidUtf8(message, notes) ?? body;
            }
            else if (damage < 4)
            {
                body = MutateBytes(body, notes);
            }
        }
        else
        {
            body = RenderJson(message, notes);
            contentType = random.Next(8) switch
            {
                0 => "application/json; charset=utf-8",
                1 => "Application/JSON",
                _ => "application/json"
            };
        }

        string? encoding = null;
        if (random.Next(10) == 0 && body.Length < MaxCaseBytes)
        {
            encoding = "gzip";
            body = Gzip(body);
            if (random.Next(4) == 0)
            {
                body = MutateBytes(body, notes);
                notes.Add("gzip damaged");
            }
            else
            {
                notes.Add("gzip");
            }
        }

        return new OtlpCase(signal, body, contentType, encoding, string.Join("; ", notes));
    }

    private string MutateGeneric(IMessage root)
    {
        var messages = Messages(root);
        var message = messages[random.Next(messages.Count)];
        var fields = message.Descriptor.Fields.InDeclarationOrder();
        if (fields.Count == 0) return "no fields";
        var field = fields[random.Next(fields.Count)];
        var name = $"{message.Descriptor.Name}.{field.Name}";
        if (field.IsRepeated)
            return name + ": " + MutateRepeated(message, field);
        if (field.FieldType == FieldType.Message)
        {
            switch (random.Next(4))
            {
                case 0:
                    field.Accessor.Clear(message);
                    return name + ": cleared";
                case 1:
                    field.Accessor.SetValue(message, Fresh(field.MessageType));
                    return name + ": empty";
                default:
                    field.Accessor.SetValue(message, field.MessageType == AnyValue.Descriptor
                        ? RandomAnyValue(random.Next(4))
                        : Populate(Fresh(field.MessageType)));
                    return name + ": random";
            }
        }

        var value = Scalar(field);
        field.Accessor.SetValue(message, value);
        return $"{name} = {Describe(value)}";
    }

    private string MutateRepeated(IMessage message, FieldDescriptor field)
    {
        var list = (IList)field.Accessor.GetValue(message);
        switch (random.Next(8))
        {
            case 0 when list.Count > 0:
                list.RemoveAt(random.Next(list.Count));
                return "removed one";
            case 1 when list.Count > 0:
                list.Add(Copy(field, list[random.Next(list.Count)]!));
                return "duplicated one";
            case 2:
                list.Clear();
                return "cleared";
            case 3 when list.Count > 0:
                var item = list[random.Next(list.Count)]!;
                var copies = Bounded(random.Next(3) switch { 0 => 16, 1 => 256, _ => 2048 }, item);
                for (var i = 0; i < copies; i++) list.Add(Copy(field, item));
                return $"{copies} copies";
            case 4 when list.Count > 1:
                var first = random.Next(list.Count);
                var second = random.Next(list.Count);
                (list[first], list[second]) = (list[second], list[first]);
                return "swapped";
            case 5 when list.Count > 0 && field.FieldType != FieldType.Message:
                var value = Scalar(field);
                list[random.Next(list.Count)] = value;
                return "element = " + Describe(value);
            default:
                list.Add(field.FieldType == FieldType.Message
                    ? field.MessageType == KeyValue.Descriptor ? RandomKeyValue() : Populate(Fresh(field.MessageType))
                    : Scalar(field));
                return "added";
        }
    }

    private string MutateSemantic(IMessage root) => random.Next(11) switch
    {
        0 => DeepValue(root),
        1 => ManyAttributes(root),
        2 => HugeString(root),
        3 => DuplicateAttribute(root),
        4 => KnownKeyValue(root),
        5 => DuplicateRecord(root),
        6 => Timestamps(root),
        7 => Identifiers(root),
        8 => MetricShape(root),
        9 => ResourceIdentity(root),
        _ => DuplicateRecord(root)
    };

    private string DeepValue(IMessage root)
    {
        var attributes = AttributeLists(root);
        if (attributes.Count == 0) return "no attribute list";
        var depth = s_depths[random.Next(s_depths.Length)];
        var mode = random.Next(3);
        var key = random.Next(3) == 0 ? "fuzz.deep" : random.Next(2) == 0 ? "feature_flag.result.value" : "http.request.header.x-deep";
        attributes[random.Next(attributes.Count)].Add(new KeyValue { Key = key, Value = Nested(depth, mode) });
        if (random.Next(3) == 0 && Messages(root).OfType<ProtoLogRecord>().FirstOrDefault() is { } log)
            log.Body = Nested(depth, mode);
        return $"deep value depth {depth} mode {mode} at {key}";
    }

    internal static AnyValue Nested(int depth, int mode)
    {
        var value = new AnyValue { StringValue = "leaf" };
        for (var i = 0; i < depth; i++)
        {
            var array = mode switch { 0 => true, 1 => false, _ => i % 2 == 0 };
            value = array
                ? new AnyValue { ArrayValue = new ArrayValue { Values = { value } } }
                : new AnyValue
                {
                    // Nested entries keep only keys the registry persists; this any-typed one is persisted at every
                    // level (content keys such as gen_ai.input.messages are not).
                    KvlistValue = new KeyValueList { Values = { new KeyValue { Key = "feature_flag.result.value", Value = value } } }
                };
        }

        return value;
    }

    private string ManyAttributes(IMessage root)
    {
        var attributes = AttributeLists(root);
        if (attributes.Count == 0) return "no attribute list";
        var target = attributes[random.Next(attributes.Count)];
        var count = random.Next(4) switch { 0 => 64, 1 => 512, 2 => 4096, _ => 20000 };
        var persisted = random.Next(2) == 0;
        for (var i = 0; i < count; i++)
        {
            target.Add(new KeyValue
            {
                Key = persisted ? $"http.request.header.x-{i}" : $"fuzz.k{i}",
                Value = new AnyValue { IntValue = i }
            });
        }

        return $"{count} attributes, persisted {persisted}";
    }

    private string HugeString(IMessage root)
    {
        var length = random.Next(5) switch
        {
            0 => 64 * 1024,
            1 => 64 * 1024 + 1,
            2 => 200_000,
            3 => 1_000_000,
            _ => 4_000_000
        };
        var unit = random.Next(3) switch { 0 => "x", 1 => "\u00E9", _ => "\uD83D\uDE00" };
        var text = new StringBuilder(length).Insert(0, unit, Math.Max(1, length / unit.Length)).ToString();
        var targets = StringFields(root);
        if (targets.Count == 0) return "no string field";
        var (message, field) = targets[random.Next(targets.Count)];
        field.Accessor.SetValue(message, text);
        return $"{message.Descriptor.Name}.{field.Name} = {text.Length} chars of U+{char.ConvertToUtf32(unit, 0):X4}";
    }

    private string DuplicateAttribute(IMessage root)
    {
        var attributes = AttributeLists(root).Where(static list => list.Count > 0).ToList();
        if (attributes.Count == 0) return "no attributes";
        var list = attributes[random.Next(attributes.Count)];
        var original = list[random.Next(list.Count)];
        var copy = original.Clone();
        switch (random.Next(4))
        {
            case 0:
                list.Add(copy);
                return $"duplicate key {original.Key} with an equal value";
            case 1:
                copy.Value = RandomAnyValue(1);
                list.Add(copy);
                return $"duplicate key {original.Key} with another value";
            case 2:
                copy.Key = original.Key.ToUpperInvariant();
                list.Add(copy);
                return $"key {original.Key} in upper case";
            default:
                copy.Key = original.Key + "\u0000";
                list.Add(copy);
                return $"key {original.Key} with a trailing NUL";
        }
    }

    private string KnownKeyValue(IMessage root)
    {
        var attributes = AttributeLists(root);
        if (attributes.Count == 0) return "no attribute list";
        var key = s_keys[random.Next(s_keys.Length)];
        var list = attributes[random.Next(attributes.Count)];
        var value = RandomAnyValue(2);
        var existing = list.FirstOrDefault(kv => kv.Key == key);
        if (existing is not null) existing.Value = value;
        else list.Add(new KeyValue { Key = key, Value = value });
        return $"{key} = {value.ValueCase}";
    }

    private string DuplicateRecord(IMessage root)
    {
        var lists = new List<IList>();
        foreach (var message in Messages(root))
        {
            foreach (var field in message.Descriptor.Fields.InDeclarationOrder())
            {
                if (!field.IsRepeated || field.FieldType != FieldType.Message || field.MessageType == KeyValue.Descriptor ||
                    field.MessageType == AnyValue.Descriptor)
                    continue;
                if (field.Accessor.GetValue(message) is IList { Count: > 0 } list) lists.Add(list);
            }
        }

        if (lists.Count == 0) return "no records";
        var target = lists[random.Next(lists.Count)];
        var item = (IMessage)target[random.Next(target.Count)]!;
        var copies = Bounded(random.Next(4) == 0 ? 2 + random.Next(150) : 1, item);
        for (var i = 0; i < copies; i++) target.Add(Clone(item));
        return $"{copies} exact copies of a {item.Descriptor.Name}";
    }

    private string Timestamps(IMessage root)
    {
        var targets = new List<(IMessage Message, FieldDescriptor Field)>();
        foreach (var message in Messages(root))
        {
            foreach (var field in message.Descriptor.Fields.InDeclarationOrder())
            {
                if (!field.IsRepeated && field.Name.EndsWith("time_unix_nano", StringComparison.Ordinal))
                    targets.Add((message, field));
            }
        }

        if (targets.Count == 0) return "no timestamps";
        var (target, time) = targets[random.Next(targets.Count)];
        if (target is Span span && random.Next(3) == 0)
        {
            (span.StartTimeUnixNano, span.EndTimeUnixNano) = (span.EndTimeUnixNano, span.StartTimeUnixNano);
            return "span start and end swapped";
        }

        var value = Timestamp();
        time.Accessor.SetValue(target, value);
        return $"{target.Descriptor.Name}.{time.Name} = {value}";
    }

    private string Identifiers(IMessage root)
    {
        var targets = new List<(IMessage Message, FieldDescriptor Field)>();
        foreach (var message in Messages(root))
        {
            foreach (var field in message.Descriptor.Fields.InDeclarationOrder())
            {
                if (!field.IsRepeated && field.FieldType == FieldType.Bytes && field.Name.EndsWith("_id", StringComparison.Ordinal))
                    targets.Add((message, field));
            }
        }

        if (targets.Count == 0) return "no ids";
        var (target, id) = targets[random.Next(targets.Count)];
        var value = Bytes(id.Name);
        id.Accessor.SetValue(target, value);
        return $"{target.Descriptor.Name}.{id.Name} = {Convert.ToHexString(value.Span)}";
    }

    private string MetricShape(IMessage root)
    {
        var messages = Messages(root);
        var histograms = messages.OfType<HistogramDataPoint>().ToList();
        var exponential = messages.OfType<ExponentialHistogramDataPoint>().ToList();
        var summaries = messages.OfType<SummaryDataPoint>().ToList();
        var numbers = messages.OfType<NumberDataPoint>().ToList();
        switch (random.Next(4))
        {
            case 0 when histograms.Count > 0:
                var histogram = histograms[random.Next(histograms.Count)];
                histogram.ExplicitBounds.Clear();
                histogram.BucketCounts.Clear();
                double[] bounds = random.Next(4) switch
                {
                    0 => [1.0, 1.0],
                    1 => [double.NaN, 2],
                    2 => [5, 1, 3],
                    _ => [.. Enumerable.Range(0, 5000).Select(static i => (double)i)]
                };
                histogram.ExplicitBounds.Add(bounds);
                histogram.BucketCounts.Add(Enumerable.Repeat(ulong.MaxValue, bounds.Length + random.Next(3) - 1));
                histogram.Sum = s_doubles[random.Next(s_doubles.Length)];
                return $"histogram bounds {bounds.Length}";
            case 1 when exponential.Count > 0:
                var point = exponential[random.Next(exponential.Count)];
                point.Scale = s_ints[random.Next(s_ints.Length)];
                point.ZeroThreshold = s_doubles[random.Next(s_doubles.Length)];
                point.Positive ??= new ExponentialHistogramDataPoint.Types.Buckets();
                point.Positive.Offset = s_ints[random.Next(s_ints.Length)];
                point.Positive.BucketCounts.Add(Enumerable.Repeat(ulong.MaxValue, random.Next(2) == 0 ? 3 : 20000));
                return $"exponential scale {point.Scale} offset {point.Positive.Offset}";
            case 2 when summaries.Count > 0:
                var summary = summaries[random.Next(summaries.Count)];
                summary.QuantileValues.Add(new SummaryDataPoint.Types.ValueAtQuantile
                {
                    Quantile = s_doubles[random.Next(s_doubles.Length)],
                    Value = s_doubles[random.Next(s_doubles.Length)]
                });
                summary.Sum = s_doubles[random.Next(s_doubles.Length)];
                return "summary quantile";
            case 3 when numbers.Count > 0:
                var number = numbers[random.Next(numbers.Count)];
                switch (random.Next(3))
                {
                    case 0:
                        number.ClearValue();
                        return "number point without a value";
                    case 1:
                        number.AsDouble = s_doubles[random.Next(s_doubles.Length)];
                        return "number point double " + Describe(number.AsDouble);
                    default:
                        number.Flags = random.Next(2) == 0 ? 1u : uint.MaxValue;
                        return $"number point flags {number.Flags}";
                }
            default:
                return MutateGeneric(root);
        }
    }

    private string ResourceIdentity(IMessage root)
    {
        var resources = Messages(root).OfType<Resource>().ToList();
        if (resources.Count == 0) return "no resource";
        var resource = resources[random.Next(resources.Count)];
        switch (random.Next(5))
        {
            case 0:
                resource.Attributes.Clear();
                return "resource without attributes";
            case 1:
                var project = s_strings[random.Next(s_strings.Length)];
                resource.Attributes.Add(new KeyValue { Key = "nami.project.id", Value = new AnyValue { StringValue = project } });
                return "nami.project.id = " + Describe(project);
            case 2:
                resource.EntityRefs.Add(new EntityRef
                {
                    Type = random.Next(2) == 0 ? "" : "service",
                    IdKeys = { random.Next(2) == 0 ? "service.name" : "missing.key", "service.name" }
                });
                return "entity ref with odd keys";
            case 3:
                foreach (var attribute in resource.Attributes.Where(static kv => kv.Key == "service.name"))
                    attribute.Value = RandomAnyValue(1);
                return "service.name of another type";
            default:
                resource.DroppedAttributesCount = uint.MaxValue;
                return "resource dropped attributes max";
        }
    }

    private byte[]? InjectInvalidUtf8(IMessage message, List<string> notes)
    {
        var targets = StringFields(message);
        if (targets.Count == 0) return null;
        var (target, field) = targets[random.Next(targets.Count)];
        const string Marker = "\u0001\u0002\u0003\u0004";
        var original = (string)field.Accessor.GetValue(target);
        field.Accessor.SetValue(target, original + Marker);
        var bytes = message.ToByteArray();
        field.Accessor.SetValue(target, original);
        byte[] invalid = random.Next(5) switch
        {
            0 => [0xFF, 0xFE, 0xFD, 0xFC],
            1 => [0xC0, 0x80, 0xC0, 0x80],
            2 => [0xED, 0xA0, 0x80, 0x41],
            3 => [0xF4, 0x90, 0x80, 0x80],
            _ => [0xE2, 0x82, 0x41, 0x80]
        };
        var index = bytes.AsSpan().IndexOf("\u0001\u0002\u0003\u0004"u8);
        if (index < 0) return null;
        invalid.CopyTo(bytes, index);
        notes.Add($"invalid UTF-8 {Convert.ToHexString(invalid)} in {target.Descriptor.Name}.{field.Name}");
        return bytes;
    }

    private byte[] MutateBytes(byte[] bytes, List<string> notes)
    {
        switch (random.Next(6))
        {
            case 0 when bytes.Length > 0:
                var length = random.Next(bytes.Length);
                notes.Add($"truncated to {length} of {bytes.Length}");
                return bytes[..length];
            case 1 when bytes.Length > 0:
                var flipped = (byte[])bytes.Clone();
                var flips = 1 + random.Next(8);
                for (var i = 0; i < flips; i++) flipped[random.Next(flipped.Length)] ^= (byte)(1 << random.Next(8));
                notes.Add($"{flips} bit flips");
                return flipped;
            case 2:
                // Unknown fields of every wire type, including groups and the reserved types 6 and 7.
                var wire = random.Next(8);
                byte[] unknown = wire switch
                {
                    0 => [0xF8, 0x07, 0x96, 0x01],
                    1 => [0xF9, 0x07, 1, 2, 3, 4, 5, 6, 7, 8],
                    2 => [0xFA, 0x07, 0x03, 0x41, 0x42, 0x43],
                    3 => [0xFB, 0x07, 0x08, 0x01, 0xFC, 0x07],
                    4 => [0xFC, 0x07],
                    5 => [0xFD, 0x07, 1, 2, 3, 4],
                    6 => [0xFE, 0x07, 0x00],
                    _ => [0xFF, 0x07, 0x00]
                };
                notes.Add($"unknown field wire type {wire}");
                return [.. bytes, .. unknown];
            case 3:
                notes.Add("overlong varint length");
                return [.. bytes, 0x0A, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01];
            case 4 when bytes.Length > 0:
                var at = random.Next(bytes.Length);
                var insert = new byte[1 + random.Next(16)];
                random.NextBytes(insert);
                notes.Add($"{insert.Length} random bytes at {at}");
                return [.. bytes.AsSpan(0, at), .. insert, .. bytes.AsSpan(at)];
            default:
                var tail = new byte[1 + random.Next(64)];
                random.NextBytes(tail);
                notes.Add($"{tail.Length} random trailing bytes");
                return [.. bytes, .. tail];
        }
    }

    private byte[] RenderJson(IMessage message, List<string> notes)
    {
        var integers = random.Next(2) == 0;
        var text = OtlpJson(message, integers);
        var raw = new List<(string Placeholder, string Text)>();
        var splices = new List<(string Placeholder, byte[] Bytes)>();
        if (random.Next(3) > 0)
        {
            var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { MaxDepth = MaxJsonDepth * 2 })!;
            var steps = 1 + random.Next(3);
            for (var i = 0; i < steps; i++) notes.Add(MutateJson(node, raw, splices));
            text = Write(node);
        }

        foreach (var (placeholder, replacement) in raw)
            text = text.Replace(Quoted(placeholder), replacement, StringComparison.Ordinal);
        var bytes = Encoding.UTF8.GetBytes(text);
        foreach (var (placeholder, replacement) in splices)
        {
            var marker = Encoding.UTF8.GetBytes(Quoted(placeholder)[1..^1]);
            var index = bytes.AsSpan().IndexOf(marker);
            if (index >= 0) bytes = [.. bytes.AsSpan(0, index), .. replacement, .. bytes.AsSpan(index + marker.Length)];
        }

        switch (random.Next(12))
        {
            case 0:
                notes.Add("BOM");
                return [0xEF, 0xBB, 0xBF, .. bytes];
            case 1:
                notes.Add("trailing garbage");
                return [.. bytes, .. "}garbage"u8];
            case 2:
                notes.Add("comment");
                return [.. "/* c */"u8, .. bytes];
            case 3:
                notes.Add("whitespace");
                return [.. " \t\r\n"u8, .. bytes, .. "\n\n"u8];
            case 4 when bytes.Length > 1:
                var cut = random.Next(bytes.Length);
                notes.Add($"JSON truncated to {cut}");
                return bytes[..cut];
            default:
                return bytes;
        }
    }

    private string MutateJson(JsonNode root, List<(string, string)> raw, List<(string, byte[])> splices)
    {
        var nodes = new List<JsonNode>();
        Collect(root, nodes);
        var node = nodes[random.Next(nodes.Count)];
        var placeholder = $"\u0001P{raw.Count + splices.Count}\u0001";
        switch (random.Next(9))
        {
            case 0:
                var literal = random.Next(14) switch
                {
                    0 => "1e400",
                    1 => "-1e400",
                    2 => "-0",
                    3 => "18446744073709551616",
                    4 => "-1",
                    5 => "1.5",
                    6 => "\"NaN\"",
                    7 => "\"-Infinity\"",
                    8 => "\"\\ud800\"",
                    9 => "\"a\\udc00\\ud800b\"",
                    10 => "\"\\u0000\"",
                    11 => "9223372036854775808",
                    12 => "\"0x10\"",
                    _ => "1E+2"
                };
                if (!Replace(node, JsonValue.Create(placeholder)!)) return "literal skipped";
                raw.Add((placeholder, literal));
                return "literal " + literal;
            case 1:
                if (!Replace(node, random.Next(5) switch
                    {
                        0 => JsonValue.Create(42),
                        1 => JsonValue.Create("text"),
                        2 => JsonValue.Create(true),
                        3 => new JsonArray(),
                        _ => new JsonObject()
                    }))
                    return "type skipped";
                return "value of another JSON type";
            case 2 when node is JsonObject { Count: > 0 } duplicate:
                var property = duplicate.ElementAt(random.Next(duplicate.Count));
                duplicate[placeholder] = property.Value?.DeepClone();
                raw.Add((placeholder, Quote(property.Key)));
                return "duplicate key " + property.Key;
            case 3:
                var depth = random.Next(2) == 0 ? 130 : 600;
                if (!Replace(node, JsonValue.Create(placeholder)!)) return "nesting skipped";
                raw.Add((placeholder, new string('[', depth) + new string(']', depth)));
                return $"JSON nesting {depth}";
            case 4:
                if (!Replace(node, JsonValue.Create(placeholder)!)) return "UTF-8 skipped";
                splices.Add((placeholder, random.Next(2) == 0 ? [0xFF, 0x41] : [0xED, 0xA0, 0x80]));
                return "invalid UTF-8 in a JSON string";
            case 5 when node is JsonObject renamed && renamed.Count > 0:
                var key = renamed.ElementAt(random.Next(renamed.Count)).Key;
                var value = renamed[key];
                renamed.Remove(key);
                var snake = string.Concat(key.Select(static c => char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : c.ToString()));
                renamed[random.Next(2) == 0 ? snake : key + "X"] = value;
                return "renamed " + key;
            case 6 when node is JsonObject unknown:
                unknown["futureObject"] = new JsonObject
                {
                    ["traceId"] = "not-hex",
                    ["spanId"] = 12,
                    ["spans"] = new JsonArray(new JsonObject { ["traceId"] = "zz" })
                };
                return "unknown object with id-named members";
            case 7 when node is JsonValue scalar && scalar.TryGetValue<string>(out var text):
                var hex = random.Next(4) switch
                {
                    0 => text.ToUpperInvariant(),
                    1 => text + "0",
                    2 => "0x" + text,
                    _ => new string('g', text.Length)
                };
                Replace(node, JsonValue.Create(hex)!);
                return "string variant " + Describe(hex);
            default:
                if (!Replace(node, JsonValue.Create(Scalar64())!))
                    return "number skipped";
                return "number replaced";
        }
    }

    private string Scalar64() => random.Next(5) switch
    {
        0 => ulong.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
        1 => long.MinValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
        2 => "",
        3 => "-0",
        _ => "1e3"
    };

    private static bool Replace(JsonNode node, JsonNode replacement)
    {
        switch (node.Parent)
        {
            case JsonObject parent:
                parent[node.GetPropertyName()] = replacement;
                return true;
            case JsonArray array:
                array[node.GetElementIndex()] = replacement;
                return true;
            default:
                return false;
        }
    }

    private static void Collect(JsonNode? node, List<JsonNode> nodes)
    {
        if (node is null) return;
        nodes.Add(node);
        switch (node)
        {
            case JsonObject obj:
                foreach (var (_, child) in obj) Collect(child, nodes);
                break;
            case JsonArray array:
                foreach (var child in array) Collect(child, nodes);
                break;
        }
    }

    private static string Quoted(string placeholder) => JsonValue.Create(placeholder).ToJsonString();

    private static string Quote(string value) => JsonValue.Create(value).ToJsonString();

    /// <summary>OTLP/JSON: protojson with lowerCamelCase names and the trace and span ids in hex.</summary>
    internal static string OtlpJson(IMessage message, bool enumsAsIntegers)
    {
        var settings = JsonFormatter.Settings.Default.WithFormatEnumsAsIntegers(enumsAsIntegers);
        var node = JsonNode.Parse(new JsonFormatter(settings).Format(message),
            documentOptions: new JsonDocumentOptions { MaxDepth = MaxJsonDepth * 2 })!;
        HexIds(node);
        return Write(node);
    }

    private static string Write(JsonNode node)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { MaxDepth = MaxJsonDepth * 2 }))
            node.WriteTo(writer);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>A deep copy that keeps nesting beyond the parser's default recursion limit.</summary>
    internal static IMessage Clone(IMessage message)
    {
        using var stream = new MemoryStream(message.ToByteArray());
        using var input = CodedInputStream.CreateWithLimits(stream, int.MaxValue, 100_000);
        return message.Descriptor.Parser.ParseFrom(input);
    }

    private static int Bounded(int copies, object item) =>
        item is IMessage message ? Math.Max(1, Math.Min(copies, 4 * 1024 * 1024 / Math.Max(1, message.CalculateSize()))) : copies;

    private static void HexIds(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj.ToArray())
                {
                    if (key is "traceId" or "spanId" or "parentSpanId" && value is JsonValue id &&
                        id.TryGetValue<string>(out var base64))
                        obj[key] = Convert.ToHexStringLower(Convert.FromBase64String(base64));
                    else HexIds(value);
                }

                break;
            case JsonArray array:
                foreach (var item in array) HexIds(item);
                break;
        }
    }

    private static byte[] Gzip(byte[] body)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true))
            gzip.Write(body);
        return output.ToArray();
    }

    internal static List<IMessage> Messages(IMessage root)
    {
        var result = new List<IMessage>();
        var stack = new Stack<IMessage>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var message = stack.Pop();
            result.Add(message);
            foreach (var field in message.Descriptor.Fields.InDeclarationOrder())
            {
                if (field.FieldType != FieldType.Message) continue;
                var value = field.Accessor.GetValue(message);
                if (field.IsRepeated)
                {
                    foreach (var item in (IList)value) stack.Push((IMessage)item!);
                }
                else if (value is IMessage child)
                {
                    stack.Push(child);
                }
            }
        }

        return result;
    }

    internal static int AnyValueDepth(IMessage root)
    {
        var deepest = 0;
        var stack = new Stack<(IMessage Message, int Depth)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            var (message, depth) = stack.Pop();
            deepest = Math.Max(deepest, depth);
            foreach (var field in message.Descriptor.Fields.InDeclarationOrder())
            {
                if (field.FieldType != FieldType.Message) continue;
                var value = field.Accessor.GetValue(message);
                if (field.IsRepeated)
                {
                    foreach (var item in (IList)value) stack.Push(((IMessage)item!, depth + 1));
                }
                else if (value is IMessage child)
                {
                    stack.Push((child, depth + 1));
                }
            }
        }

        return deepest;
    }

    private static List<RepeatedField<KeyValue>> AttributeLists(IMessage root)
    {
        var lists = new List<RepeatedField<KeyValue>>();
        foreach (var message in Messages(root))
        {
            foreach (var field in message.Descriptor.Fields.InDeclarationOrder())
            {
                if (field.IsRepeated && field.FieldType == FieldType.Message && field.MessageType == KeyValue.Descriptor)
                    lists.Add((RepeatedField<KeyValue>)field.Accessor.GetValue(message));
            }
        }

        return lists;
    }

    private static List<(IMessage Message, FieldDescriptor Field)> StringFields(IMessage root)
    {
        var fields = new List<(IMessage, FieldDescriptor)>();
        foreach (var message in Messages(root))
        {
            foreach (var field in message.Descriptor.Fields.InDeclarationOrder())
            {
                if (field.IsRepeated || field.FieldType != FieldType.String) continue;
                if (field.ContainingOneof is { } oneof && oneof.Accessor.GetCaseFieldDescriptor(message) != field) continue;
                fields.Add((message, field));
            }
        }

        return fields;
    }

    private static IMessage Fresh(MessageDescriptor descriptor) => descriptor.Parser.ParseFrom(ByteString.Empty);

    private IMessage Populate(IMessage message)
    {
        var fields = message.Descriptor.Fields.InDeclarationOrder();
        for (var i = 0; i < 1 + random.Next(3) && fields.Count > 0; i++)
        {
            var field = fields[random.Next(fields.Count)];
            if (field.FieldType == FieldType.Message || field.IsRepeated) continue;
            field.Accessor.SetValue(message, Scalar(field));
        }

        return message;
    }

    private static object Copy(FieldDescriptor field, object value) => field.FieldType == FieldType.Message
        ? Clone((IMessage)value)
        : value;

    private KeyValue RandomKeyValue() => new()
    {
        Key = s_keys[random.Next(s_keys.Length)],
        Value = RandomAnyValue(2)
    };

    private AnyValue RandomAnyValue(int depth)
    {
        switch (random.Next(depth > 0 ? 9 : 7))
        {
            case 0: return new AnyValue { StringValue = s_strings[random.Next(s_strings.Length)] };
            case 1: return new AnyValue { IntValue = s_longs[random.Next(s_longs.Length)] };
            case 2: return new AnyValue { DoubleValue = s_doubles[random.Next(s_doubles.Length)] };
            case 3: return new AnyValue { BoolValue = random.Next(2) == 0 };
            case 4: return new AnyValue { BytesValue = Bytes("bytes") };
            case 5: return new AnyValue();
            case 6: return new AnyValue { StringValueStrindex = s_ints[random.Next(s_ints.Length)] };
            case 7:
                var array = new ArrayValue();
                for (var i = random.Next(4); i > 0; i--) array.Values.Add(RandomAnyValue(depth - 1));
                return new AnyValue { ArrayValue = array };
            default:
                var list = new KeyValueList();
                for (var i = random.Next(4); i > 0; i--)
                    list.Values.Add(new KeyValue { Key = s_keys[random.Next(s_keys.Length)], Value = RandomAnyValue(depth - 1) });
                return new AnyValue { KvlistValue = list };
        }
    }

    private object Scalar(FieldDescriptor field) => field.FieldType switch
    {
        FieldType.Double => s_doubles[random.Next(s_doubles.Length)],
        FieldType.Float => (float)s_doubles[random.Next(s_doubles.Length)],
        FieldType.Int64 or FieldType.SInt64 or FieldType.SFixed64 => s_longs[random.Next(s_longs.Length)],
        FieldType.UInt64 or FieldType.Fixed64 => field.Name.EndsWith("time_unix_nano", StringComparison.Ordinal)
            ? Timestamp()
            : random.Next(3) switch { 0 => ulong.MaxValue, 1 => 0UL, _ => (ulong)random.Next(1000) },
        FieldType.Int32 or FieldType.SInt32 or FieldType.SFixed32 => s_ints[random.Next(s_ints.Length)],
        FieldType.UInt32 or FieldType.Fixed32 => random.Next(3) switch { 0 => uint.MaxValue, 1 => 0u, _ => 1u },
        FieldType.Bool => random.Next(2) == 0,
        FieldType.String => s_strings[random.Next(s_strings.Length)],
        FieldType.Bytes => Bytes(field.Name),
        FieldType.Enum => Enum.ToObject(field.EnumType.ClrType, s_ints[random.Next(s_ints.Length)]),
        _ => throw new ArgumentException("Unsupported field type " + field.FieldType)
    };

    private ulong Timestamp() => random.Next(11) switch
    {
        0 => 0UL,
        1 => 1UL,
        2 => ulong.MaxValue,
        3 => (ulong)long.MaxValue,
        4 => (ulong)long.MaxValue + 1UL,
        5 => nowNanos + 3_600_000_000_000UL,
        6 => nowNanos - 1UL,
        // A producer that sends microseconds, and year 9999 in microseconds.
        7 => nowNanos / 1000UL,
        8 => 253_402_300_799_000_000UL,
        _ => nowNanos - (ulong)random.Next(1_000_000_000)
    };

    private ByteString Bytes(string name)
    {
        var length = random.Next(6) switch
        {
            0 => 0,
            1 => name.Contains("trace", StringComparison.Ordinal) ? 16 : 8,
            2 => 1 + random.Next(20),
            3 => name.Contains("trace", StringComparison.Ordinal) ? 8 : 16,
            _ => name.Contains("trace", StringComparison.Ordinal) ? 16 : 8
        };
        var bytes = new byte[length];
        switch (random.Next(3))
        {
            case 0: break;
            case 1: Array.Fill(bytes, (byte)0xFF); break;
            default: random.NextBytes(bytes); break;
        }

        return ByteString.CopyFrom(bytes);
    }

    private static string Describe(object value) => value switch
    {
        string text => text.Length > 24 ? $"\"{text[..24]}...\" ({text.Length})" : $"\"{text}\"",
        double number => number.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        ByteString bytes => Convert.ToHexString(bytes.Span),
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "null"
    };
}
