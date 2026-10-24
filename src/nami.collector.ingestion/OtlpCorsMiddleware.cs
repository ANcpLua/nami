using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Nami.Collector.Ingestion;

internal sealed class OtlpCorsMiddleware
{
    private readonly bool _allowAll;
    private readonly string _allowedHeadersHeader;
    private readonly HashSet<string> _allowedOrigins;
    private readonly RequestDelegate _next;

    public OtlpCorsMiddleware(RequestDelegate next, IOptions<OtlpCorsOptions> configured)
    {
        var options = configured.Value;
        _next = next;
        _allowedOrigins = options.GetOrigins().ToHashSet(StringComparer.OrdinalIgnoreCase);
        _allowedHeadersHeader = string.Join(", ", options.GetHeaders());
        _allowAll = _allowedOrigins.Contains("*");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        if (!OtlpConstants.IsOtlpPath(path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var origin = context.Request.Headers.Origin.FirstOrDefault();

        if (context.Request.Method == "OPTIONS")
        {
            if (IsOriginAllowed(origin))
            {
                SetCorsHeaders(context.Response, origin);
                context.Response.Headers.AccessControlAllowMethods = "POST, OPTIONS";
                context.Response.Headers.AccessControlMaxAge =
                    OtlpCorsOptions.MaxAge.ToString(CultureInfo.InvariantCulture);
                context.Response.StatusCode = 204;
                return;
            }

            context.Response.StatusCode = 403;
            return;
        }

        if (IsOriginAllowed(origin))
        {
            SetCorsHeaders(context.Response, origin);
        }

        await _next(context).ConfigureAwait(false);
    }

    private bool IsOriginAllowed(string? origin) =>
        !string.IsNullOrEmpty(origin) && (_allowAll || _allowedOrigins.Contains(origin));

    private void SetCorsHeaders(HttpResponse response, string? origin)
    {
        response.Headers.AccessControlAllowOrigin = _allowAll ? "*" : origin;
        response.Headers.AccessControlAllowHeaders = _allowedHeadersHeader;

        if (!_allowAll)
        {
            response.Headers.AccessControlAllowCredentials = "true";
            response.Headers.Append("Vary", "Origin");
        }
    }
}
