using System.Text.Json.Nodes;
using Grpc.Core;
using Nami.Collector.Ingestion;
using Nami.Collector.Ingestion.Primitives;
using Nami.Tests.Infrastructure;

namespace Nami.IntegrationTests;

/// <summary>
///     The API-key boundary of OTLP over gRPC, the twin of the HTTP cases in HostTransportTests
///     (Http_KeyAndResourceConflict_Refused, Http_KeyRolesAndCrossProjectReads_AreEnforced). The collector runs with
///     the composition of Program.cs, and each case is a real gRPC call against its listener.
/// </summary>
[TestClass]
[TestCategory("Integration")]
[DependsOn(typeof(IngestStoreTests), nameof(IngestStoreTests.Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
public sealed class GrpcIngestAuthTests(TestContext context)
{
    private const string Keys =
        "{\"a\":[{\"key\":\"ingest-a\",\"roles\":[\"ingest\"]},{\"key\":\"read-a\",\"roles\":[\"read\"]}]," +
        "\"b\":[{\"key\":\"ingest-b\",\"roles\":[\"ingest\"]}]}";

    private static readonly string[] s_projects = ["a", "b", ProjectIdentity.DefaultProjectId];

    [TestMethod]
    [Timeout(60000, CooperativeCancellation = true)]
    [DataRow(Signals.Traces, DisplayName = "gRPC trace export")]
    [DataRow(Signals.Logs, DisplayName = "gRPC logs export")]
    [DataRow(Signals.Metrics, DisplayName = "gRPC metrics export")]
    public async Task Grpc_KeyRolesAndResourceConflict_AreEnforced(string signal)
    {
        var path = Path.Combine(
            context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"),
            "grpc-auth.duckdb");
        await using var host = await CollectorHost.StartAsync(path, context.CancellationToken, Keys);
        var own = CollectorHost.Encode(signal, Payload(signal, "a"));
        var foreign = CollectorHost.Encode(signal, Payload(signal, "b"));

        Assert.AreEqual(StatusCode.Unauthenticated, await RefusalAsync(host, signal, own, null));
        Assert.AreEqual(StatusCode.Unauthenticated, await RefusalAsync(host, signal, own, Key("ingest-c")));
        Assert.AreEqual(StatusCode.PermissionDenied, await RefusalAsync(host, signal, own, Key("read-a")));
        // The resource names project b; the key belongs to project a.
        Assert.AreEqual(StatusCode.InvalidArgument, await RefusalAsync(host, signal, foreign, Key("ingest-a")));
        // Call metadata cannot choose a project either.
        var redirected = Key("ingest-a");
        redirected.Add(ProjectIdentity.HeaderName, "b");
        Assert.AreEqual(StatusCode.InvalidArgument, await RefusalAsync(host, signal, own, redirected));
        foreach (var project in s_projects)
            Assert.AreEqual(0, await RecordedAsync(host, project), "A refused export left rows in project " + project);

        _ = await host.ExportGrpcAsync(signal, own, context.CancellationToken, Key("ingest-a"));
        // A resource that names no project is stored in the project of the key.
        _ = await host.ExportGrpcAsync(signal, CollectorHost.Encode(signal, WithoutProject(Payload(signal, "a", 1))),
            context.CancellationToken, Key("ingest-a"));

        Assert.AreEqual(2, await RowsAsync(host, signal, "a"));
        Assert.AreEqual(0, await RecordedAsync(host, "b"));
        Assert.AreEqual(0, await RecordedAsync(host, ProjectIdentity.DefaultProjectId));
    }

    private static Metadata Key(string value)
    {
        return new Metadata { { OtlpConstants.ApiKeyHeaderName, value } };
    }

    /// <summary>One record of <paramref name="signal" /> whose resource names <paramref name="project" />.</summary>
    private static string Payload(string signal, string project, int offset = 0)
    {
        var time = TestData.Nanos(DateTimeOffset.UtcNow.AddSeconds(-2));
        var service = offset == 0 ? "checkout" : "billing";
        return signal switch
        {
            Signals.Traces => OtlpData.Traces(time, offset: offset, project: project, service: service),
            Signals.Logs => OtlpData.Logs(time, offset == 0 ? "order accepted" : "invoice sent", project, service),
            _ => OtlpData.Metrics(time, project, service)
        };
    }

    private static string WithoutProject(string json)
    {
        var payload = JsonNode.Parse(json)!;
        var attributes = payload.AsObject().Single().Value![0]!["resource"]!["attributes"]!.AsArray();
        attributes.Remove(attributes.Single(static attribute =>
            attribute!["key"]!.GetValue<string>() == "nami.project.id"));
        return payload.ToJsonString();
    }

    private async Task<StatusCode> RefusalAsync(CollectorHost host, string signal, byte[] payload, Metadata? headers)
    {
        var refusal = await Assert.ThrowsExactlyAsync<RpcException>(() =>
            host.ExportGrpcAsync(signal, payload, context.CancellationToken, headers));
        return refusal.StatusCode;
    }

    private async Task<int> RowsAsync(CollectorHost host, string signal, string project)
    {
        var snapshot = await host.Store.ReadSnapshotAsync(project, ct: context.CancellationToken);
        return signal switch
        {
            Signals.Traces => snapshot.Spans.Count,
            Signals.Logs => snapshot.Logs.Count,
            _ => snapshot.Metrics.Count
        };
    }

    /// <summary>Everything a project's snapshot holds of any signal, including the trace of having seen one.</summary>
    private async Task<int> RecordedAsync(CollectorHost host, string project)
    {
        var snapshot = await host.Store.ReadSnapshotAsync(project, ct: context.CancellationToken);
        return snapshot.Spans.Count + snapshot.Logs.Count + snapshot.Metrics.Count + snapshot.Signals.Count +
               snapshot.Resources.Count;
    }
}
