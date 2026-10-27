using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Nami.ApiKeys;
using Nami.Collector.Ingestion;
using Nami.Hosting;
using Nami.Retention;

namespace Nami.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
public sealed class ConfigurationModelsTests
{
    [TestMethod]
    [DataRow("{\"p\":[{\"roles\":[\"read\"]}]}", DisplayName = "Missing credential key")]
    [DataRow("{\"p\":[{\"key\":\"secret-sample\"}]}", DisplayName = "Missing roles")]
    [DataRow("{\"p\":[{\"key\":null,\"roles\":[\"read\"]}]}", DisplayName = "Null key")]
    [DataRow("{\"p\":[{\"key\":\"secret-sample\",\"roles\":null}]}", DisplayName = "Null roles")]
    [DataRow("{\"p\":[{\"key\":\"secret-sample\",\"roles\":[null]}]}", DisplayName = "Null role element")]
    [DataRow("{\"p\":[{\"key\":\"secret-sample\",\"roles\":[\"read\"],\"extra\":true}]}", DisplayName = "Unknown credential member")]
    [DataRow("{\"p\":[{\"key\":\"secret-sample\",\"key\":\"replacement\",\"roles\":[\"read\"]}]}", DisplayName = "Duplicate credential member")]
    [DataRow("{\"p\":[{\"key\":\"secret-sample\",\"roles\":[\"read\"]}],\"p\":[{\"key\":\"replacement\",\"roles\":[\"ingest\"]}]}", DisplayName = "Duplicate project member")]
    [DataRow("{\"p\":[{\"key\":\"secret-sample\",\"roles\":[\"read\",\"read\"]}]}", DisplayName = "Duplicate role")]
    [DataRow("{\"p\":[{\"key\":\"secret-sample\",\"roles\":[]}]}", DisplayName = "Empty role set")]
    [DataRow("{\"p\":[null]}", DisplayName = "Null credential element")]
    [DataRow("{\"p\":null}", DisplayName = "Null project credentials")]
    [DataRow("{\"p\":[]}", DisplayName = "Empty project credentials")]
    [DataRow("{\"p\":[{\"key\":\"secret-sample\",\"roles\":[\"read\"]}],\"q\":[{\"key\":\"secret-sample\",\"roles\":[\"ingest\"]}]}", DisplayName = "Credential belongs to two projects")]
    public void CredentialJson_InvalidStructureOrIdentity_FailsWithoutExposingTheKey(string json)
    {
        Assert.IsFalse(ProjectKeysParser.TryParse(json, out var keys, out var error));
        Assert.IsEmpty(keys);
        Assert.IsNotNull(error);
        Assert.Contains("NAMI_OTLP_PROJECT_KEYS", error);
        Assert.DoesNotContain("secret-sample", error);
    }

    [TestMethod]
    public void CredentialJson_RotatingKeysAndCombinedRoles_RetainProjectAndPermissions()
    {
        const string Json = "{\"p\":[{\"key\":\"old\",\"roles\":[\"read\",\"ingest\"]},{\"key\":\"new\",\"roles\":[\"ingest\"]}]}";
        Assert.IsTrue(ProjectKeysParser.TryParse(Json, out var keys, out var error));
        Assert.IsNull(error);
        Assert.AreSequenceEqual([new ProjectApiKey("p", "old", ApiKeyRoles.Read | ApiKeyRoles.Ingest),
            new ProjectApiKey("p", "new", ApiKeyRoles.Ingest)], keys);
    }

