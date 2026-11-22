using System.Net;
using System.Text;
using Nami.Collector.Ingestion;
using Nami.Collector.Ingestion.Configuration;
using Nami.Collector.Ingestion.Primitives;
using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

/// <summary>
///     The names an OTLP/HTTP export may carry, the rules of /mcp (src/nami.mcp/src/http-security.ts): a page whose
///     name was rebound to this machine sends that name as Host and as Origin, and without a key nothing else tells
///     it from a local exporter. Each case is a real request against the listener of a collector composed as
///     Program.cs composes it.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class OtlpRequestNamesTests(TestContext context)
{
    private const string Keys = "{\"a\":[{\"key\":\"ingest-a\",\"roles\":[\"ingest\"]}]}";
    private int _exports;

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task WithoutAKey_AForeignHost_IsRefused_AndNothingIsStored()
    {
        await using var host = await CollectorHost.StartAsync(DataPath("host"), context.CancellationToken);
        var port = host.HttpEndpoint.Port;

        // A rebound name, the wildcard address a listener binds but no client reaches it by, and the name of a
        // platform's health probe, which /mcp answers for GET /health alone.
        foreach (var name in (string[])
                 [$"rebind.example:{port}", "rebind.example", $"0.0.0.0:{port}", "healthcheck.railway.app"])
        {
            using var refused = await PostAsync(host, name, null);
            Assert.AreEqual(HttpStatusCode.Forbidden, refused.StatusCode, name);
            Assert.AreEqual("{ \"message\": \"Invalid Host.\" }",
                await refused.Content.ReadAsStringAsync(context.CancellationToken), name);
        }

        using var probe = new HttpRequestMessage(HttpMethod.Get, new Uri(host.HttpEndpoint, "v1/logs"));
        probe.Headers.Host = "healthcheck.railway.app";
        using var answered = await TelemetryLifecycle.Http.SendAsync(probe, context.CancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, answered.StatusCode);

        Assert.AreEqual(0, await RecordedAsync(host, ProjectIdentity.DefaultProjectId));
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task AForeignOrigin_IsRefused_AndNothingIsStored()
    {
        await using var host = await CollectorHost.StartAsync(DataPath("origin"), context.CancellationToken);
        var port = host.HttpEndpoint.Port;

        foreach (var origin in (string[])
                 [$"http://rebind.example:{port}", "https://rebind.example", $"http://0.0.0.0:{port}", "null",
                     "file://localhost", "localhost"])
        {
            using var refused = await PostAsync(host, null, origin);
            Assert.AreEqual(HttpStatusCode.Forbidden, refused.StatusCode, origin);
            Assert.AreEqual("{ \"message\": \"Invalid Origin.\" }",
                await refused.Content.ReadAsStringAsync(context.CancellationToken), origin);
        }

        using var preflight = new HttpRequestMessage(HttpMethod.Options, new Uri(host.HttpEndpoint, "v1/logs"));
        preflight.Headers.Add("Origin", "https://rebind.example");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        using var answered = await TelemetryLifecycle.Http.SendAsync(preflight, context.CancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, answered.StatusCode);

        Assert.AreEqual(0, await RecordedAsync(host, ProjectIdentity.DefaultProjectId));
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task ALocalExport_IsAccepted_UnderEveryNameOfThisMachine()
    {
        await using var host = await CollectorHost.StartAsync(DataPath("local"), context.CancellationToken);
        var port = host.HttpEndpoint.Port;

        // An exporter sends no Origin and names the address it was configured with.
        using (var plain = await host.PostJsonAsync(Signals.Logs, Export(), context.CancellationToken))
            Assert.AreEqual(HttpStatusCode.OK, plain.StatusCode);
        using (var protobuf = await host.PostProtobufAsync(Signals.Logs,
                   CollectorHost.Encode(Signals.Logs, Export()), context.CancellationToken))
            Assert.AreEqual(HttpStatusCode.OK, protobuf.StatusCode);
        foreach (var name in (string[]) [$"localhost:{port}", "LOCALHOST", $"[::1]:{port}", "127.0.0.1"])
        {
            using var accepted = await PostAsync(host, name, null);
            Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode, name);
        }

        // A page of this machine has no fixed port.
        foreach (var origin in (string[]) ["http://localhost:3000", "https://127.0.0.1:8443", "http://[::1]"])
        {
            using var accepted = await PostAsync(host, null, origin);
            Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode, origin);
        }

        var snapshot = await host.Store.ReadSnapshotAsync(ProjectIdentity.DefaultProjectId,
            ct: context.CancellationToken);
        Assert.HasCount(9, snapshot.Logs);
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task ThePublicUrl_NamesOneMoreHost_AndOneOriginInSchemeHostAndPort()
    {
        await using var host = await CollectorHost.StartAsync(DataPath("public"), context.CancellationToken,
            settings: new Dictionary<string, string?>
            {
                [CollectorConfigurationKeys.PublicUrl] = "https://nami.example"
            });

        foreach (var (name, origin, expected) in ((string?, string?, HttpStatusCode)[])
                 [
                     ("nami.example", null, HttpStatusCode.OK),
                     ("nami.example:4318", "https://nami.example", HttpStatusCode.OK),
                     (null, "https://nami.example:443", HttpStatusCode.OK),
                     (null, "http://nami.example", HttpStatusCode.Forbidden),
                     (null, "https://nami.example:8443", HttpStatusCode.Forbidden),
                     (null, "https://nami.example.rebind.example", HttpStatusCode.Forbidden),
                     ("other.example", null, HttpStatusCode.Forbidden)
                 ])
        {
            using var response = await PostAsync(host, name, origin);
            Assert.AreEqual(expected, response.StatusCode, $"Host {name}, Origin {origin}");
        }

        var snapshot = await host.Store.ReadSnapshotAsync(ProjectIdentity.DefaultProjectId,
            ct: context.CancellationToken);
        Assert.HasCount(3, snapshot.Logs);
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task AnOriginTheOperatorAllowsForCors_IsAnOriginOfAnExport()
    {
        await using var host = await CollectorHost.StartAsync(DataPath("cors"), context.CancellationToken,
            settings: new Dictionary<string, string?>
            {
                [CollectorConfigurationKeys.CorsAllowedOrigins] = "https://app.example"
            });

        using (var allowed = await PostAsync(host, null, "https://app.example"))
        {
            Assert.AreEqual(HttpStatusCode.OK, allowed.StatusCode);
            Assert.AreEqual("https://app.example",
                allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());
        }

        using (var other = await PostAsync(host, null, "https://other.example"))
            Assert.AreEqual(HttpStatusCode.Forbidden, other.StatusCode);

        var snapshot = await host.Store.ReadSnapshotAsync(ProjectIdentity.DefaultProjectId,
            ct: context.CancellationToken);
        Assert.HasCount(1, snapshot.Logs);
    }

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    public async Task WithAKey_TheKeyAdmitsAnExportUnderAnyHost_AndAForeignOriginIsStillRefused()
    {
        await using var host = await CollectorHost.StartAsync(DataPath("key"), context.CancellationToken, Keys);

        // A producer in another container reaches nami under a name nami does not know; no page holds the key.
        using (var named = await PostAsync(host, "nami:4318", null, "ingest-a", "a"))
            Assert.AreEqual(HttpStatusCode.OK, named.StatusCode);
        using (var unkeyed = await PostAsync(host, "nami:4318", null, project: "a"))
            Assert.AreEqual(HttpStatusCode.Unauthorized, unkeyed.StatusCode);
        using (var page = await PostAsync(host, null, "https://rebind.example", "ingest-a", "a"))
            Assert.AreEqual(HttpStatusCode.Forbidden, page.StatusCode);

        var snapshot = await host.Store.ReadSnapshotAsync("a", ct: context.CancellationToken);
        Assert.HasCount(1, snapshot.Logs);
    }

    private string DataPath(string name)
    {
        return Path.Combine(
            context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"),
            "names-" + name + ".duckdb");
    }

    /// <summary>One log record, with a body no other export of the test has.</summary>
    private string Export(string project = "default")
    {
        return OtlpData.Logs(TestData.Nanos(DateTimeOffset.UtcNow.AddSeconds(-2)), "export " + ++_exports, project);
    }

    /// <summary>One logs export as a browser's fetch or an exporter sends it, under the given Host and Origin.</summary>
    private async Task<HttpResponseMessage> PostAsync(CollectorHost host, string? name, string? origin,
        string? key = null, string project = "default")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(host.HttpEndpoint, "v1/logs"));
        request.Content = new StringContent(Export(project), Encoding.UTF8, "application/json");
        if (name is not null) request.Headers.Host = name;
        if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin);
        if (key is not null) request.Headers.Add(OtlpConstants.ApiKeyHeaderName, key);
        return await TelemetryLifecycle.Http.SendAsync(request, context.CancellationToken);
    }

    private async Task<int> RecordedAsync(CollectorHost host, string project)
    {
        var snapshot = await host.Store.ReadSnapshotAsync(project, ct: context.CancellationToken);
        return snapshot.Spans.Count + snapshot.Logs.Count + snapshot.Metrics.Count + snapshot.Signals.Count +
               snapshot.Resources.Count;
    }
}
