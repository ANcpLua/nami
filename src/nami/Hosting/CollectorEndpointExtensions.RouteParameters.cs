using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Routing;
using Nami.Api.Contracts;
using Nami.Collector.Analysis;

namespace Nami.Collector.Hosting;

internal static partial class CollectorEndpointExtensions
{
    // The path and query parameters the contract bounds, from the generated RouteParameters, by their operation's method
    // and route, in the order the operation declares them.
    private static readonly FrozenDictionary<(string Method, string Route), (string Name, string In, int? MinLength, int? MaxLength, string? Pattern)[]> s_routeParameters =
        RouteParameters.All.GroupBy(static parameter => (parameter.Method, parameter.Route))
            .ToFrozenDictionary(static operation => operation.Key,
                static operation => operation.Select(static parameter => (parameter.Name, parameter.In, parameter.MinLength, parameter.MaxLength, parameter.Pattern)).ToArray());

    // A path or query parameter is held to the length and the pattern the contract gives it before its route runs, as a
    // body member is, so a malformed id is refused naming the parameter instead of answered as a record not found or an
    // empty page. A repeated or absent query parameter is left to the route's own reading.
    private static async ValueTask<object?> CheckRouteParametersAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (http.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: { } route } &&
            s_routeParameters.TryGetValue((http.Request.Method, route), out var parameters))
        {
            foreach (var (name, location, minimum, maximum, pattern) in parameters)
            {
                var value = location == "path"
                    ? http.Request.RouteValues[name] as string
                    : http.Request.Query.TryGetValue(name, out var values) && values.Count == 1 ? values[0] : null;
                if (value is null) continue;
                // QueryReader reports the established date-time problem using the same generated Timestamp grammar.
                if (location == "query" && pattern == Api.Contracts.ContractJson.Schema("Common.Timestamp").GetProperty("pattern").GetString()) continue;
                try
                {
                    var sent = JsonValue.Create(value);
                    if (minimum is not null || maximum is not null) CheckLength(sent, name, name, (minimum, maximum));
                    if (pattern is not null) CheckPattern(sent, name, name, pattern);
                }
                catch (AnalysisInputException invalid)
                {
                    return AnalysisError(invalid);
                }
            }
        }
        return await next(context).ConfigureAwait(false);
    }
}
