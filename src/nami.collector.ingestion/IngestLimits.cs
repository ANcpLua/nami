using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Nami.Collector.Ingestion.Configuration;

namespace Nami.Collector.Ingestion;

/// <summary>
///     The size bounds of ingest (observer's ruling, 16 Nov 2026). nami stores telemetry as it was sent; besides the
///     credential stage these bounds are the only thing ingest does to it. One instance serves the request readers
///     (HTTP and gRPC), the bounded parse and the converter, so the three count against the same numbers.
///     <para>
///         nami follows the OpenTelemetry Common Specification
///         (https://opentelemetry.io/docs/specs/otel/common/#anyvalue and #attribute-limits). Where a bound differs
///         from it, that is nami's receiver policy: the value bound counts encoded bytes of the whole value, nested
///         elements included, where the specification counts the characters of each string; nesting is bounded;
///         and one record has a byte budget.
///     </para>
///     <para>Everything a bound removes is counted in the record's <c>dropped_attributes_count</c>.</para>
/// </summary>
internal sealed class IngestLimits
{
    /// <summary>Configuration keys, read from the engine's configuration (environment variables included).</summary>
    internal const string MaxValueUtf8BytesKey = "NAMI_INGEST_MAX_VALUE_BYTES";

    /// <inheritdoc cref="MaxValueUtf8BytesKey" />
    internal const string MaxAttributesPerRecordKey = "NAMI_INGEST_MAX_ATTRIBUTES";

    /// <inheritdoc cref="MaxValueUtf8BytesKey" />
    internal const string MaxRecordAttributeBytesKey = "NAMI_INGEST_MAX_RECORD_BYTES";

    /// <inheritdoc cref="MaxValueUtf8BytesKey" />
    internal const string MaxValueNestingKey = "NAMI_INGEST_MAX_VALUE_NESTING";

    /// <inheritdoc cref="MaxValueUtf8BytesKey" />
    internal const string MaxRequestWireBytesKey = "NAMI_INGEST_MAX_REQUEST_BYTES";

    /// <inheritdoc cref="MaxValueUtf8BytesKey" />
    internal const string MaxRequestDecompressedBytesKey = "NAMI_INGEST_MAX_DECOMPRESSED_BYTES";

    internal const int DefaultMaxValueUtf8Bytes = 256 * 1024;
    internal const int DefaultMaxAttributesPerRecord = 128;
    internal const int DefaultMaxRecordAttributeBytes = 1024 * 1024;
    internal const int DefaultMaxValueNesting = 16;
    internal const int DefaultMaxRequestBytes = 16 * 1024 * 1024;

    /// <summary>The bounds as the observer confirmed them; what a caller without configuration uses.</summary>
    internal static IngestLimits Default => new();

    /// <summary>
    ///     The most UTF-8 bytes one attribute value (or log body) keeps, counted as it is encoded: the elements of an
    ///     array, the members of a map and their encoding overhead count toward the value that holds them.
    /// </summary>
    [ConfigurationKeyName(MaxValueUtf8BytesKey)]
    [ConfiguredRange(Key = MaxValueUtf8BytesKey, Minimum = 1, Maximum = MaxConfigurableBytes)]
    public int MaxValueUtf8Bytes { get; set; } = DefaultMaxValueUtf8Bytes;

    /// <summary>
    ///     The most top-level attributes one record keeps (a span, span event, span link, log record or
    ///     instrumentation scope); the rest are dropped and counted.
    /// </summary>
    [ConfigurationKeyName(MaxAttributesPerRecordKey)]
    [ConfiguredRange(Key = MaxAttributesPerRecordKey, Minimum = 1, Maximum = int.MaxValue)]
    public int MaxAttributesPerRecord { get; set; } = DefaultMaxAttributesPerRecord;

    /// <summary>The most bytes the attributes of one record keep together, keys and structure included.</summary>
    [ConfigurationKeyName(MaxRecordAttributeBytesKey)]
    [ConfiguredRange(Key = MaxRecordAttributeBytesKey, Minimum = 1, Maximum = MaxConfigurableBytes)]
    public int MaxRecordAttributeBytes { get; set; } = DefaultMaxRecordAttributeBytes;

    /// <summary>
    ///     How deep arrays and maps may nest inside one value. A value nested deeper is replaced by an empty value.
    ///     The upper end of the range stays below the depth at which a stored value breaks the read side (ledger A4).
    /// </summary>
    [ConfigurationKeyName(MaxValueNestingKey)]
    [ConfiguredRange(Key = MaxValueNestingKey, Minimum = 1, Maximum = MaxConfigurableNesting)]
    public int MaxValueNesting { get; set; } = DefaultMaxValueNesting;

    /// <summary>
    ///     The most bytes one export request may hold on the wire, compressed or not. It holds for OTLP/HTTP bodies
    ///     and for gRPC messages alike; a larger request is refused at once (HTTP 413, gRPC RESOURCE_EXHAUSTED).
    /// </summary>
    [ConfigurationKeyName(MaxRequestWireBytesKey)]
    [ConfiguredRange(Key = MaxRequestWireBytesKey, Minimum = 1, Maximum = MaxConfigurableBytes)]
    public int MaxRequestWireBytes { get; set; } = DefaultMaxRequestBytes;

    /// <summary>
    ///     The most bytes one export request may hold after decompression, on HTTP and gRPC alike. Decompression
    ///     stops at this count; the full size is never allocated.
    /// </summary>
    [ConfigurationKeyName(MaxRequestDecompressedBytesKey)]
    [ConfiguredRange(Key = MaxRequestDecompressedBytesKey, Minimum = 1, Maximum = MaxConfigurableBytes)]
    public int MaxRequestDecompressedBytes { get; set; } = DefaultMaxRequestBytes;

    // One buffer holds a request, so a byte bound stays inside what one array can hold.
    private const long MaxConfigurableBytes = 1024L * 1024 * 1024;

    // Ledger A4 (15 Nov): a value 29 levels deep was stored and then broke traces_get.
    private const long MaxConfigurableNesting = 28;
}

/// <summary>Refuses a bound outside its range at start-up, naming the configuration key and the value it held.</summary>
[OptionsValidator]
internal sealed partial class IngestLimitsValidator : IValidateOptions<IngestLimits>;
