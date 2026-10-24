using System.Threading.Tasks;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Options;
using Nami.ApiKeys;
using Nami.Collector.Ingestion.Primitives;

namespace Nami.Grpc;

/// <summary>
///     The gRPC mirror of <see cref="Nami.ApiKeys.CollectorApiKeyMiddleware" />: in ApiKey
///     mode every OTLP export call must carry a valid <c>x-otlp-api-key</c> metadata entry with the
///     <c>ingest</c> role — the same project bindings and fixed-time validation, <c>Unauthenticated</c>
///     instead of 401, <c>PermissionDenied</c> instead of 403, and <c>InvalidArgument</c> instead of
///     400 when <c>x-nami-project</c> names another project.
///     All OTLP export methods are unary, so the unary handler is the whole boundary.
/// </summary>
internal sealed class OtlpApiKeyInterceptor(IOptions<OtlpApiKeyOptions> configured) : Interceptor
{
    private readonly OtlpApiKeyOptions _options = configured.Value;

    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        if (_options.IsApiKeyMode)
        {
            if (OtlpApiKeyValidator.Resolve(
                    context.RequestHeaders.GetValue(OtlpConstants.ApiKeyHeaderName), _options) is not { } credential)
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing or invalid API key."));

            if ((credential.Roles & ApiKeyRoles.Ingest) is ApiKeyRoles.None)
                throw new RpcException(new Status(StatusCode.PermissionDenied, "The API key lacks the ingest role."));

            if (ProjectIdentity.Conflicts(context.RequestHeaders.GetValue(ProjectIdentity.HeaderName),
                    credential.ProjectId))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Project does not match the API key."));

            AuthenticatedProject.Set(context, credential.ProjectId);
        }

        return continuation(request, context);
    }
}
