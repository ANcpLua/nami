namespace Nami.IntegrationTests;

[TestClass]
public static class TelemetryLifecycle
{
    internal static HttpClient Http { get; } = new();

    [AssemblyCleanup]
    public static void CloseAssembly()
    {
        Http.Dispose();
    }
}
