using System.Text.Unicode;
using Google.Protobuf.Reflection;

namespace Nami.Collector.Ingestion;

/// <summary>
///     OTLP 1.11.1 "UTF-8 String Handling": a decoder SHOULD replace an invalid UTF-8 sequence with
///     U+FFFD rather than refuse the export. Google.Protobuf has no lenient mode and rejects the whole
///     message, so before its strict parse the <c>string</c> fields are checked on the wire, guided by the
///     generated <see cref="MessageDescriptor" />. A payload that holds an invalid sequence is rewritten once,
///     every other field copied byte for byte, and the rewritten bytes are what the parser reads: no parse
///     that fails first, and no string or copy of a replaced value in between.
/// </summary>
/// <remarks>
///     Two passes over the bytes: the first measures the rewritten message and records each nested
///     message's new length and the new length of each replaced string, and is all a valid payload costs; the
///     second writes into a buffer of exactly that size. That keeps a hostile payload linear in time and at one
///     copy in memory (three bytes for each invalid one, at most).
/// </remarks>
internal static class OtlpUtf8Replacement
{
    // Google.Protobuf's recursion limit; a deeper message fails the strict parse anyway.
    private const int MaxDepth = 100;

    private const int WireTypeVarint = 0;
    private const int WireTypeFixed64 = 1;
    private const int WireTypeLengthDelimited = 2;
    private const int WireTypeStartGroup = 3;
    private const int WireTypeEndGroup = 4;
    private const int WireTypeFixed32 = 5;

    /// <summary>
    ///     Rewrites <paramref name="message" /> with every invalid UTF-8 sequence in a <c>string</c>
    ///     field replaced by U+FFFD. False when there was nothing to replace or the bytes are not a
    ///     well-formed message, so the strict parser's own failure stands.
    /// </summary>
    public static bool TryReplaceInvalidUtf8(
        ReadOnlySpan<byte> message,
        MessageDescriptor descriptor,
        [NotNullWhen(true)] out byte[]? replaced)
    {
        var plan = new Plan();
        var measure = new Sink(default, false);
        if (!Rewrite(message, descriptor, 0, plan, ref measure) || !plan.ReplacedAnything)
        {
            replaced = null;
            return false;
        }

        plan.BeginWrite();
        replaced = GC.AllocateUninitializedArray<byte>(measure.Length);
        var write = new Sink(replaced, true);
        if (!Rewrite(message, descriptor, 0, plan, ref write) || write.Length != replaced.Length)
            throw new InvalidOperationException("The UTF-8 rewrite did not match its measured output.");
        return true;
    }

    private static bool Rewrite(ReadOnlySpan<byte> message, MessageDescriptor descriptor, int depth, Plan plan,
        ref Sink sink)
    {
        if (depth > MaxDepth)
            return false;

        var position = 0;
        while (position < message.Length)
        {
            var fieldStart = position;
            if (!TryReadVarint(message, ref position, out var tag))
                return false;

            var fieldNumber = tag >> 3;
            if (fieldNumber is 0 or > int.MaxValue)
                return false;

            switch ((int)(tag & 7))
            {
                case WireTypeVarint:
                    if (!TryReadVarint(message, ref position, out _))
                        return false;
                    sink.Copy(message[fieldStart..position]);
                    break;

                case WireTypeFixed64:
                    if (!TrySkip(message, ref position, 8))
                        return false;
                    sink.Copy(message[fieldStart..position]);
                    break;

                case WireTypeFixed32:
                    if (!TrySkip(message, ref position, 4))
                        return false;
                    sink.Copy(message[fieldStart..position]);
                    break;

                case WireTypeStartGroup:
                    // Groups do not exist in proto3; an unknown one is copied through unchanged.
                    if (!TrySkipGroup(message, ref position))
                        return false;
                    sink.Copy(message[fieldStart..position]);
                    break;

                case WireTypeLengthDelimited:
                    var tagEnd = position;
                    if (!TryReadVarint(message, ref position, out var length) ||
                        length > (ulong)(message.Length - position))
                        return false;
                    var body = message.Slice(position, (int)length);
                    position += (int)length;

                    var field = descriptor.FindFieldByNumber((int)fieldNumber);
                    switch (field)
                    {
                        case { FieldType: FieldType.String } when !Utf8.IsValid(body):
                            sink.Copy(message[fieldStart..tagEnd]);
                            sink.Replaced(body, plan.ReplacedLength(body));
                            break;
                        case { FieldType: FieldType.Message }:
                            if (!RewriteNested(body, field.MessageType, depth, plan, ref sink, message[fieldStart..tagEnd]))
                                return false;
                            break;
                        default:
                            sink.Copy(message[fieldStart..position]);
                            break;
                    }

                    break;

                default:
                    return false;
            }
        }

        return true;
    }

    private static bool RewriteNested(
        ReadOnlySpan<byte> body,
        MessageDescriptor descriptor,
        int depth,
        Plan plan,
        ref Sink sink,
        ReadOnlySpan<byte> tag)
    {
        int length;
        if (plan.Measuring)
        {
            var slot = plan.ReserveNestedLength();
            var measure = new Sink(default, false);
            if (!Rewrite(body, descriptor, depth + 1, plan, ref measure))
                return false;
            length = measure.Length;
            plan.SetNestedLength(slot, length);
        }
        else
        {
            length = plan.NestedLength();
        }

        sink.Copy(tag);
        sink.Varint((ulong)length);
        if (!plan.Measuring)
        {
            var nested = sink.Nested(length);
            if (!Rewrite(body, descriptor, depth + 1, plan, ref nested))
                return false;
        }

        sink.Advance(length);
        return true;
    }

