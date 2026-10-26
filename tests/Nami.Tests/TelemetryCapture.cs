using JetBrains.Annotations;
using Nami.Tests.Policies;

namespace Nami.Tests;

[PublicAPI]
public static class TelemetryCapture
{
    public static Task<TraceEvidence> TraceAsync(Func<CaptureContext, Task> action, Func<TraceEvidence, bool> until,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return TraceAsync(action, until, OtlpReceiver.DefaultMaxPayloadBytes, timeout, cancellationToken);
    }

    public static async Task<TraceEvidence> TraceAsync(Func<CaptureContext, Task> action,
        Func<TraceEvidence, bool> until,
        int maxPayloadBytes, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(until);
        await using var receiver =
            await OtlpReceiver.StartAsync(maxPayloadBytes, cancellationToken).ConfigureAwait(false);
        using var ownedRoot = Activity.Current?.OperationName == TelemetryActivity.Name
            ? null
            : TelemetryActivity.Start();
        var root = Activity.Current!;
        var capture = new CaptureContext(receiver, root.TraceId.ToHexString(), root.Id!);
        await action(capture).ConfigureAwait(false);
        var snapshot = await receiver
            .AwaitAsync(s => until(capture.Read(s)), timeout ?? OtlpReceiver.DefaultEvidenceTimeout, cancellationToken)
            .ConfigureAwait(false);
        var evidence = capture.Read(snapshot);
        TelemetryInvocation.Publish(new PublishedTrace(evidence, root.SpanId.ToHexString()));
        return evidence;
    }
}

[PublicAPI]
public sealed class CaptureContext
{
    private readonly OtlpReceiver _receiver;

    internal CaptureContext(OtlpReceiver receiver, string traceId, string traceParent)
    {
        _receiver = receiver;
        TraceId = traceId;
        TraceParent = traceParent;
        Partition = new MetricPartition(MetricPartition.TestIdAttribute, traceId);
    }

    public Uri Endpoint => _receiver.Endpoint;
    public Uri GrpcEndpoint => _receiver.GrpcEndpoint;
    public string TraceId { get; }
    public string TraceParent { get; }
    public MetricPartition Partition { get; }

    public TraceEvidence Read()
    {
        return Read(_receiver.Snapshot());
    }

    internal TraceEvidence Read(TelemetrySnapshot snapshot)
    {
        return new TraceEvidence(TraceId, snapshot, Partition);
    }

    public Task<TelemetrySnapshot> AwaitAsync(Func<TelemetrySnapshot, bool> until, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return _receiver.AwaitAsync(until, timeout, cancellationToken);
    }
}

internal static class TelemetryActivity
{
    internal const string Name = TracePerTestAttribute.ActivityName;

    internal static Activity Start()
    {
        var activity = new Activity(Name);
        try
        {
            activity.SetIdFormat(ActivityIdFormat.W3C)
                .SetParentId(ActivityTraceId.CreateRandom(), default, ActivityTraceFlags.Recorded).Start();
            var result = activity;
            activity = null;
            return result;
        }
        finally
        {
            activity?.Dispose();
        }
    }
}
