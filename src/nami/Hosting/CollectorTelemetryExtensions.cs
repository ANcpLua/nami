using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nami.Collector.Telemetry;

namespace Nami.Collector.Hosting;

internal static class CollectorTelemetryExtensions
{
    public static IServiceCollection AddNamiCollectorTelemetry(
        this IServiceCollection services,
        IHostEnvironment environment)
    {
        services.AddNamiTelemetry();
        services.AddLogging(logging => logging.AddNamiLogging(environment));

        var healthBuilder = services.AddHealthChecks()
            .AddApplicationLifecycleHealthCheck(NamiEndpoints.LiveTag);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            healthBuilder.AddResourceUtilizationHealthCheck(static options =>
            {
                options.CpuThresholds = new ResourceUsageThresholds
                {
                    DegradedUtilizationPercentage = 80, UnhealthyUtilizationPercentage = 95
                };
                options.MemoryThresholds = new ResourceUsageThresholds
                {
                    DegradedUtilizationPercentage = 85, UnhealthyUtilizationPercentage = 95
                };
            }, "resources", NamiEndpoints.LiveTag);

            services.AddResourceMonitoring();
        }

        services.AddTelemetryHealthCheckPublisher();

        return services;
    }
}
