using System.Net;
using Microsoft.Extensions.Options;
using Nami.Collector.Ingestion.Primitives;
using Nami.Telemetry;
using HttpContext = Microsoft.AspNetCore.Http.HttpContext;
using RequestDelegate = Microsoft.AspNetCore.Http.RequestDelegate;

namespace Nami.ApiKeys;

/// <summary>
///     The HTTP mirror of <see cref="Nami.Grpc.OtlpApiKeyInterceptor" />: in ApiKey mode every OTLP export must
///     carry a valid <c>x-otlp-api-key</c> with the <c>ingest</c> role, 401 without a valid key and 403 without the
///     role, and 400 when <c>x-nami-project</c> names another project than the key's.
/// </summary>
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

        // The gRPC interceptor's rule: a project the request names must be the credential's own.
        if (context.Request.Headers[ProjectIdentity.HeaderName]
            .Any(requested => ProjectIdentity.Conflicts(requested, credential.ProjectId)))
        {
            await OtlpHttpResult.Failure(StatusCodes.Status400BadRequest, OtlpPayloadEncoding.Json,
                "Project does not match the API key.").ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        AuthenticatedProject.Set(context, credential.ProjectId);
        await next(context).ConfigureAwait(false);
    }
}
