using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using JetBrains.Annotations;
using NuGet.Versioning;

namespace Nami.Tests;

/// <summary>An owned local NuGet feed. A package identity is published once for one immutable set of inputs.</summary>
[PublicAPI]
public sealed class PackageFeedFixture : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    private ImmutableDictionary<string, Publication> _published =
        ImmutableDictionary.Create<string, Publication>(StringComparer.OrdinalIgnoreCase);

    public string DirectoryPath { get; } =
        Path.Combine(Environment.GetEnvironmentVariable("NAMI_FIXTURE_WORK_ROOT")
                     ?? Path.GetTempPath(), "nami-package-feed-" + Guid.NewGuid().ToString("N"));

    public string CacheDirectory => Path.Combine(DirectoryPath, ".cache", "packages");

    internal IEnumerable<string> PackageIds => _published.Values.Select(publication => publication.Package.Id)
        .Distinct(StringComparer.OrdinalIgnoreCase);

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
            _disposed = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FixturePackage> PackAsync(FixtureProject producer, string id, string version,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(producer);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAvailable();
        if (id.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '_' and not '-'))
            throw new ArgumentException("Package identity must be safe for a NuGet file name.", nameof(id));
        if (!NuGetVersion.TryParse(version, out var parsedVersion))
            throw new ArgumentException("A valid NuGet package version is required.", nameof(version));
        version = parsedVersion.ToNormalizedString();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureAvailable();
            producer.WithProperty("IsPackable", "true").WithProperty("PackageId", id)
                .WithProperty("Version", version).WithProperty("PackageVersion", version);
            var fingerprint = Fingerprint(producer.Definition);
            var key = id + "/" + version;
            if (_published.TryGetValue(key, out var existing))
            {
                if (existing.InputFingerprint != fingerprint)
                {
                    throw new InvalidOperationException(
                        "The package identity already belongs to different fixture inputs: " + key);
                }

                existing.Package.Verify();
                return existing.Package;
            }

            var build = await producer.PackAsync(cancellationToken).ConfigureAwait(false);
            if (!build.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Packing {key} failed with exit code {build.ExitCode}.\n{build.Output}\n{build.Error}");
            }

            var archivePath = Directory.GetFiles(build.OutputDirectory, "*.nupkg", SearchOption.TopDirectoryOnly)
                .Single();
            VerifyIdentity(archivePath, id, version);
            Directory.CreateDirectory(DirectoryPath);
            var target = Path.Combine(DirectoryPath, id + "." + version + ".nupkg");
            var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.Copy(archivePath, temporary);
            File.Move(temporary, target, true);
            var package = new FixturePackage(id, version, target, Hash(target));
            _published = _published.Add(key, new Publication(fingerprint, package));
            return package;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal void EnsureAvailable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static string Fingerprint(FixtureProjectDefinition definition)
    {
        var text = new StringBuilder().Append(definition.ToProjectDocument()).Append(definition.ToPackageDocument());
        foreach (var (path, source) in definition.Sources.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            text.Append(path.Length).Append(':').Append(path).Append(source.Length).Append(':').Append(source);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static void VerifyIdentity(string path, string id, string version)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry =
            archive.Entries.Single(item => item.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        var metadata = document.Root!.Elements().Single(element => element.Name.LocalName == "metadata");
        var actualId = metadata.Elements().Single(element => element.Name.LocalName == "id").Value;
        var actualVersion = metadata.Elements().Single(element => element.Name.LocalName == "version").Value;
        if (!string.Equals(actualId, id, StringComparison.OrdinalIgnoreCase) || actualVersion != version)
            throw new InvalidDataException($"Expected package {id}/{version}, found {actualId}/{actualVersion}.");
    }

    internal static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private sealed record Publication(string InputFingerprint, FixturePackage Package);
}

[PublicAPI]
public sealed record FixturePackage(string Id, string Version, string Path, string Sha256)
{
    public void Verify()
    {
        if (PackageFeedFixture.Hash(Path) != Sha256)
            throw new InvalidDataException("The fixture package changed after publication: " + Path);
    }
}
