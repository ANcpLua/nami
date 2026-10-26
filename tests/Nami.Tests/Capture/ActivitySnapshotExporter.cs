using Activity = System.Diagnostics.Activity;
using System.Collections.Concurrent;
using JetBrains.Annotations;
using OpenTelemetry;

namespace Nami.Tests.Capture;

/// <summary>A source-filtered exporter for raw producer evidence, without OTLP conversion.</summary>
[PublicAPI]
public sealed class ActivitySnapshotExporter(params string[] sources) : BaseExporter<Activity>
{
    private readonly HashSet<string> _sources = [with(sources, StringComparer.Ordinal)];
    private readonly ConcurrentQueue<ActivitySnapshot> _spans = [];

    /// <summary>Uses the queue's moment-in-time snapshot even while exports enqueue.</summary>
    public ActivitySnapshot[] GetSnapshots()
    {
        return [.. _spans];
    }

    public override ExportResult Export(in Batch<Activity> batch)
    {
        foreach (var activity in batch)
        {
            if (_sources.Contains(activity.Source.Name))
                _spans.Enqueue(ActivitySnapshot.Capture(activity));
        }

        return ExportResult.Success;
    }
}
