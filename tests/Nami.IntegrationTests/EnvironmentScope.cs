namespace Nami.IntegrationTests;

/// <summary>Sets process environment variables and restores their previous values on disposal.</summary>
internal sealed class EnvironmentScope : IDisposable
{
    private readonly (string Name, string? Previous)[] _previous;

    public EnvironmentScope(params (string Name, string? Value)[] settings)
    {
        _previous = [.. settings.Select(setting => (setting.Name, Environment.GetEnvironmentVariable(setting.Name)))];
        foreach (var (name, value) in settings) Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose()
    {
        foreach (var (name, previous) in _previous) Environment.SetEnvironmentVariable(name, previous);
    }
}
