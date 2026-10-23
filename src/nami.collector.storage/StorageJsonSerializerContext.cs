namespace Nami.Collector.Storage;

/// <summary>
/// The only serializer of persisted JSON and of every encoding an identifier hashes. It registers
/// storage's own records and no API contract type, so contract serialization cannot change stored
/// bytes or identifiers.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowReadingFromString |
                     JsonNumberHandling.AllowNamedFloatingPointLiterals,
    AllowOutOfOrderMetadataProperties = true,
    WriteIndented = false)]
[JsonSerializable(typeof(List<SpanEventJson>), TypeInfoPropertyName = "SpanEventJsonList")]
[JsonSerializable(typeof(List<SpanLinkJson>), TypeInfoPropertyName = "SpanLinkJsonList")]
[JsonSerializable(typeof(List<ResourceEntityRefIngestionRecord>), TypeInfoPropertyName = "ResourceEntityRefIngestionRecordList")]
[JsonSerializable(typeof(IReadOnlyList<ResourceEntityRefIngestionRecord>), TypeInfoPropertyName = "ResourceEntityRefIngestionRecordReadOnlyList")]
[JsonSerializable(typeof(StoredAttributeValue))]
[JsonSerializable(typeof(StoredObjectValue))]
[JsonSerializable(typeof(StoredIntValue))]
[JsonSerializable(typeof(StoredDoubleValue))]
[JsonSerializable(typeof(StoredBytesValue))]
[JsonSerializable(typeof(StoredKeyValueListValue))]
[JsonSerializable(typeof(StoredMetricPointData))]
[JsonSerializable(typeof(IReadOnlyList<StoredExemplar>), TypeInfoPropertyName = "StoredExemplars")]
[JsonSerializable(typeof(IReadOnlyList<StoredAttribute>), TypeInfoPropertyName = "StoredAttributes")]
[JsonSerializable(typeof(HistoricalMetricProjection))]
[JsonSerializable(typeof(IReadOnlyList<string>), TypeInfoPropertyName = "IReadOnlyListString")]
[JsonSerializable(typeof(IReadOnlyList<double>), TypeInfoPropertyName = "Doubles")]
[JsonSerializable(typeof(IReadOnlyList<ulong>), TypeInfoPropertyName = "Counts")]
internal partial class StorageJsonSerializerContext : JsonSerializerContext;
