using Microsoft.Extensions.Options;
using Nami.Collector.Hosting;

namespace Nami.Collector.Analysis;

internal sealed class AnalysisSigningKey
{
    private readonly byte[] _key;

    public AnalysisSigningKey(IOptions<AnalysisOptions> analysis, IOptions<CollectorStorageOptions> storage)
    {
        if (analysis.Value is { HasSigningKey: true, SigningKey: { } configured })
        {
            _key = Encoding.UTF8.GetBytes(configured);
            return;
        }
        // Local collectors persist an independent random key beside their database.
        // Rotating an ingest/read credential never invalidates signed continuations.
        var database = storage.Value.DataPath;
        if (database == ":memory:") { _key = RandomNumberGenerator.GetBytes(32); return; }
        var path = Path.GetFullPath(database) + ".analysis-signing.key";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using var file = new FileStream(path, options);
            file.Write(RandomNumberGenerator.GetBytes(32));
        }
        catch (IOException) when (File.Exists(path)) { _key = File.ReadAllBytes(path); }
        _key = File.ReadAllBytes(path);
        if (_key.Length < 32) throw new InvalidOperationException("The stored analysis signing key is invalid.");
    }

    public AnalysisHandles ForProject(string project) => new(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes("nami-analysis-v1\0" + project)));
}
