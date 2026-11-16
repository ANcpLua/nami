using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nami.Collector.Ingestion.Configuration;
using Nami.Hosting;
using Nami.Tests.Infrastructure;
using Nami.Tools;

namespace Nami.IntegrationTests;

/// <summary>
///     <c>Program.cs</c> and the collectors the tests start (<see cref="CollectorHost" /> and
///     <see cref="ClaudeCode.CollectorHost" />) are built by <see cref="CollectorComposition.BuildNamiCollector" />, so
///     a step added to that method reaches all three. Two ways to leave a test collector behind remain, and each has
///     a test here: a step put under <see cref="CollectorParts.Engine" /> alone, and a step written into
///     <c>Program.cs</c> beside the method.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class CollectorCompositionTests(TestContext context)
{
    private static readonly string[] s_productAssemblies = ["nami", "nami.collector.ingestion", "nami.collector.storage"];

    [TestMethod]
    public async Task IngestOnly_LacksNoProductServiceButTheToolService()
    {
        var engine = await ComposeAsync(CollectorParts.Engine);
        var ingestOnly = await ComposeAsync(CollectorParts.IngestOnly);

        Assert.AreSequenceEqual(Array.Empty<string>(), Missing(from: engine.Services, ingestOnly.Services),
            "A test collector must not have a service the engine lacks.");
        Assert.AreSequenceEqual([typeof(NamiToolService).FullName],
            Missing(from: ingestOnly.Services, engine.Services, productOnly: true),
            "Only the tool service may be the engine's alone; the export of nami's own telemetry registers " +
            "OpenTelemetry's services, none of the product's. A new step belongs to every collector, or it is named here.");
    }

    [TestMethod]
    public async Task IngestOnly_ServesTheEndpointsOfTheEngine()
    {
        var engine = await ComposeAsync(CollectorParts.Engine);
        var ingestOnly = await ComposeAsync(CollectorParts.IngestOnly);

        Assert.IsNotEmpty(engine.Endpoints);
        Assert.AreSequenceEqual(engine.Endpoints, ingestOnly.Endpoints);
    }

    [TestMethod]
    public async Task Program_ComposesTheCollectorThroughTheOneMethodOnly()
    {
        var program = await File.ReadAllTextAsync(
            Path.Combine(ProcessRunner.RepositoryRoot, "src", "nami", "Program.cs"), context.CancellationToken);

        Assert.Contains("builder.BuildNamiCollector()", program);
        // A registration, a middleware or an endpoint written here would be missing from every test collector.
        foreach (var beside in (string[])["builder.Services", "app.Use", "app.Map"])
            Assert.DoesNotContain(beside, program);
    }

    /// <summary>The service registrations and the endpoints of a collector composed with <paramref name="parts" />; it is never started.</summary>
    private async Task<(string[] Services, string[] Endpoints)> ComposeAsync(CollectorParts parts)
    {
        var directory = context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");
        var builder = LoopbackHost.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            // A port above zero, so that the engine's parts include the export of nami's own telemetry.
            [CollectorConfigurationKeys.OtlpPort] = 4318.ToString(CultureInfo.InvariantCulture),
            [CollectorConfigurationKeys.GrpcPort] = "0",
            [CollectorConfigurationKeys.DataPath] = Path.Combine(directory, parts + ".duckdb"),
            [CollectorConfigurationKeys.AuthMode] = "Unsecured"
        });
        string[] services = [];
        var application = builder.BuildNamiCollector(parts,
            registered => services = [.. registered.Select(Describe).Order(StringComparer.Ordinal)]);
        await using (application.ConfigureAwait(false))
        {
            return (services,
            [
                .. ((IEndpointRouteBuilder)application).DataSources.SelectMany(static source => source.Endpoints)
                .Select(static endpoint => endpoint.DisplayName ?? "").Order(StringComparer.Ordinal)
            ]);
        }
    }

    /// <summary>The registrations of <paramref name="others" /> that <paramref name="from" /> does not have, each as often as it is missing.</summary>
    private static string[] Missing(string[] from, string[] others, bool productOnly = false)
    {
        var left = from.ToList();
        return [.. others.Where(other => !left.Remove(other))
            .Where(other => !productOnly || other.StartsWith("product ", StringComparison.Ordinal))
            .Select(static other => other[(other.IndexOf(' ', StringComparison.Ordinal) + 1)..])];
    }

    /// <summary>
    ///     "product " or "other ", then the service type; a registration is the product's when its service type,
    ///     its implementation or one of their type arguments comes from a product assembly.
    /// </summary>
    private static string Describe(ServiceDescriptor descriptor)
    {
        var implementation = descriptor.IsKeyedService
            ? descriptor.KeyedImplementationType ?? descriptor.KeyedImplementationInstance?.GetType()
            : descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType();
        var product = OfProduct(descriptor.ServiceType) || (implementation is not null && OfProduct(implementation));
        return (product ? "product " : "other ") + descriptor.ServiceType.FullName;
    }

    private static bool OfProduct(Type type)
    {
        return s_productAssemblies.Contains(type.Assembly.GetName().Name, StringComparer.Ordinal) ||
               type.GetGenericArguments().Any(OfProduct);
    }
}
