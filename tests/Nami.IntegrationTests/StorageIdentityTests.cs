using Microsoft.Extensions.Time.Testing;
using Nami.Collector.Ingestion;
using Nami.Collector.Storage;
using Nami.Tests.Infrastructure;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;

namespace Nami.IntegrationTests;

/// <summary>
///     Stored rows key on ids hashed from their content, so the same record must keep the id it was stored with. The
///     values were taken from the build of 843383e, before the log, instrument and series ids shared one recipe. The
///     log id was taken again when the record's place in its request became part of every log key (ledger N62): it
///     is the id of this record at the first place. Rows stored before that keep the id without the place.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class StorageIdentityTests
{
    private const string Logs = """
        {"resourceLogs":[{
          "resource":{"attributes":[
            {"key":"service.name","value":{"stringValue":"identity-checkout"}},
            {"key":"nami.project.id","value":{"stringValue":"identity"}}]},
          "schemaUrl":"https://opentelemetry.io/schemas/1.40.0",
          "scopeLogs":[{"scope":{"name":"identity-tests","version":"1.0.0"},"logRecords":[{
            "timeUnixNano":"1761307200000000000","observedTimeUnixNano":"1761307200000000001",
            "severityNumber":9,"severityText":"INFO","body":{"stringValue":"order accepted"},
            "attributes":[{"key":"http.request.method","value":{"stringValue":"GET"}}],
            "traceId":"AQIDBAUGBwgJCgsMDQ4PEA==","spanId":"AQIDBAUGBwg="}]}]}]}
        """;

    private const string Metrics = """
        {"resourceMetrics":[{
          "resource":{"attributes":[
            {"key":"service.name","value":{"stringValue":"identity-checkout"}},
            {"key":"nami.project.id","value":{"stringValue":"identity"}}]},
          "schemaUrl":"https://opentelemetry.io/schemas/1.40.0",
          "scopeMetrics":[{"scope":{"name":"identity-tests","version":"1.0.0"},"metrics":[{
            "name":"identity.requests","unit":"{request}","description":"Requests seen by the identity test.",
            "sum":{"aggregationTemporality":2,"isMonotonic":true,"dataPoints":[{
              "startTimeUnixNano":"1761307100000000000","timeUnixNano":"1761307200000000000","asInt":"7",
              "attributes":[{"key":"http.request.method","value":{"stringValue":"GET"}}]}]}}]}]}]}
        """;

    [TestMethod]
    public void StorageIds_FixedRecords_KeepTheIdsTheyWereStoredWith()
    {
        var logs = OtlpConverter.ConvertLogs(ExportLogsServiceRequest.Parser.ParseJson(Logs),
            new FakeTimeProvider(TestData.s_now));
        var log = Assert.ContainsSingle(IngestionStorageMapper.ToLogStorageRows(logs));
        var metrics = MetricStorageMapper.ToStorageRows(
            OtlpConverter.ConvertMetrics(ExportMetricsServiceRequest.Parser.ParseJson(Metrics)));
        var series = Assert.ContainsSingle(metrics.Series);
        var observation = Assert.ContainsSingle(metrics.Observations);

        Assert.AreEqual("log_4d0ec50d221cb0119ed9d24b8ec2083d", log.LogId);
        Assert.AreEqual("mi_e2babd097554c5e098dfc3ac5a191f84", series.InstrumentId);
        Assert.AreEqual("ms_2f23b323e8890b1b61e0f205f1ea6e26", series.SeriesId);
        Assert.AreEqual("mo_54634891374fae16223f16f1162d5a9dcb8df68666945730e44f022f0a06add4",
            observation.ObservationId);
        Assert.AreEqual("mm_c60ab6e240f682126809b15704d3957f9d6a1ce3ce26c5466a1a8c662ebc5d88",
            observation.MeasurementId);
    }
}
