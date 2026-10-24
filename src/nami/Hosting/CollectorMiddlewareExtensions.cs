using Microsoft.Extensions.Options;
using Nami.Collector.ApiKeys;

namespace Nami.Collector.Hosting;

internal static class CollectorMiddlewareExtensions
{
    public static WebApplication UseNamiCollectorMiddleware(this WebApplication app)
    {
        if (app.Services.GetRequiredService<IOptions<OtlpCorsOptions>>().Value.IsEnabled)
            app.UseMiddleware<OtlpCorsMiddleware>();
        app.UseMiddleware<CollectorApiKeyMiddleware>();
        return app;
    }
}
