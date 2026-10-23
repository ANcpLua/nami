using Microsoft.Extensions.Options;
using Nami.Collector.Ingestion.Primitives;

namespace Nami.Collector.ApiKeys;

/// <summary>
/// API-key boundary for the collector's HTTP surface. In ApiKey mode it
/// guards both the OTLP ingest routes (<c>/v1/*</c>) and the product API (<c>/api/v1/*</c>) with
/// a project-bound credential sent as the generated contract's <c>x-otlp-api-key</c> header, and
/// requires the role each route declares: <c>ingest</c> for OTLP, <c>read</c>, <c>write</c> or
/// <c>delete</c> for the product API (NAMI-A2).
/// <c>/health</c> and <c>/alive</c> stay open for unauthenticated liveness probes.
/// The gRPC ingest boundary is <see cref="Nami.Collector.Grpc.OtlpApiKeyInterceptor"/>.
/// </summary>
internal sealed class CollectorApiKeyMiddleware(RequestDelegate next, IOptions<OtlpApiKeyOptions> configured)
{
    private readonly OtlpApiKeyOptions _options = configured.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        var isProductApi = path.StartsWith("/api/v1", StringComparison.OrdinalIgnoreCase);

        if (isProductApi)
            context.Response.Headers.CacheControl = "private, no-store";

        if (!_options.IsApiKeyMode || (!OtlpConstants.IsOtlpPath(path) && !isProductApi))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var credential = OtlpApiKeyValidator.Resolve(
            context.Request.Headers[OtlpConstants.ApiKeyHeaderName].FirstOrDefault(), _options);
        if (credential is null)
        {
            var challenge = $"{OtlpConstants.ApiKeyHeaderName} realm=\"nami-otlp\"";
            context.Response.Headers.WWWAuthenticate = challenge;
            if (isProductApi)
            {
                await ContractErrorResults.WriteUnauthorizedAsync(
                    context.Response,
                    challenge,
                    context.RequestAborted).ConfigureAwait(false);
            }
            else
            {
                await OtlpFailureAsync(context, StatusCodes.Status401Unauthorized, "Missing or invalid API key.")
                    .ConfigureAwait(false);
            }

            return;
        }

        var requiredRole = isProductApi
            ? context.GetEndpoint()?.Metadata.GetMetadata<ApiKeyRoleRequirement>()?.Role
              ?? ApiKeyRoleRequirement.ForMethod(context.Request.Method)
            : ApiKeyRoles.Ingest;
        if ((credential.Roles & requiredRole) is ApiKeyRoles.None)
        {
            if (isProductApi)
            {
                await ContractErrorResults.WriteForbiddenAsync(
                    context.Response,
                    ApiKeyRoleNames.Name(requiredRole),
                    context.RequestAborted).ConfigureAwait(false);
            }
            else
            {
                await OtlpFailureAsync(context, StatusCodes.Status403Forbidden, "The API key lacks the ingest role.")
                    .ConfigureAwait(false);
            }

            return;
        }

        if (ProjectIdentity.Conflicts(context.Request.Headers[ProjectIdentity.HeaderName].FirstOrDefault(),
                credential.ProjectId))
        {
            if (isProductApi)
                await ContractErrorResults.WriteValidationAsync(
                    context.Response, ProjectIdentity.HeaderName, "Project does not match the API key.",
                    "project.mismatch", cancellationToken: context.RequestAborted).ConfigureAwait(false);
            else
                await OtlpFailureAsync(context, StatusCodes.Status400BadRequest, "Project does not match the API key.")
                    .ConfigureAwait(false);
            return;
        }

        AuthenticatedProject.Set(context, credential.ProjectId);
        await next(context).ConfigureAwait(false);
    }

    private static Task OtlpFailureAsync(HttpContext context, int statusCode, string message) =>
        OtlpHttpResult.Failure(statusCode, DetectEncoding(context.Request.ContentType), message)
            .ExecuteAsync(context);

    private static OtlpPayloadEncoding DetectEncoding(string? contentType)
    {
        try
        {
            return OtlpPayloadParser.GetEncoding(contentType);
        }
        catch (OtlpUnsupportedMediaTypeException)
        {
            // An unsupported request has no OTLP response encoding to mirror. Valid protobuf and
            // JSON requests always retain their wire type; JSON is the interoperable fallback.
            return OtlpPayloadEncoding.Json;
        }
    }
}
