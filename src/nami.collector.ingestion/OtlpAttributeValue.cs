using System;
using System.Collections.Generic;
using System.Linq;
using Nami.Api.Contracts.Common;
using ContractValue = Nami.Api.Contracts.Common.AttributeValue;

namespace Nami.Collector.Ingestion;

internal enum OtlpAttributeValueKind
{
    Empty,
    String,
    Bool,
    Int,
    Double,
    Bytes,
    Array,
    KeyValueList
}

internal sealed class OtlpAttributeValue
{
    private readonly bool _bool;

    private readonly double _double;

    // One typed slot per kind: a scalar is stored unboxed, a string, byte array, array or key-value
    // list by reference.
    private readonly long _int;
    private readonly object? _reference;

    private OtlpAttributeValue(
        OtlpAttributeValueKind kind,
        long @int = 0,
        double @double = 0,
        bool @bool = false,
        object? reference = null)
    {
        Kind = kind;
        _int = @int;
        _double = @double;
        _bool = @bool;
        _reference = reference;
    }

    public OtlpAttributeValueKind Kind { get; }

    public static OtlpAttributeValue Empty { get; } = new(OtlpAttributeValueKind.Empty);

    public static OtlpAttributeValue FromString(string value) => new(OtlpAttributeValueKind.String, reference: value);

    public static OtlpAttributeValue FromBool(bool value) => new(OtlpAttributeValueKind.Bool, @bool: value);

    public static OtlpAttributeValue FromInt(long value) => new(OtlpAttributeValueKind.Int, value);

    public static OtlpAttributeValue FromDouble(double value) => new(OtlpAttributeValueKind.Double, @double: value);

    public static OtlpAttributeValue FromBytes(byte[] value) => new(OtlpAttributeValueKind.Bytes, reference: value);

    public static OtlpAttributeValue FromArray(IReadOnlyList<OtlpAttributeValue> value) =>
        new(OtlpAttributeValueKind.Array, reference: value);

    public static OtlpAttributeValue FromKeyValueList(IReadOnlyDictionary<string, OtlpAttributeValue> value) =>
        new(OtlpAttributeValueKind.KeyValueList, reference: value);

    public string? AsString() =>
        Kind is OtlpAttributeValueKind.String ? (string)_reference! : null;

    public long? AsInt64() =>
        Kind is OtlpAttributeValueKind.Int ? _int : null;

    public bool? AsBool() =>
        Kind is OtlpAttributeValueKind.Bool ? _bool : null;

    public double? AsDouble() =>
        Kind is OtlpAttributeValueKind.Double ? _double : null;

    public byte[]? AsBytes() =>
        Kind is OtlpAttributeValueKind.Bytes ? (byte[])_reference! : null;

    public IReadOnlyList<OtlpAttributeValue>? AsArray() =>
        Kind is OtlpAttributeValueKind.Array ? (IReadOnlyList<OtlpAttributeValue>)_reference! : null;

    public IReadOnlyDictionary<string, OtlpAttributeValue>? AsKeyValueList() =>
        Kind is OtlpAttributeValueKind.KeyValueList
            ? (IReadOnlyDictionary<string, OtlpAttributeValue>)_reference!
            : null;

    /// <summary>
    ///     The contract's canonical text of this value, shared with TypeScript. Decoding orders and
    ///     deduplicates resource entity references by it; storage hashes its own canonical text.
    /// </summary>
    public string ToStableString() => ContractValue.StableString(ToContract());

    /// <summary>
    ///     The generated contract value. Storage maps it into its own persisted record at the ingestion
    ///     boundary; no contract serializer writes stored bytes.
    /// </summary>
    public ContractValue? ToContract() =>
        Kind switch
        {
            OtlpAttributeValueKind.Empty => null,
            OtlpAttributeValueKind.String => new ContractValue.StringValue((string)_reference!),
            OtlpAttributeValueKind.Bool => new ContractValue.BoolValue(_bool),
            OtlpAttributeValueKind.Int => new AttributeValue.ObjectValue(new AttributeIntValue { Value = _int }),
            OtlpAttributeValueKind.Double => new AttributeValue.ObjectValue(
                new AttributeDoubleValue { Value = _double }),
            OtlpAttributeValueKind.Bytes => new AttributeValue.ObjectValue(
                new AttributeBytesValue { Base64 = (byte[])_reference! }),
            OtlpAttributeValueKind.Array => new AttributeValue.ArrayValue(
                ((IReadOnlyList<OtlpAttributeValue>)_reference!).Select(static item => item.ToContract()).ToArray()),
            OtlpAttributeValueKind.KeyValueList => new AttributeValue.ObjectValue(new AttributeKeyValueListValue
            {
                Values = ((IReadOnlyDictionary<string, OtlpAttributeValue>)_reference!)
                    .OrderBy(static item => item.Key, StringComparer.Ordinal)
                    .ToDictionary(static item => item.Key, static item => item.Value.ToContract(),
                        StringComparer.Ordinal)
            }),
            _ => throw new InvalidOperationException($"Unknown OTLP attribute value kind '{Kind}'.")
        };
}
