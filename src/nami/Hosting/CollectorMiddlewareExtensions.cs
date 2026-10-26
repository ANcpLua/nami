using Microsoft.Extensions.Options;
using Nami.ApiKeys;

namespace Nami.Hosting;

internal static class CollectorMiddlewareExtensions
{
    public static void UseNamiCollectorMiddleware(this WebApplication app)
    {
        if (app.Services.GetRequiredService<IOptions<OtlpCorsOptions>>().Value.IsEnabled)
            app.UseMiddleware<OtlpCorsMiddleware>();
        app.UseMiddleware<CollectorApiKeyMiddleware>();
    }
}
