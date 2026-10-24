using System;

namespace Nami.Collector.Ingestion.Primitives;

/// <summary>
///     Project identity shared by the auth boundaries, the read API and storage stamping. It sits
///     outside Ingestion and Storage so both transports and the storage mappers apply one rule.
/// </summary>
internal static class ProjectIdentity
{
    public const string DefaultProjectId = "default";

    // Lowercase because gRPC metadata keys are lowercase; HTTP header lookup ignores case.
    public const string HeaderName = "x-nami-project";

    public static string Normalize(string? projectId) =>
        string.IsNullOrWhiteSpace(projectId) ? DefaultProjectId : projectId.Trim();

    // A blank requested project defers to the authenticated one; any other value must match it.
    public static bool Conflicts(string? requestedProjectId, string authenticatedProjectId) =>
        !string.IsNullOrWhiteSpace(requestedProjectId) &&
        !string.Equals(Normalize(requestedProjectId), authenticatedProjectId, StringComparison.Ordinal);
}
