namespace Nami.Collector.Ingestion.Primitives;

/// <summary>
/// The one definition of an active session, used by GET /api/v1/sessions and the analysis list_sessions:
/// its last span ended less than <see cref="ActiveWindow"/> ago, as the list_sessions contract documents.
/// </summary>
internal static class SessionActivity
{
    public static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(5);

    // The first nanosecond of last activity that is still active: activity exactly ActiveWindow
    // before now has ended. Keep this cutoff exact through SQL and mapping, which compare with >=.
    public static ulong GetActiveCutoffUnixNano(DateTimeOffset now) =>
        TimeConversions.ToUnixNanoUnsigned(now - ActiveWindow) + 1;
}
