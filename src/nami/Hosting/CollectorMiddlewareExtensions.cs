using Microsoft.Extensions.Options;
using Nami.ApiKeys;

namespace Nami.Hosting;

internal static class CollectorMiddlewareExtensions
{
    public static void UseNamiCollectorMiddleware(this WebApplication app)
    {
        // First: a request under a foreign Host or Origin is answered before CORS, the key or the gate sees it.
        app.UseMiddleware<OtlpRequestNamesMiddleware>();
        if (app.Services.GetRequiredService<IOptions<OtlpCorsOptions>>().Value.IsEnabled)
            app.UseMiddleware<OtlpCorsMiddleware>();
        app.UseMiddleware<CollectorApiKeyMiddleware>();
        // After the credential check, so a request without a valid key takes no slot; before any body is read.
        app.UseMiddleware<OtlpIngestGateMiddleware>();
    }
}
