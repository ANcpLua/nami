using System.Net;
using Microsoft.Extensions.Options;
using Nami.Telemetry;

namespace Nami.ApiKeys;

internal sealed class CollectorApiKeyMiddleware(
    RequestDelegate next,
    IOptions<OtlpApiKeyOptions> configured,
    SelfTelemetryCredential selfTelemetry)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var options = configured.Value;
        var ingest = OtlpConstants.IsOtlpPath(context.Request.Path.Value ?? "");
        if (!options.IsApiKeyMode || !ingest)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var supplied = context.Request.Headers[OtlpConstants.ApiKeyHeaderName].FirstOrDefault();
        var credential = OtlpApiKeyValidator.Resolve(supplied, options);
        if (credential is null && context.Connection.RemoteIpAddress is { } remote &&
            IPAddress.IsLoopback(remote) && selfTelemetry.Matches(supplied))
            credential = new ProjectApiKey(NamiTelemetry.ServiceName, supplied!, ApiKeyRoles.Ingest);
        const ApiKeyRoles Required = ApiKeyRoles.Ingest;
        if (credential is null || (credential.Roles & Required) == 0)
        {
            context.Response.Headers.WWWAuthenticate = $"{OtlpConstants.ApiKeyHeaderName} realm=\"nami\"";
            context.Response.StatusCode = credential is null ? 401 : 403;
            await OtlpHttpResult.Failure(context.Response.StatusCode, OtlpPayloadEncoding.Json,
                "Missing or insufficient project credential.").ExecuteAsync(context).ConfigureAwait(false);

            return;
        }

        AuthenticatedProject.Set(context, credential.ProjectId);
        await next(context).ConfigureAwait(false);
    }
}