    private static bool TryReadVarint(ReadOnlySpan<byte> message, ref int position, out ulong value)
    {
        value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if (position >= message.Length)
                return false;

            var b = message[position++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) is 0)
                return true;
        }

        return false;
    }

    private static bool TrySkip(ReadOnlySpan<byte> message, ref int position, int count)
    {
        if (message.Length - position < count)
            return false;
        position += count;
        return true;
    }

    private static bool TrySkipGroup(ReadOnlySpan<byte> message, ref int position)
    {
        var depth = 1;
        while (depth > 0)
        {
            if (!TryReadVarint(message, ref position, out var tag) || tag >> 3 is 0)
                return false;

            switch ((int)(tag & 7))
            {
                case WireTypeVarint:
                    if (!TryReadVarint(message, ref position, out _))
                        return false;
                    break;
                case WireTypeFixed64:
                    if (!TrySkip(message, ref position, 8))
                        return false;
                    break;
                case WireTypeFixed32:
                    if (!TrySkip(message, ref position, 4))
                        return false;
                    break;
                case WireTypeLengthDelimited:
                    if (!TryReadVarint(message, ref position, out var length) ||
                        length > (ulong)(message.Length - position))
                        return false;
                    position += (int)length;
                    break;
                case WireTypeStartGroup:
                    depth++;
                    break;
                case WireTypeEndGroup:
                    depth--;
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     How many bytes <paramref name="utf8" /> has once every maximal invalid subsequence is U+FFFD: three bytes
    ///     where one to three stood.
    /// </summary>
    internal static int ReplacedLength(ReadOnlySpan<byte> utf8)
    {
        var length = 0;
        while (!utf8.IsEmpty)
        {
            var beyondAscii = utf8.IndexOfAnyInRange((byte)0x80, (byte)0xFF);
            if (beyondAscii < 0)
                return checked(length + utf8.Length);

            utf8 = utf8[beyondAscii..];
            var valid = Rune.DecodeFromUtf8(utf8, out _, out var consumed) is OperationStatus.Done;
            length = checked(length + beyondAscii + (valid ? consumed : 3));
            utf8 = utf8[consumed..];
        }

        return length;
    }

    /// <summary>
    ///     Writes <paramref name="utf8" /> with every maximal invalid subsequence replaced by U+FFFD, the sequences
    ///     Encoding.UTF8 replaces, into <paramref name="replaced" />, which holds
    ///     <see cref="ReplacedLength" /> bytes. Encoding.UTF8 would build a string and allocate some forty bytes
    ///     for each sequence it replaces.
    /// </summary>
    internal static void ReplaceInvalidUtf8(ReadOnlySpan<byte> utf8, Span<byte> replaced)
    {
        while (!utf8.IsEmpty)
        {
            var beyondAscii = utf8.IndexOfAnyInRange((byte)0x80, (byte)0xFF);
            if (beyondAscii < 0)
            {
                utf8.CopyTo(replaced);
                return;
            }

            utf8[..beyondAscii].CopyTo(replaced);
            utf8 = utf8[beyondAscii..];
            replaced = replaced[beyondAscii..];
            if (Rune.DecodeFromUtf8(utf8, out _, out var consumed) is OperationStatus.Done)
            {
                utf8[..consumed].CopyTo(replaced);
                replaced = replaced[consumed..];
            }
            else
            {
                "\uFFFD"u8.CopyTo(replaced);
                replaced = replaced[3..];
            }

            utf8 = utf8[consumed..];
        }
    }

    /// <summary>What the measure pass learned, consumed by the write pass in the same order.</summary>
    private sealed class Plan
    {
        private readonly List<int> _nestedLengths = [];
        private readonly List<int> _replacedLengths = [];
        private int _nextNestedLength;
        private int _nextReplaced;

        public bool Measuring { get; private set; } = true;

        public bool ReplacedAnything => _replacedLengths.Count > 0;

        public void BeginWrite() => Measuring = false;

        /// <summary>The length of a string field once replaced; the value itself is written by the write pass.</summary>
        public int ReplacedLength(ReadOnlySpan<byte> invalidUtf8)
        {
            if (!Measuring)
                return _replacedLengths[_nextReplaced++];

            var length = OtlpUtf8Replacement.ReplacedLength(invalidUtf8);
            _replacedLengths.Add(length);
            return length;
        }

        public int ReserveNestedLength()
        {
            _nestedLengths.Add(0);
            return _nestedLengths.Count - 1;
        }

        public void SetNestedLength(int slot, int length) => _nestedLengths[slot] = length;

        public int NestedLength() => _nestedLengths[_nextNestedLength++];
    }

    /// <summary>Counts the rewritten bytes and, in the write pass, stores them.</summary>
    private ref struct Sink(Span<byte> output, bool write)
    {
        private readonly Span<byte> _output = output;

        public int Length { get; private set; }

        public void Copy(ReadOnlySpan<byte> bytes)
        {
            if (write)
                bytes.CopyTo(_output[Length..]);
            Length += bytes.Length;
        }

        /// <summary>A string field's length and its bytes with the invalid sequences replaced, written in place.</summary>
        public void Replaced(ReadOnlySpan<byte> invalidUtf8, int replacedLength)
        {
            Varint((ulong)replacedLength);
            if (write)
                ReplaceInvalidUtf8(invalidUtf8, _output.Slice(Length, replacedLength));
            Length += replacedLength;
        }

        public void Varint(ulong value)
        {
            do
            {
                var b = (byte)(value & 0x7F);
                value >>= 7;
                if (value is not 0)
                    b |= 0x80;
                if (write)
                    _output[Length] = b;
                Length++;
            } while (value is not 0);
        }

        public readonly Sink Nested(int length) => new(write ? _output.Slice(Length, length) : default, write);

        public void Advance(int length) => Length += length;
    }
}
