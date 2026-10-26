using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Nami.Tests;

/// <summary>Identifies named build inputs or every file of an installed runtime by its actual bytes.</summary>
internal static class ArtifactFingerprint
{
    internal static string Files(IEnumerable<string> paths, string recipe = "")
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, Encoding.UTF8.GetBytes(recipe));
        foreach (var path in paths.Order(StringComparer.Ordinal))
        {
            Append(hash, Encoding.UTF8.GetBytes(Path.GetFullPath(path)));
            Append(hash, File.Exists(path) ? File.ReadAllBytes(path) : []);
            Append(hash, [File.Exists(path) ? (byte)1 : (byte)0]);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, ReadOnlySpan<byte> bytes)
    {
        Span<byte> length = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
