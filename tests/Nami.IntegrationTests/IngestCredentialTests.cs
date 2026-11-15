using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Nami.ApiKeys;
using Nami.Telemetry;
using RpcStatus = Google.Rpc.Status;

namespace Nami.IntegrationTests;

/// <summary>
///     The credential boundary of OTLP/HTTP ingest in ApiKey mode, called as the pipeline calls it. gRPC has the
///     same rules in <c>OtlpApiKeyInterceptor</c>.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class IngestCredentialTests
{
    private const string IngestKey = "alpha-ingest-key";
    private const string ReadKey = "alpha-read-key";

    private static readonly OtlpApiKeyOptions s_options = new()
    {
        AuthMode = OtlpApiKeyOptions.ApiKeyMode,
        Keys =
        [
            new ProjectApiKey("alpha", IngestKey, ApiKeyRoles.Ingest),
            new ProjectApiKey("alpha", ReadKey, ApiKeyRoles.Read)
        ]
    };

    [TestMethod]
    [DataRow("beta", 400, DisplayName = "Another project")]
    [DataRow("alpha,beta", 400, DisplayName = "Two projects in one value")]
    [DataRow("alpha", 200, DisplayName = "The key's project")]
    [DataRow(" alpha ", 200, DisplayName = "The key's project with spaces")]
    [DataRow("", 200, DisplayName = "Blank")]
    [DataRow(null, 200, DisplayName = "Absent")]
    public async Task Http_ProjectHeader_MustNameTheKeysProject(string? requested, int status)
    {
        var export = await ExportAsync("application/json", IngestKey, requested);
        Assert.AreEqual(status, export.Status);
        Assert.AreEqual(status == 200 ? "alpha" : null, export.Project);
        if (status == 400)
            Assert.AreEqual("{ \"message\": \"Project does not match the API key.\" }", Encoding.UTF8.GetString(export.Body));
    }

    /// <summary>
    ///     OTLP/HTTP answers a failed export with a Status in the encoding of the request, so an exporter that
    ///     sent protobuf can read why it was refused.
    /// </summary>
    [TestMethod]
    [DataRow(null, null, 401, "Missing or insufficient project credential.", DisplayName = "No key")]
    [DataRow(ReadKey, null, 403, "Missing or insufficient project credential.", DisplayName = "Key without the ingest role")]
    [DataRow(IngestKey, "beta", 400, "Project does not match the API key.", DisplayName = "Another project")]
    public async Task Http_RefusedExport_AnswersInTheEncodingOfTheRequest(string? key, string? project, int status,
        string message)
    {
        foreach (var contentType in new[] { "application/x-protobuf", "application/x-protobuf; proto=x" })
        {
            var binary = await ExportAsync(contentType, key, project);
            Assert.AreEqual(status, binary.Status, contentType);
            Assert.AreEqual("application/x-protobuf", binary.ContentType, contentType);
            Assert.AreEqual(message, RpcStatus.Parser.ParseFrom(binary.Body).Message, contentType);
        }

        foreach (var contentType in new[] { "application/json", "text/plain", null })
        {
            var json = await ExportAsync(contentType, key, project);
            Assert.AreEqual(status, json.Status, contentType);
            Assert.AreEqual("application/json", json.ContentType, contentType);
            Assert.AreEqual(message, RpcStatus.Parser.ParseJson(Encoding.UTF8.GetString(json.Body)).Message, contentType);
        }
    }

    /// <summary>
    ///     One export through the middleware. Project is the project the endpoint would store under, or null
    ///     when the middleware answered the request itself.
    /// </summary>
    private static async Task<(int Status, string? ContentType, byte[] Body, string? Project)> ExportAsync(
        string? contentType, string? key, string? project)
    {
        string? forwarded = null;
        var middleware = new CollectorApiKeyMiddleware(context =>
        {
            forwarded = AuthenticatedProject.ForHttpIngest(context);
            return Task.CompletedTask;
        }, Options.Create(s_options), new SelfTelemetryCredential());
        using var body = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/v1/traces";
        context.Request.ContentType = contentType;
        if (key is not null) context.Request.Headers["x-otlp-api-key"] = key;
        if (project is not null) context.Request.Headers["x-nami-project"] = project;
        context.Response.Body = body;
        await middleware.InvokeAsync(context);
        return (context.Response.StatusCode, context.Response.ContentType, body.ToArray(), forwarded);
    }
}
