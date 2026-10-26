namespace Nami.Tests.Policies;

/// <summary>Keeps the evidence text of a policy failure and returns the location the failure message cites.</summary>
internal interface IEvidenceStore
{
    string Save(string text);
}

/// <summary>Writes each evidence text to its own file under the system temp directory.</summary>
internal sealed class TempFileEvidenceStore : IEvidenceStore
{
    public static TempFileEvidenceStore Instance { get; } = new();

    public string Save(string text)
    {
        var directory = Path.Combine(Path.GetTempPath(), "nami-telemetry-evidence", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "telemetry-evidence.txt");
        File.WriteAllText(path, text);
        return path;
    }
}
