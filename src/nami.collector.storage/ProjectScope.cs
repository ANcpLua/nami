using Nami.Collector.Ingestion.Primitives;

namespace Nami.Collector.Storage;

internal static class ProjectScope
{
    public const string DefaultProjectId = ProjectIdentity.DefaultProjectId;

    public static string ForIngest(string? projectIdHint, string? authenticatedProjectId)
    {
        if (authenticatedProjectId is null) return ProjectIdentity.Normalize(projectIdHint);
        if (ProjectIdentity.Conflicts(projectIdHint, authenticatedProjectId))
            throw new InvalidDataException("OTLP project does not match the authenticated project.");

        return authenticatedProjectId;
    }
}
