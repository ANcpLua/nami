using Microsoft.Extensions.Options;
using Nami.Collector.Telemetry;

namespace Nami.Collector.ApiKeys;

internal sealed class CollectorApiKeyMiddleware(RequestDelegate next, IOptions<OtlpApiKeyOptions> configured,
    SelfTelemetryCredential selfTelemetry)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var options = configured.Value;
        var ingest = OtlpConstants.IsOtlpPath(context.Request.Path.Value ?? "");
        var mcp = context.Request.Path.StartsWithSegments("/mcp");
        if (!options.IsApiKeyMode || (!ingest && !mcp))
        {
            await next(context).ConfigureAwait(false);
            return;
        }
        var supplied = context.Request.Headers[OtlpConstants.ApiKeyHeaderName].FirstOrDefault();
        var credential = OtlpApiKeyValidator.Resolve(supplied, options);
        if (credential is null && ingest && context.Connection.RemoteIpAddress is { } remote &&
            System.Net.IPAddress.IsLoopback(remote) && selfTelemetry.Matches(supplied))
            credential = new ProjectApiKey(NamiTelemetry.ServiceName, supplied!, ApiKeyRoles.Ingest);
        var required = ingest ? ApiKeyRoles.Ingest : ApiKeyRoles.Read;
        if (credential is null || (credential.Roles & required) == 0)
        {
            context.Response.Headers.WWWAuthenticate = $"{OtlpConstants.ApiKeyHeaderName} realm=\"nami\"";
            context.Response.StatusCode = credential is null ? 401 : 403;
            if (ingest)
                await OtlpHttpResult.Failure(context.Response.StatusCode, OtlpPayloadEncoding.Json,
                    "Missing or insufficient project credential.").ExecuteAsync(context).ConfigureAwait(false);
            return;
        }
        AuthenticatedProject.Set(context, credential.ProjectId);
        await next(context).ConfigureAwait(false);
    }
}
