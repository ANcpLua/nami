using Nami.Api.Contracts.Common;
using Nami.Api.Contracts.OTel.Metrics;
using Nami.Collector.Ingestion;
using Nami.Collector.Ingestion.Primitives;
using Nami.Collector.Storage;

namespace Nami.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
public sealed class MetricIdentityTests
{
    [TestMethod]
    [DataRow(0L, -0d, DisplayName = "Negative zero matches integer zero")]
    [DataRow(42L, 42d, DisplayName = "Positive integral double matches integer")]
    [DataRow(-42L, -42d, DisplayName = "Negative integral double matches integer")]
    [DataRow(long.MinValue, -9_223_372_036_854_775_808d, DisplayName = "Inclusive Int64 lower bound")]
    [DataRow(9_223_372_036_854_774_784L, 9_223_372_036_854_774_784d,
        DisplayName = "Largest double below Int64 upper bound")]
    public void MeasurementIdentity_ExactIntegralDouble_MatchesInteger(long referenceValue, double number)
    {
        var integerRow = Encode(new AttributeIntValue { Value = referenceValue });
        var doubleRow = Encode(new AttributeDoubleValue { Value = number });

        Assert.AreEqual(integerRow.MeasurementId, doubleRow.MeasurementId);
        Assert.AreNotEqual(integerRow.ObservationId, doubleRow.ObservationId);
    }

    [TestMethod]
    [DataRow(long.MinValue, 9_223_372_036_854_775_808d, DisplayName = "Exclusive Int64 upper bound")]
    [DataRow(long.MaxValue, 9_223_372_036_854_775_808d, DisplayName = "Rounded Int64 maximum stays distinct")]
    [DataRow(long.MinValue, -9_223_372_036_854_777_856d, DisplayName = "Double below Int64 lower bound")]
    [DataRow(1L, 1.5d, DisplayName = "Fraction stays distinct")]
    [DataRow(1L, 1.0000000000000002d, DisplayName = "Adjacent double above one stays distinct")]
    [DataRow(1L, 0.9999999999999999d, DisplayName = "Adjacent double below one stays distinct")]
    [DataRow(0L, double.Epsilon, DisplayName = "Smallest positive double stays distinct")]
    [DataRow(long.MinValue, double.NaN, DisplayName = "NaN stays distinct")]
    [DataRow(long.MinValue, double.PositiveInfinity, DisplayName = "Positive infinity stays distinct")]
    [DataRow(long.MinValue, double.NegativeInfinity, DisplayName = "Negative infinity stays distinct")]
    public void MeasurementIdentity_NonEquivalentDouble_StaysDistinct(long referenceValue, double number)
    {
        var integerRow = Encode(new AttributeIntValue { Value = referenceValue });
        var doubleRow = Encode(new AttributeDoubleValue { Value = number });

        Assert.AreNotEqual(integerRow.MeasurementId, doubleRow.MeasurementId);
    }

    [TestMethod]
    public void MeasurementIdentity_AbsentValue_StaysDistinctFromZero()
    {
        Assert.AreNotEqual(Encode(new AttributeIntValue { Value = 0 }).MeasurementId,
            Encode(null).MeasurementId);
    }

    private static MetricObservationRow Encode(MetricNumberValue? value) =>
        MetricObservationCodec.FromIngestion(new MetricPointIngestionRecord
        {
            MetricName = "test.numeric_identity",
            Kind = MetricKind.Gauge,
            Temporality = MetricTemporality.Unspecified,
            IsMonotonic = false,
            ServiceName = "identity-tests",
            Attributes = new Dictionary<string, OtlpAttributeValue>(),
            ResourceAttributes = new Dictionary<string, OtlpAttributeValue>(),
            TimeUnixNano = 1_000_000_000,
            Data = new MetricNumberPointData { Value = value }
        }, "identity-tests", "numeric-series");
}
