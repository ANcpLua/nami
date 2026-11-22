using Nami.Collector.Ingestion;

namespace Nami.UnitTests.Hosting;

/// <summary>
///     How the OTLP CORS options read their comma-separated lists: entries trimmed, empty entries dropped, and the
///     allowed headers always led by the two nami needs, each header once whatever its case.
/// </summary>
[TestClass]
[TestCategory("Fast")]
public sealed class OtlpCorsOptionsListTests
{
    [TestMethod]
    public void Should_ReadNoOriginAndOnlyTheDefaultHeaders_When_NothingIsConfigured()
    {
        var options = new OtlpCorsOptions();

        Assert.IsFalse(options.IsEnabled);
        Assert.IsEmpty(options.GetOrigins().ToArray());
        Assert.AreSequenceEqual(["content-type", "x-otlp-api-key"], options.GetHeaders().ToArray());
    }

    [TestMethod]
    public void Should_TrimDropEmptyAndDeduplicate_When_ListsAreConfigured()
    {
        var options = new OtlpCorsOptions
        {
            AllowedOrigins = " https://a.test , ,https://b.test,",
            AllowedHeaders = "X-Custom, Content-Type ,, x-custom , X-OTLP-API-KEY,x-other"
        };

        Assert.IsTrue(options.IsEnabled);
        Assert.AreSequenceEqual(["https://a.test", "https://b.test"], options.GetOrigins().ToArray());
        Assert.AreSequenceEqual(["content-type", "x-otlp-api-key", "X-Custom", "x-other"],
            options.GetHeaders().ToArray());
    }
}
