using Nami.Api.Contracts.Common;
using ContractValue = Nami.Api.Contracts.Common.AttributeValue;

namespace Nami.Collector.Ingestion;

internal sealed partial class OtlpAttributeValue
{
    /// <summary>
    ///     The contract's canonical text of this value, shared with TypeScript. Decoding orders and
    ///     deduplicates resource entity references by it; storage hashes its own canonical text.
    /// </summary>
    public string ToStableString() => ContractValue.StableString(ToContract());

    /// <summary>
    ///     The generated contract value. Storage maps it into its own persisted record at the ingestion
    ///     boundary; no contract serializer writes stored bytes.
    /// </summary>
    private ContractValue? ToContract() =>
        Kind switch
        {
            OtlpAttributeValueKind.Empty => null,
            OtlpAttributeValueKind.String => new ContractValue.StringValue((string)_reference!),
            OtlpAttributeValueKind.Bool => new ContractValue.BoolValue(_bool),
            OtlpAttributeValueKind.Int => new ContractValue.ObjectValue(new AttributeIntValue { Value = _int }),
            OtlpAttributeValueKind.Double => new ContractValue.ObjectValue(
                new AttributeDoubleValue { Value = _double }),
            OtlpAttributeValueKind.Bytes => new ContractValue.ObjectValue(
                new AttributeBytesValue { Base64 = (byte[])_reference! }),
            OtlpAttributeValueKind.Array => new ContractValue.ArrayValue(
                ((IReadOnlyList<OtlpAttributeValue>)_reference!).Select(static item => item.ToContract()).ToArray()),
            OtlpAttributeValueKind.KeyValueList => new ContractValue.ObjectValue(new AttributeKeyValueListValue
            {
                Values = ((IReadOnlyDictionary<string, OtlpAttributeValue>)_reference!)
                    .OrderBy(static item => item.Key, StringComparer.Ordinal)
                    .ToDictionary(static item => item.Key, static item => item.Value.ToContract(),
                        StringComparer.Ordinal)
            }),
            _ => throw new InvalidOperationException($"Unknown OTLP attribute value kind '{Kind}'.")
        };
}
