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
///     role, and 400 when <c>x-nami-project</c> names another project than the key's. Each refusal is a Status in
///     the encoding of the request.
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
            await RefuseAsync(context, credential is null ? 401 : 403, "Missing or insufficient project credential.")
                .ConfigureAwait(false);
            return;
        }

        // The gRPC interceptor's rule: a project the request names must be the credential's own.
        if (context.Request.Headers[ProjectIdentity.HeaderName]
            .Any(requested => ProjectIdentity.Conflicts(requested, credential.ProjectId)))
        {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, "Project does not match the API key.")
                .ConfigureAwait(false);
            return;
        }

        AuthenticatedProject.Set(context, credential.ProjectId);
        await next(context).ConfigureAwait(false);
    }

    /// <summary>
    ///     OTLP/HTTP answers a failed export with a Status in the encoding of the request: binary protobuf for
    ///     <c>application/x-protobuf</c>, JSON otherwise, which is also what the endpoints answer for a media type
    ///     OTLP does not define.
    /// </summary>
    private static Task RefuseAsync(HttpContext context, int statusCode, string message)
    {
        var encoding = OtlpPayloadParser.TryGetEncoding(context.Request.ContentType, out var requested)
            ? requested
            : OtlpPayloadEncoding.Json;
        return OtlpHttpResult.Failure(statusCode, encoding, message).ExecuteAsync(context);
    }
}
