using JetBrains.Annotations;
using Nami.SemanticConventions;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SpanKind = Nami.Tests.SpanKind;

namespace Nami.IntegrationTests.Receiver;

[TestClass]
public sealed class WireSchemaTests
{
    private const string MisleadingSchemaUrlAttribute = "https://attribute.invalid/";
    private const string ResourceSchemaUrl = "https://opentelemetry.io/schemas/1.44.0";
    [UsedImplicitly] public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(null)]
    [DataRow(ResourceSchemaUrl)]
    public async Task OfficialSdkResourceSchema_MisleadingSchemaAttribute_DoesNotReplaceTheWireSchemaUrl(
        string? resourceSchema)
    {
        await using var receiver = await OtlpReceiver.StartAsync(TestContext.CancellationToken);
        using var source = new ActivitySource("wire-" + Guid.NewGuid().ToString("N"), "1.2.3");
        var sourceName = source.Name;
#pragma warning disable Nami0006
        var resource = ResourceBuilder.CreateEmpty().AddAttributes(
            new Dictionary<string, object>
            {
                [ServiceAttributes.Name] = "schema-producer", ["telemetry.schema_url"] = MisleadingSchemaUrlAttribute
            },
            resourceSchema);
#pragma warning restore Nami0006
        using var provider = Sdk.CreateTracerProviderBuilder().SetResourceBuilder(resource).AddSource(sourceName)
            .AddOtlpCapture(receiver.Endpoint).Build();
        using var activity = source.CreateActivity("wire.operation", ActivityKind.Server)!.Start();
        activity.SetStatus(ActivityStatusCode.Ok).SetTag(HttpAttributes.RequestMethod, "POST");
        var traceId = activity.TraceId.ToHexString();
        activity.Stop();
        var capture = await receiver.AwaitAsync(s => s.Spans.Count == 1, OtlpReceiver.DefaultEvidenceTimeout,
            TestContext.CancellationToken);
        var span = Assert.ContainsSingle(capture.Spans);
        Assert.AreEqual(traceId, span.TraceId);
        Assert.AreEqual("wire.operation", span.Name);
        Assert.AreEqual(SpanKind.Server, span.Kind);
        Assert.AreEqual(ActivityStatusCode.Ok, span.Status);
        Assert.AreEqual(source.Name, span.Scope?.Name);
        Assert.AreEqual("1.2.3", span.Scope?.Version);
        Assert.AreEqual(resourceSchema, span.Resource.SchemaUrl);
        Assert.AreEqual(MisleadingSchemaUrlAttribute, span.Resource.Attributes["telemetry.schema_url"]);
        Assert.AreEqual("schema-producer", span.Service);
        Assert.AreEqual("POST", span.GetString("http.request.method"));
    }
}
