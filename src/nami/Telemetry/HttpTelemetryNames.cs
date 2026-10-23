namespace Nami.Collector.Telemetry;

internal static class HttpTelemetryNames
{
    private const string OtherMethod = "OTHER";

    public static string NormalizeMethod(string? method)
    {
        if (string.IsNullOrWhiteSpace(method))
            return OtherMethod;

        return method.ToUpperInvariant() switch
        {
            "GET" => HttpMethods.Get,
            "POST" => HttpMethods.Post,
            "PUT" => HttpMethods.Put,
            "PATCH" => HttpMethods.Patch,
            "DELETE" => HttpMethods.Delete,
            "HEAD" => HttpMethods.Head,
            "OPTIONS" => HttpMethods.Options,
            "TRACE" => HttpMethods.Trace,
            "CONNECT" => HttpMethods.Connect,
            _ => OtherMethod
        };
    }
}
