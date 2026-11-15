using System.Globalization;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Time.Testing;
using Nami.Collector.Ingestion;
using Nami.Collector.Storage;
using Nami.Grpc;
using Nami.Tools;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace Nami.IntegrationTests;

/// <summary>
///     How deeply an attribute value may nest. Protobuf's recursion limit admits 31 nested key-value lists or 47 nested
///     arrays in a span attribute; the stored JSON and the tools' results carry less, so ingest bounds the nesting per
///     record, and a read over a value stored before that bound answers a refusal.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class AttributeDepthTests(TestContext context)
{
    private const string InnocentTrace = "0af7651916cd43dd8448eb211c80319c";
    private const string DeepTrace = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string Since = "1970-01-01T00:00:00Z";

    private static readonly DateTimeOffset s_now = DateTimeOffset.Parse("2026-11-16T00:00:00Z", CultureInfo.InvariantCulture);

    [TestMethod]
    [DataRow(false, 17, DisplayName = "Key-value lists one past the bound")]
    [DataRow(false, 29, DisplayName = "Key-value lists 29 deep, which traces_get could not render")]
    [DataRow(false, 31, DisplayName = "Key-value lists 31 deep, which the store refused with a retryable 503")]
    [DataRow(true, 47, DisplayName = "Arrays 47 deep, protobuf's limit")]
    public async Task Export_AttributeNestedPastTheBound_RefusesThatSpanAndKeepsItsSibling(bool arrays, int depth)
    {
        var batch = OtlpConverter.ConvertTraceRequest(Export(Nested(depth, arrays)));

        Assert.AreEqual(1L, batch.RejectedSpans, "The span whose attribute nests too deeply is refused.");
        Assert.IsNotNull(batch.RejectionMessage);
        Assert.AreEqual(InnocentTrace, Assert.ContainsSingle(batch.Spans).TraceId);
        await using var store = new DuckDbStore(":memory:", timeProvider: new FakeTimeProvider(s_now));
        await store.EnqueueAsync(new SpanBatch(IngestionStorageMapper.ToSpanStorageRows(batch)), context.CancellationToken);
        var trace = await TraceAsync(store, InnocentTrace);
        Assert.IsFalse(trace.IsError, trace.Content.GetRawText());
    }

    [TestMethod]
    [DataRow(false, 16, DisplayName = "Key-value lists at the bound")]
    [DataRow(true, 16, DisplayName = "Arrays at the bound")]
    public async Task Export_AttributeNestedToTheBound_StoredAndReadable(bool arrays, int depth)
    {
        var batch = OtlpConverter.ConvertTraceRequest(Export(Nested(depth, arrays)));

        Assert.AreEqual(0L, batch.RejectedSpans, batch.RejectionMessage);
        await using var store = new DuckDbStore(":memory:", timeProvider: new FakeTimeProvider(s_now));
        await store.EnqueueAsync(new SpanBatch(IngestionStorageMapper.ToSpanStorageRows(batch)), context.CancellationToken);
        var trace = await TraceAsync(store, DeepTrace);
        Assert.IsFalse(trace.IsError, trace.Content.GetRawText());
    }

    [TestMethod]
    public async Task TracesGet_RetainedValueNestedPastTheReaders_AnswersARefusal()
    {
        // A store written before ingest bounded the nesting can hold such a value; its trace answers a refusal.
        var value = OtlpAttributeValue.FromString("leaf");
        for (var i = 0; i < 29; i++)
            value = OtlpAttributeValue.FromKeyValueList(new NormalizedAttributes { ["gen_ai.input.messages"] = value });
        var span = new SpanIngestionRecord
        {
            TraceId = DeepTrace,
            SpanId = "b7ad6b7169203331",
            Name = "deep",
            ServiceName = "checkout",
            StartTimeUnixNano = (ulong)(s_now.AddMinutes(-2).ToUnixTimeMilliseconds() * 1_000_000),
            EndTimeUnixNano = (ulong)(s_now.AddMinutes(-1).ToUnixTimeMilliseconds() * 1_000_000),
            ResourceAttributes = new NormalizedAttributes(),
            Attributes = new NormalizedAttributes { ["gen_ai.input.messages"] = value }
        };
        await using var store = new DuckDbStore(":memory:", timeProvider: new FakeTimeProvider(s_now));
        await store.EnqueueAsync(new SpanBatch(IngestionStorageMapper.ToSpanStorageRows(new TraceIngestionBatch([span]))),
            context.CancellationToken);

        var trace = await TraceAsync(store, DeepTrace);

        Assert.IsTrue(trace.IsError);
        Assert.AreEqual("data_unavailable", trace.Content.GetProperty("code").GetString());
    }

    [TestMethod]
    public async Task GrpcExport_RecordsTheStoreCannotEncode_AnswersInvalidArgument()
    {
        // An export whose records storage cannot encode fails the same way on every retry: not UNAVAILABLE.
        var error = await Assert.ThrowsAsync<RpcException>(() =>
            GrpcExport.ExecuteAsync<ExportTraceServiceResponse>(() => throw new JsonException("The value nests too deeply."),
                "trace"));

        Assert.AreEqual(StatusCode.InvalidArgument, error.StatusCode);
    }

    private async Task<ToolRunResult> TraceAsync(DuckDbStore store, string traceId)
    {
        var snapshot = await store.ReadSnapshotAsync("default", ct: context.CancellationToken);
        using var input = JsonDocument.Parse($$"""{"since":"{{Since}}","trace_id":"{{traceId}}","max_bytes":262144}""");
        return ToolDispatcher.Run("traces_get", snapshot, input.RootElement);
    }

    /// <summary>An exporter's request: an ordinary span and one carrying <paramref name="value" />, decoded from protobuf.</summary>
    private static ExportTraceServiceRequest Export(AnyValue value)
    {
        var end = (ulong)(s_now.AddMinutes(-1).ToUnixTimeMilliseconds() * 1_000_000);
        var request = new ExportTraceServiceRequest
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
                    Resource = new Resource
                    {
                        Attributes = { new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = "checkout" } } }
                    },
                    ScopeSpans =
                    {
                        new ScopeSpans
                        {
                            Spans =
                            {
                                NewSpan(InnocentTrace, "innocent", end, null),
                                NewSpan(DeepTrace, "deep", end, new KeyValue { Key = "gen_ai.input.messages", Value = value })
                            }
                        }
                    }
                }
            }
        };
        // The depth must be one an exporter can send: protobuf decodes it within its recursion limit.
        return ExportTraceServiceRequest.Parser.ParseFrom(request.ToByteArray());
    }

    private static Span NewSpan(string traceId, string name, ulong end, KeyValue? attribute)
    {
        var span = new Span
        {
            TraceId = ByteString.CopyFrom(Convert.FromHexString(traceId)),
            SpanId = ByteString.CopyFrom(Convert.FromHexString("b7ad6b7169203331")),
            Name = name,
            StartTimeUnixNano = end - 1_000_000_000UL,
            EndTimeUnixNano = end
        };
        if (attribute is not null) span.Attributes.Add(attribute);
        return span;
    }

    private static AnyValue Nested(int depth, bool arrays)
    {
        var value = new AnyValue { StringValue = "leaf" };
        for (var i = 0; i < depth; i++)
        {
            value = arrays
                ? new AnyValue { ArrayValue = new ArrayValue { Values = { value } } }
                : new AnyValue
                {
                    // Nested entries keep only registry keys; this one is persisted at every level.
                    KvlistValue = new KeyValueList { Values = { new KeyValue { Key = "gen_ai.input.messages", Value = value } } }
                };
        }

        return value;
    }
}
