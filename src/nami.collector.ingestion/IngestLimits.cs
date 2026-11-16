using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Nami.Collector.Ingestion.Configuration;

namespace Nami.Collector.Ingestion;

/// <summary>
///     The size bounds of ingest (observer's ruling, 16 Nov 2026). nami stores telemetry as it was sent; besides the
///     credential stage these bounds are the only thing ingest does to it. One instance serves the request readers
///     (HTTP and gRPC), the bounded parse and the converter, so the three count against the same numbers.
///     <para>
///         nami takes the data model from the current OpenTelemetry Common Specification: what an AnyValue is, how
///         values nest, what an empty value is (https://opentelemetry.io/docs/specs/otel/common/#anyvalue). The
///         bounds are nami receiver policy; compare the SDK attribute limits in the Common Specification
///         (#attribute-limits), which are written for SDKs. nami differs from them in five ways: it counts UTF-8
///         bytes, not characters; it bounds nesting, at 16; its record budget also covers resource and metric
///         attributes; it reduces a record deterministically, priority keys first; and it uses its own truncation
///         marker and sentinel.
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

    /// <inheritdoc cref="MaxValueUtf8BytesKey" />
    internal const string MaxRequestMessagesKey = "NAMI_INGEST_MAX_REQUEST_MESSAGES";

    /// <inheritdoc cref="MaxValueUtf8BytesKey" />
    internal const string MaxRequestRecordsKey = "NAMI_INGEST_MAX_REQUEST_RECORDS";

    /// <inheritdoc cref="MaxValueUtf8BytesKey" />
    internal const string MaxRequestElementsKey = "NAMI_INGEST_MAX_REQUEST_ELEMENTS";

    /// <inheritdoc cref="MaxValueUtf8BytesKey" />
    internal const string MaxConcurrentRequestsKey = "NAMI_INGEST_MAX_CONCURRENT_REQUESTS";

    /// <inheritdoc cref="MaxValueUtf8BytesKey" />
    internal const string MaxRequestWaitSecondsKey = "NAMI_INGEST_MAX_WAIT_SECONDS";

    internal const int DefaultMaxValueUtf8Bytes = 256 * 1024;
    internal const int DefaultMaxAttributesPerRecord = 128;
    internal const int DefaultMaxRecordAttributeBytes = 1024 * 1024;
    internal const int DefaultMaxValueNesting = 16;
    internal const int DefaultMaxRequestBytes = 16 * 1024 * 1024;
    internal const int DefaultMaxRequestMessages = 50_000;
    internal const int DefaultMaxRequestRecords = 3_000;
    internal const int DefaultMaxRequestElements = 1_000_000;
    internal const int DefaultMaxConcurrentRequests = 1;
    internal const int DefaultMaxRequestWaitSeconds = 5;

    /// <summary>The bounds as the observer confirmed them; what a caller without configuration uses.</summary>
    internal static IngestLimits Default => new();

    /// <summary>
    ///     nami receiver policy: the most UTF-8 bytes one attribute value (or log body) keeps, counted as it is
    ///     encoded: the elements of an array, the members of a map and their encoding overhead count toward the
    ///     value that holds them. Compare the SDK limits in the Common Specification, which count characters.
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

    /// <summary>
    ///     nami receiver policy: the most protobuf messages one export request may hold, every type counted alike:
    ///     the request, its resource and scope containers, its records and every KeyValue, AnyValue, ArrayValue and
    ///     KeyValueList in them. The parser builds one object per message, and an empty message is two bytes of
    ///     the request and up to some 240 bytes as an object (two million empty spans in 3.8 MiB took 465 MiB), so
    ///     the byte bounds alone do not bound what a request costs to parse. What a structure over the value cap
    ///     held is not counted: it is dropped without being built. A request over the bound is refused whole, like
    ///     one over a byte bound. The default lets the default batch of an OpenTelemetry SDK through with room to
    ///     spare (512 records of the density Claude Code sends are about 19,000 messages).
    /// </summary>
    [ConfigurationKeyName(MaxRequestMessagesKey)]
    [ConfiguredRange(Key = MaxRequestMessagesKey, Minimum = 1, Maximum = int.MaxValue - 1)]
    public int MaxRequestMessages { get; set; } = DefaultMaxRequestMessages;

    /// <summary>
    ///     nami receiver policy: the most records one export request may hold, a record being a message that is
    ///     stored as a row of its own: a span, a log record, a metric data point of any kind, an exemplar. Span
    ///     events and links are stored inside their span's row and are no records. Storing costs far more than
    ///     parsing (a stored row took 13 to 30 KiB of peak memory when the default was measured, 16 Nov 2026), so
    ///     records have a bound of their own below the message bound. A request over it is refused whole.
    /// </summary>
    [ConfigurationKeyName(MaxRequestRecordsKey)]
    [ConfiguredRange(Key = MaxRequestRecordsKey, Minimum = 1, Maximum = int.MaxValue - 1)]
    public int MaxRequestRecords { get; set; } = DefaultMaxRequestRecords;

    /// <summary>
    ///     nami receiver policy: the most scalar elements one export request may hold outside its messages: what
    ///     a parser builds an object or an array slot for besides the messages. Over protobuf these are the elements
    ///     of repeated scalar fields, packed or not (16 MiB of one-byte packed bucket counts are sixteen million
    ///     8-byte numbers), and every occurrence of a string or bytes field, also of one that is not repeated. Over
    ///     JSON every member name and every string, number, boolean and null counts, in known and unknown members
    ///     alike. A request over the bound is refused whole, like one over a byte bound.
    /// </summary>
    [ConfigurationKeyName(MaxRequestElementsKey)]
    [ConfiguredRange(Key = MaxRequestElementsKey, Minimum = 1, Maximum = int.MaxValue - 1)]
    public int MaxRequestElements { get; set; } = DefaultMaxRequestElements;

    /// <summary>
    ///     nami receiver policy: how many export requests are read, parsed, converted and stored at once, in the
    ///     whole process (<see cref="OtlpIngestGate" />). One request at the record bound took about 100 MiB of
    ///     peak memory when the defaults were measured, so the slots times that is what ingest may cost.
    /// </summary>
    [ConfigurationKeyName(MaxConcurrentRequestsKey)]
    [ConfiguredRange(Key = MaxConcurrentRequestsKey, Minimum = 1, Maximum = 1024)]
    public int MaxConcurrentRequests { get; set; } = DefaultMaxConcurrentRequests;

    /// <summary>
    ///     How many seconds a request waits for a slot before it is turned away with an answer an exporter retries
    ///     (HTTP 503 with Retry-After, gRPC UNAVAILABLE). It waits before its body is read and holds no buffer.
    /// </summary>
    [ConfigurationKeyName(MaxRequestWaitSecondsKey)]
    [ConfiguredRange(Key = MaxRequestWaitSecondsKey, Minimum = 0, Maximum = 300)]
    public int MaxRequestWaitSeconds { get; set; } = DefaultMaxRequestWaitSeconds;

    // One buffer holds a request, so a byte bound stays inside what one array can hold.
    private const long MaxConfigurableBytes = 1024L * 1024 * 1024;

    // Ledger A4 (15 Nov): a value 29 levels deep was stored and then broke traces_get.
    private const long MaxConfigurableNesting = 28;
}

/// <summary>Refuses a bound outside its range at start-up, naming the configuration key and the value it held.</summary>
[OptionsValidator]
internal sealed partial class IngestLimitsValidator : IValidateOptions<IngestLimits>;
