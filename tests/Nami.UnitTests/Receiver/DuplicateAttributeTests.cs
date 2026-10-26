using Google.Protobuf;
using Nami.Tests.Evidence;
using Nami.Tests.Wire.OpenTelemetry.Proto.Common.V1;
using ProtoLog = Nami.Tests.Wire.OpenTelemetry.Proto.Logs.V1.LogRecord;

namespace Nami.UnitTests.Receiver;

[TestClass]
[TestCategory("Fast")]
public class DuplicateAttributeTests
{
    public enum AttributeShape
    {
        Text,
        Number,
        Array,
        Bytes
    }

    private const string Key = "ConnectionId";

    [TestMethod]
    [DataRow(AttributeShape.Text)]
    [DataRow(AttributeShape.Number)]
    [DataRow(AttributeShape.Array)]
    [DataRow(AttributeShape.Bytes)]
    public void Should_BeCoalescedIntoOneAttribute_When_DuplicateAttributes_IdenticalTypedValues(AttributeShape shape)
    {
        var (observed, _, _, _) = DecodeIdenticalDuplicates(shape);
        Assert.AreSequenceEqual([Key], observed.Attributes.Keys);
    }

    [TestMethod]
    [DataRow(AttributeShape.Text)]
    [DataRow(AttributeShape.Number)]
    [DataRow(AttributeShape.Array)]
    [DataRow(AttributeShape.Bytes)]
    public void Should_ReportACoalescingWarning_When_DuplicateAttributes_IdenticalTypedValues(AttributeShape shape)
    {
        var (_, warnings, _, _) = DecodeIdenticalDuplicates(shape);
        Assert.AreEqual($"Coalesced identical OTLP attribute: {Key}.", Assert.ContainsSingle(warnings));
    }

    [TestMethod]
    [DataRow(AttributeShape.Text)]
    [DataRow(AttributeShape.Number)]
    [DataRow(AttributeShape.Array)]
    [DataRow(AttributeShape.Bytes)]
    public void Should_LeaveTheWireMessageUnchanged_When_DuplicateAttributes_IdenticalTypedValues(AttributeShape shape)
    {
        var (_, _, original, input) = DecodeIdenticalDuplicates(shape);
        Assert.AreSequenceEqual(original, input.ToByteArray());
    }

    [TestMethod]
    [DataRow(AttributeShape.Text, "connection-1")]
    [DataRow(AttributeShape.Number, 1L)]
    public void Should_KeepTheTypedValue_When_DuplicateAttributes_IdenticalScalars(AttributeShape shape,
        object expected)
    {
        var (observed, _, _, _) = DecodeIdenticalDuplicates(shape);
        Assert.AreEqual(expected, observed.Attributes[Key]);
    }

    [TestMethod]
    public void Should_KeepTheOrderedValues_When_DuplicateAttributes_IdenticalArrays()
    {
        var (observed, _, _, _) = DecodeIdenticalDuplicates(AttributeShape.Array);
        Assert.AreSequenceEqual(["first", "second"],
            Assert.IsInstanceOfType<IReadOnlyList<object?>>(observed.Attributes[Key]));
    }

    [TestMethod]
    public void Should_KeepTheBytes_When_DuplicateAttributes_IdenticalBytes()
    {
        var (observed, _, _, _) = DecodeIdenticalDuplicates(AttributeShape.Bytes);
        Assert.AreSequenceEqual<byte>([1, 2], Assert.IsInstanceOfType<byte[]>(observed.Attributes[Key]));
    }

    [TestMethod]
    [DataRow(AttributeShape.Text)]
    [DataRow(AttributeShape.Number)]
    [DataRow(AttributeShape.Array)]
    [DataRow(AttributeShape.Bytes)]
    public void Should_RemainInvalid_When_DuplicateAttributes_ConflictingTypedValues(AttributeShape shape)
    {
        var input = new ProtoLog
        {
            Attributes = { OtlpEvidence.Pair(Key, Value(shape)), OtlpEvidence.Pair(Key, ConflictingValue(shape)) }
        };
        var failure = Assert.ThrowsExactly<InvalidDataException>(() =>
            new OtlpDecode().Log(input, new ResourceRecord(new Dictionary<string, object?>()), null));
        Assert.AreEqual($"Duplicate OTLP attribute: {Key}.", failure.Message);
    }

    private static (LogRecord Observed, IReadOnlyList<string> Warnings, byte[] Original, ProtoLog Input)
        DecodeIdenticalDuplicates(AttributeShape shape)
    {
        var value = Value(shape);
        var input = new ProtoLog
        {
            Attributes = { OtlpEvidence.Pair(Key, value), OtlpEvidence.Pair(Key, value.Clone()) }
        };
        var original = input.ToByteArray();
        var decoder = new OtlpDecode();
        var observed = decoder.Log(input, new ResourceRecord(new Dictionary<string, object?>()), null);
        return (observed, [.. decoder.Warnings], original, input);
    }

    private static AnyValue Value(AttributeShape shape)
    {
        return shape switch
        {
            AttributeShape.Text => new AnyValue { StringValue = "connection-1" },
            AttributeShape.Number => new AnyValue { IntValue = 1 },
            AttributeShape.Array => new AnyValue
            {
                ArrayValue = new ArrayValue
                    { Values = { new AnyValue { StringValue = "first" }, new AnyValue { StringValue = "second" } } }
            },
            AttributeShape.Bytes => new AnyValue { BytesValue = ByteString.CopyFrom([1, 2]) },
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
    }

    private static AnyValue ConflictingValue(AttributeShape shape)
    {
        return shape switch
        {
            AttributeShape.Text => new AnyValue { StringValue = "connection-2" },
            AttributeShape.Number => new AnyValue { DoubleValue = 1 },
            AttributeShape.Array => new AnyValue
            {
                ArrayValue = new ArrayValue
                    { Values = { new AnyValue { StringValue = "second" }, new AnyValue { StringValue = "first" } } }
            },
            AttributeShape.Bytes => new AnyValue { BytesValue = ByteString.CopyFrom([2, 1]) },
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
    }
}
