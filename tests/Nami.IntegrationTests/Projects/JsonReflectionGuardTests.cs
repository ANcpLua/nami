using System.Text.Json;

namespace Nami.IntegrationTests.Projects;

[TestClass]
public sealed class JsonReflectionGuardTests(TestContext context)
{
    [TestMethod]
    public void Runtime_IntegrationHost_DisablesDefaultJsonReflection()
    {
        Assert.IsFalse(JsonSerializer.IsReflectionEnabledByDefault);
    }

    [TestMethod]
    public void Serialize_UnregisteredType_RejectsTheReflectionFallback()
    {
        _ = Assert.ThrowsExactly<InvalidOperationException>(() => JsonSerializer.Serialize(new UnregisteredPayload()));
    }

    [TestMethod]
    [DataRow("process-host/Nami.ProcessHost.runtimeconfig.json")]
    [DataRow("fixtures/InvocationProbe/InvocationProbe.runtimeconfig.json")]
    public async Task RuntimeConfiguration_GeneratedExecutable_DisablesDefaultJsonReflection(string relativePath)
    {
        using var json =
            JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, relativePath),
                context.CancellationToken));

        Assert.IsFalse(json.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties")
            .GetProperty("System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault").GetBoolean());
    }

    [TestMethod]
    public async Task BuildAsync_GeneratedConsumer_UsesMetadataWithReflectionDisabled()
    {
        await using var project = new FixtureProject("JsonGuardConsumer", context: context);
        project.RecordProperties("JsonSerializerIsReflectionEnabledByDefault")
            .AddSource("Program.cs", """
                                     using System.Text.Json;
                                     using System.Text.Json.Serialization;
                                     Console.Write(JsonSerializer.IsReflectionEnabledByDefault + "|");
                                     Console.Write(JsonSerializer.Serialize(new Payload(42), ConsumerJsonContext.Default.Payload));
                                     internal sealed record Payload(int Value);
                                     [JsonSerializable(typeof(Payload))]
                                     internal partial class ConsumerJsonContext : JsonSerializerContext;
                                     """);

        var build = await project.BuildAsync(context.CancellationToken);
        Assert.AreEqual(0, build.ExitCode, build.Output + build.Error);
        var start = new ProcessStartInfo("dotnet");
        start.ArgumentList.Add(build.AssemblyPath);
        var run = await ChildProcess.RunAsync(start, "the JSON guard consumer", context.CancellationToken);

        Assert.AreEqual(0, run.ExitCode, run.Error);
        Assert.AreEqual("false", build.GetRecordedProperty("JsonSerializerIsReflectionEnabledByDefault"));
        Assert.AreEqual("False|{\"Value\":42}", run.Output);
    }

    private sealed class UnregisteredPayload;
}