    [TestMethod]
    public void GeneratedOptions_DefaultsAndEnvironmentNames_ReachTheirConsumers()
    {
        var ports = new CollectorPortOptions();
        Assert.AreEqual("127.0.0.1", ports.BindAddress);
        Assert.AreEqual(4318, ports.OtlpHttp);
        Assert.AreEqual(4317, ports.Grpc);
        Assert.AreEqual("nami.duckdb", new CollectorStorageOptions().DataPath);
        var retention = new RetentionOptions();
        Assert.AreEqual(30, retention.Days);
        Assert.AreEqual(TimeSpan.FromHours(1), retention.Interval);
        Assert.AreEqual(2_147_483_648L, retention.StorageMinimumFreeBytes);
        Assert.IsFalse(new OtlpApiKeyOptions().IsApiKeyMode);

        using var configuration = new ConfigurationRoot([new MemoryConfigurationProvider(new MemoryConfigurationSource
        { InitialData = new Dictionary<string, string?>
        {
            ["NAMI_OTLP_PORT"] = "1234", ["NAMI_GRPC_PORT"] = "2345", ["NAMI_BIND_ADDRESS"] = "::1",
            ["NAMI_DATA_PATH"] = "chosen.duckdb", ["NAMI_DB_THREADS"] = "3",
            ["NAMI_RETENTION_DAYS"] = "0", ["NAMI_OTLP_AUTH_MODE"] = "ApiKey",
            ["NAMI_OTLP_CORS_ALLOWED_ORIGINS"] = "https://example.test"
        } })]);
        configuration.Bind(ports);
        var storage = new CollectorStorageOptions();
        configuration.Bind(storage);
        configuration.Bind(retention);
        var auth = new OtlpApiKeyOptions();
        configuration.Bind(auth);
        var cors = new OtlpCorsOptions();
        configuration.Bind(cors);
        Assert.AreEqual(1234, ports.OtlpHttp);
        Assert.AreEqual(2345, ports.Grpc);
        Assert.AreEqual(System.Net.IPAddress.IPv6Loopback, ports.ParseBindAddress());
        Assert.AreEqual("chosen.duckdb", storage.DataPath);
        Assert.AreEqual(3, storage.Threads);
        Assert.IsFalse(retention.IsEnabled);
        Assert.IsTrue(auth.IsApiKeyMode);
        Assert.AreSequenceEqual(["https://example.test"], cors.GetOrigins().ToArray());
    }

    [TestMethod]
    [DataRow(0, true)]
    [DataRow(65535, true)]
    [DataRow(-1, false)]
    [DataRow(65536, false)]
    public void GeneratedPortBounds_ValidateInclusiveLimits(int port, bool valid)
    {
        var result = new CollectorPortOptionsValidator().Validate(null, new CollectorPortOptions { OtlpHttp = port });
        Assert.AreEqual(valid, result.Succeeded);
        if (!valid) Assert.Contains("NAMI_OTLP_PORT", Assert.ContainsSingle(result.Failures!));
    }

    [TestMethod]
    [DataRow(8796093022207L, true)]
    [DataRow(8796093022208L, false)]
    [DataRow(-1L, false)]
    public void GeneratedStorageBudgetBounds_PreventByteOverflow(long megabytes, bool valid)
    {
        var options = new RetentionOptions { StorageMinimumFreeMegabytes = megabytes };
        var result = new RetentionOptionsValidator().Validate(null, options);
        Assert.AreEqual(valid, result.Succeeded);
        if (!valid) Assert.Contains("NAMI_STORAGE_MIN_FREE_MB", Assert.ContainsSingle(result.Failures!));
        else Assert.AreEqual(9_223_372_036_853_727_232L, options.StorageMinimumFreeBytes);
    }

    [TestMethod]
    [DataRow("ingest", true)]
    [DataRow("read", true)]
    [DataRow("write", false)]
    [DataRow("delete", false)]
    [DataRow("none", false)]
    public void GeneratedCredentialRoles_OnlyEnableCurrentProductCapabilities(string role, bool valid)
    {
        const string Credential = "private-test-credential";
        var json = "{\"project\":[{\"key\":\"" + Credential + "\",\"roles\":[\"" + role + "\"]}]}";
        Assert.AreEqual(valid, ProjectKeysParser.TryParse(json, out var keys, out var error));
        if (valid)
        {
            var key = Assert.ContainsSingle(keys);
            Assert.AreEqual(role == "ingest" ? ApiKeyRoles.Ingest : ApiKeyRoles.Read, key.Roles);
            Assert.AreEqual("project", key.ProjectId);
            Assert.AreEqual(Credential, key.ApiKey);
            Assert.IsNull(error);
        }
        else
        {
            Assert.IsEmpty(keys);
            Assert.IsNotNull(error);
            Assert.DoesNotContain(Credential, error);
        }
    }
}
