using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Nami.ApiKeys;
using Nami.Telemetry;

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
