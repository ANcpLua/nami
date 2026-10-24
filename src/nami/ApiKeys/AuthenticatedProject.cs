using Microsoft.Extensions.Options;

namespace Nami.ApiKeys;

internal static class AuthenticatedProject
{
    private static readonly object s_httpKey = new();
    private static readonly object s_grpcKey = new();

    public static void Set(HttpContext context, string projectId)
    {
        context.Items[s_httpKey] = projectId;
    }

    public static string? ForHttpIngest(HttpContext context)
    {
        return FromHttp(context);
    }

    private static string? FromHttp(HttpContext context)
    {
        if (context.Items.TryGetValue(s_httpKey, out var value) && value is string projectId)
            return projectId;

        if (context.RequestServices.GetService<IOptions<OtlpApiKeyOptions>>()?.Value.IsApiKeyMode == true)
            throw new InvalidOperationException("Authenticated project scope is missing.");

        return null;
    }

    public static void Set(ServerCallContext context, string projectId)
    {
        context.UserState[s_grpcKey] = projectId;
    }

    public static string? ForGrpcIngest(ServerCallContext context, OtlpApiKeyOptions? options)
    {
        if (context.UserState.TryGetValue(s_grpcKey, out var value) && value is string projectId)
            return projectId;

        if (options?.IsApiKeyMode == true)
            throw new InvalidOperationException("Authenticated project scope is missing.");

        return null;
    }
}
