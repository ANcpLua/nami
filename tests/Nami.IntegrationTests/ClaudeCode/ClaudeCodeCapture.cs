using System.Security.Cryptography;
using System.Text.Json;
using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests.ClaudeCode;

/// <summary>One OTLP/HTTP export body as Claude Code sent it: the signal route and the protobuf bytes.</summary>
internal sealed record ClaudeCodeExport(string Signal, byte[] Payload);

/// <summary>
///     A scrubbed capture of Claude Code 2.1.331's OTLP/HTTP export in
///     <c>tests/Nami.IntegrationTests/Fixtures/ClaudeCodeOtlp</c> (see its README): the protobuf bodies in
///     arrival order, as <c>manifest.json</c> lists them, each checked against the manifest's SHA-256.
/// </summary>
internal static class ClaudeCodeCapture
{
    /// <summary>Three signed-in sessions on the observer's Mac: 14 logs, 10 metrics and 11 traces bodies.</summary>
    internal const string SignedIn = "signed-in";

    /// <summary>Two sessions without a credential, which never reach the API: 6 bodies.</summary>
    internal const string NotSignedIn = "not-signed-in";

    internal static IReadOnlyList<ClaudeCodeExport> Load(string capture)
    {
        var directory = Path.Combine(ProcessRunner.RepositoryRoot, "tests", "Nami.IntegrationTests", "Fixtures",
            "ClaudeCodeOtlp", capture);
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "manifest.json")));
        var exports = new List<ClaudeCodeExport>();
        foreach (var entry in manifest.RootElement.GetProperty("captures").EnumerateArray())
        {
            var file = entry.GetProperty("file").GetString()!;
            var payload = File.ReadAllBytes(Path.Combine(directory, file));
            if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(payload)), entry.GetProperty("sha256").GetString(),
                    StringComparison.Ordinal))
                throw new InvalidDataException($"{capture}/{file} does not match the SHA-256 in its manifest.");
            exports.Add(new ClaudeCodeExport(entry.GetProperty("signal").GetString()!, payload));
        }

        return exports;
    }
}
