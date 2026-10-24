using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Unicode;
using Google.Protobuf.Reflection;

namespace Nami.Collector.Ingestion;

/// <summary>
///     OTLP 1.11.1 "UTF-8 String Handling": a decoder SHOULD replace an invalid UTF-8 sequence with
///     U+FFFD rather than refuse the export. Google.Protobuf has no lenient mode and rejects the whole
///     message, so after its strict parse fails the <c>string</c> fields are rewritten on the wire,
///     guided by the generated <see cref="MessageDescriptor" />, and the message is parsed again.
///     Every other field is copied byte for byte. A valid payload never reaches this.
/// </summary>
/// <remarks>
///     Two passes over the bytes: the first measures the rewritten message and records each nested
///     message's new length and each replaced string; the second writes into a buffer of exactly that
///     size. That keeps a hostile payload linear in time and memory.
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
        replaced = new byte[measure.Length];
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
            if (fieldNumber is 0 || fieldNumber > int.MaxValue)
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
                    if (field is { FieldType: FieldType.String } && !Utf8.IsValid(body))
                    {
                        var replacement = plan.Replacement(body);
                        sink.Copy(message[fieldStart..tagEnd]);
                        sink.Varint((ulong)replacement.Length);
                        sink.Copy(replacement);
                    }
                    else if (field is { FieldType: FieldType.Message })
                    {
                        if (!RewriteNested(body, field.MessageType, depth, plan, ref sink, message[fieldStart..tagEnd]))
                            return false;
                    }
                    else
                    {
                        sink.Copy(message[fieldStart..position]);
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

    /// <summary>What the measure pass learned, consumed by the write pass in the same order.</summary>
    private sealed class Plan
    {
        private readonly List<int> _nestedLengths = [];
        private readonly List<byte[]> _replacements = [];
        private int _nextNestedLength;
        private int _nextReplacement;

        public bool Measuring { get; private set; } = true;

        public bool ReplacedAnything => _replacements.Count > 0;

        public void BeginWrite() => Measuring = false;

        public byte[] Replacement(ReadOnlySpan<byte> invalidUtf8)
        {
            if (!Measuring)
                return _replacements[_nextReplacement++];

            // Encoding.UTF8 decodes each maximal invalid subsequence as one U+FFFD.
            var replacement = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(invalidUtf8));
            _replacements.Add(replacement);
            return replacement;
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
