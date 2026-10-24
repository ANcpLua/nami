using System.Diagnostics.Metrics;
using System.Net.Sockets;
using System.Text;
using Nami.Hosting;
using Nami.SemanticConventions;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Nami.Telemetry;

internal static partial class NamiTelemetry
{
    public const string ServiceName = "nami";
    public static readonly ActivitySource Source = new(ServiceName, BuildVersion.ProductVersion);
    private static readonly Meter s_meter = new(ServiceName, BuildVersion.ProductVersion);

    public static readonly Histogram<double> Duration = s_meter.CreateHistogram<double>(
        Metrics.McpServerOperationDuration.Name, "s");

    public static void RecordException(Exception error, ILogger logger)
    {
        Activity.Current?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
        {
            [ExceptionAttributes.Type] = error.GetType().FullName,
            [ExceptionAttributes.Message] = error.Message,
            [ExceptionAttributes.Stacktrace] = error.StackTrace
        }));
        ToolFailed(logger, error);
    }

    [LoggerMessage(EventId = 1, EventName = "exception", Level = LogLevel.Error, Message = "Nami tool failed")]
    private static partial void ToolFailed(ILogger logger, Exception exception);
}

internal static class NamiSelfTelemetry
{
    public static void AddNamiSelfTelemetry(this IServiceCollection services,
        IConfiguration configuration)
    {
        var credential = new SelfTelemetryCredential();
        services.AddSingleton(credential);
        var ports = new CollectorPortOptions();
        configuration.Bind(ports);
        if (ports.OtlpHttp <= 0) return;
        var port = ports.OtlpHttp;
        var loopback = ports.ParseBindAddress().AddressFamily == AddressFamily.InterNetworkV6 ? "[::1]" : "127.0.0.1";
        var resource = ResourceBuilder.CreateDefault()
            .AddService(NamiTelemetry.ServiceName, serviceVersion: BuildVersion.ProductVersion)
            .AddAttributes([new KeyValuePair<string, object>(NamiAttributes.ProjectId, NamiTelemetry.ServiceName)]);
        var headers = $"{OtlpConstants.ApiKeyHeaderName}={credential.Key}";
        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.SetResourceBuilder(resource).AddSource(NamiTelemetry.ServiceName)
                .AddOtlpExporter(options => Configure(options, loopback, port, "/v1/traces", headers)))
            .WithMetrics(metrics => metrics.SetResourceBuilder(resource)
                .AddMeter(NamiTelemetry.ServiceName, CollectorSemanticAttributeCatalog.InstrumentationScope)
                .AddOtlpExporter((options, reader) =>
                {
                    Configure(options, loopback, port, "/v1/metrics", headers);
                    reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 1000;
                }));
        services.AddLogging(logging => logging.AddOpenTelemetry(options =>
        {
            options.SetResourceBuilder(resource);
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
            options.AddOtlpExporter(exporter => Configure(exporter, loopback, port, "/v1/logs", headers));
        }));
    }

    private static void Configure(OtlpExporterOptions options, string loopback, int port, string path, string headers)
    {
        options.Protocol = OtlpExportProtocol.HttpProtobuf;
        options.Endpoint = new Uri($"http://{loopback}:{port}{path}");
        options.Headers = headers;
    }
}

/// <summary>Process-local credential for loopback self-export; never persisted or returned to clients.</summary>
internal sealed class SelfTelemetryCredential
{
    public string Key { get; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    public bool Matches(string? supplied)
    {
        return supplied is { Length: 64 } &&
               CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Key),
                   Encoding.ASCII.GetBytes(supplied));
    }
}
