using Nami.Collector.Ingestion;
using OpenTelemetry.Proto.Collector.Metrics.V1;

namespace Nami.IntegrationTests;

/// <summary>
///     An exemplar's trace and span ids follow the rule of every other OTLP id the converter reads: absent, or the exact
///     number of bytes and not all zero. A wrong id rejects its point and the rejection names the field.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class OtlpIdentifierTests
{
    // One gauge point with one exemplar; <ids> becomes the exemplar's id members.
    private const string Gauge = """
        {"resourceMetrics":[{"scopeMetrics":[{"metrics":[{"name":"identity.requests","gauge":{"dataPoints":[{
          "timeUnixNano":"1761307200000000000","asInt":"7",
          "exemplars":[{"timeUnixNano":"1761307200000000000","asInt":"1"<ids>}]}]}}]}]}]}
        """;

    [TestMethod]
    [DataRow(",\"traceId\":\"AQID\"", "trace_id", "16-byte", DisplayName = "Three-byte trace id")]
    [DataRow(",\"spanId\":\"AAAAAAAAAAA=\"", "span_id", "8-byte", DisplayName = "All-zero span id")]
    public void ConvertMetrics_ExemplarIdWrongLengthOrZero_RejectsItsPoint(string ids, string field, string length)
    {
        var batch = ConvertGauge(ids);

        Assert.IsEmpty(batch.Points);
        Assert.AreEqual(1L, batch.RejectedDataPoints);
        Assert.IsNotNull(batch.RejectionMessage);
        Assert.Contains("exemplar", batch.RejectionMessage);
        Assert.Contains(field, batch.RejectionMessage);
        Assert.Contains($"nonzero {length} identifier", batch.RejectionMessage);
    }

    [TestMethod]
    public void ConvertMetrics_ExemplarIdsAbsentOrExact_KeepsThePointWithHexIds()
    {
        var absent = Assert.ContainsSingle(Assert.ContainsSingle(ConvertGauge("").Points).Exemplars);
        var exact = Assert.ContainsSingle(Assert.ContainsSingle(
            ConvertGauge(",\"traceId\":\"AQIDBAUGBwgJCgsMDQ4PEA==\",\"spanId\":\"AQIDBAUGBwg=\"").Points).Exemplars);

        Assert.IsNull(absent.TraceId);
        Assert.IsNull(absent.SpanId);
        Assert.AreEqual("0102030405060708090a0b0c0d0e0f10", exact.TraceId);
        Assert.AreEqual("0102030405060708", exact.SpanId);
    }

    private static MetricIngestionBatch ConvertGauge(string exemplarIds) =>
        OtlpConverter.ConvertMetrics(ExportMetricsServiceRequest.Parser.ParseJson(
            Gauge.Replace("<ids>", exemplarIds, StringComparison.Ordinal)));
}
