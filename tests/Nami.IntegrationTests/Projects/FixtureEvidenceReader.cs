using System.IO.Compression;
using Microsoft.Build.Logging;

namespace Nami.IntegrationTests.Projects;

internal static class FixtureEvidenceReader
{
    internal static (IReadOnlyDictionary<string, string> Files, IReadOnlyList<string?> ErrorCodes) ReadBinlog(
        string path,
        CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var errors = new List<string?>();
        var replay = new BinaryLogReplayEventSource();
        ((IBuildEventArgsReaderNotifications)replay).ArchiveFileEncountered += args =>
        {
            var file = args.ArchiveData.ToArchiveFile();
            files[file.FullPath.Replace('\\', '/')] = file.Content;
        };
        replay.ErrorRaised += (_, error) => errors.Add(error.Code);
        replay.Replay(path, cancellationToken);
        return (files, errors);
    }

    internal static string ReadInput(string archivePath, string entryName)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.GetEntry(entryName) ??
                    throw new InvalidDataException("Missing fixture input: " + entryName);
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}
