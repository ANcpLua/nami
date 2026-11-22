using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nami.ApiKeys;
using Nami.Collector.Ingestion.Configuration;
using Nami.Hosting;

namespace Nami.UnitTests.Hosting;

/// <summary>
///     Unsecured mode has no credential, so the bind address is its only guard: the engine accepts it on loopback
///     alone, and only when <c>NAMI_OTLP_AUTH_MODE</c> names it.
/// </summary>
[TestClass]
[TestCategory("Fast")]
public class UnsecuredModeTests
{
    private const string Keys = """{"default":[{"key":"synthetic-read-key","roles":["read"]}]}""";

    [TestMethod]
    [DataRow("0.0.0.0", DisplayName = "Every IPv4 interface, as the image binds")]
    [DataRow("::", DisplayName = "Every IPv6 interface")]
    [DataRow("192.0.2.10", DisplayName = "One network interface")]
    public async Task Should_RefuseUnsecuredAndSayWhatToSet_When_TheBindAddressIsNotLoopback(string address)
    {
        var refused = await Assert.ThrowsExactlyAsync<OptionsValidationException>(() => AuthOfAsync(new()
        {
            [CollectorConfigurationKeys.AuthMode] = "Unsecured", [CollectorConfigurationKeys.BindAddress] = address
        }));

        var failure = Assert.ContainsSingle(refused.Failures);
        Assert.Contains($"'{address}'", failure);
        Assert.Contains("NAMI_BIND_ADDRESS=127.0.0.1", failure);
        Assert.Contains("NAMI_OTLP_AUTH_MODE=ApiKey", failure);
        Assert.Contains("NAMI_OTLP_PROJECT_KEYS", failure);
    }

    [TestMethod]
    [DataRow(null, DisplayName = "The default bind address")]
    [DataRow("127.0.0.1", DisplayName = "IPv4 loopback")]
    [DataRow("::1", DisplayName = "IPv6 loopback")]
    public async Task Should_AcceptUnsecured_When_TheBindAddressIsLoopback(string? address)
    {
        var auth = await AuthOfAsync(new()
        {
            [CollectorConfigurationKeys.AuthMode] = "Unsecured", [CollectorConfigurationKeys.BindAddress] = address
        });

        Assert.IsFalse(auth.IsApiKeyMode);
    }

    [TestMethod]
    public async Task Should_StayInApiKeyMode_When_OnlyTheEnvironmentNameIsDevelopment()
    {
        var auth = await AuthOfAsync(new() { [CollectorConfigurationKeys.ProjectKeys] = Keys }, Environments.Development);

        Assert.IsTrue(auth.IsApiKeyMode, "The environment name alone must not turn Unsecured on.");
    }

    [TestMethod]
    public async Task Should_NameBothWaysOut_When_NeitherModeNorKeysAreSet()
    {
        var refused = await Assert.ThrowsExactlyAsync<OptionsValidationException>(() =>
            AuthOfAsync(new(), Environments.Development));

        var failure = Assert.ContainsSingle(refused.Failures);
        Assert.Contains("NAMI_OTLP_PROJECT_KEYS", failure);
        Assert.Contains("NAMI_OTLP_AUTH_MODE=Unsecured", failure);
    }

    [TestMethod]
    public async Task Should_AcceptApiKeyMode_When_TheBindAddressIsEveryInterface()
    {
        var auth = await AuthOfAsync(new()
        {
            [CollectorConfigurationKeys.ProjectKeys] = Keys, [CollectorConfigurationKeys.BindAddress] = "0.0.0.0"
        });

        Assert.IsTrue(auth.IsApiKeyMode);
    }

    /// <summary>The auth options of a collector composed as the engine composes it; it is never started.</summary>
    private static async Task<OtlpApiKeyOptions> AuthOfAsync(Dictionary<string, string?> settings,
        string environment = "Production")
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = environment });
        _ = builder.Logging.ClearProviders();
        settings[CollectorConfigurationKeys.DataPath] = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        _ = builder.Configuration.AddInMemoryCollection(settings);
        var app = builder.BuildNamiCollector(CollectorParts.IngestOnly);
        await using (app.ConfigureAwait(false))
        {
            return app.Services.GetRequiredService<IOptions<OtlpApiKeyOptions>>().Value;
        }
    }
}
