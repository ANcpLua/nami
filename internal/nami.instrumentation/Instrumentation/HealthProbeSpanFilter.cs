using OpenTelemetry;

namespace Nami.Instrumentation.Instrumentation;

/// <summary>
/// Drops server spans for the liveness and readiness probes.
/// </summary>
/// <remarks>
/// The hosting platform polls these endpoints continuously, so at steady state their spans would
/// dominate everything this process reports about itself while describing only the prober. Clearing
/// the recorded flag drops the span at export without suppressing the <see cref="Activity"/>, so
/// in-process code reading <see cref="Activity.Current"/> still sees a live span and the health
/// request keeps a trace context to propagate.
/// <para>
/// This is deliberately not a producer-side concern: <c>Nami.Telemetry.Hosting</c> composes the pipeline but
/// cannot know which of a given application's endpoints are noise.
/// </para>
/// </remarks>
internal sealed class HealthProbeSpanFilter : BaseProcessor<Activity>
{
    public override void OnEnd(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        if (activity.Kind is not ActivityKind.Server)
            return;

        if (activity.GetTagItem("url.path") is string path &&
            (string.Equals(path, NamiEndpoints.Health, StringComparison.Ordinal) ||
             string.Equals(path, NamiEndpoints.Alive, StringComparison.Ordinal)))
        {
            activity.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
        }
    }
}
