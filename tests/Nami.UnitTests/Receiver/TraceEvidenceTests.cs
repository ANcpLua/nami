namespace Nami.UnitTests.Receiver;

/// <summary>Checks trace relationships against canonical records without changing captured evidence.</summary>
[TestClass]
[TestCategory("Fast")]
public class TraceEvidenceTests
{
    [TestMethod]
    public void Should_KeepTheOriginalCaptureInstance_When_TraceEvidence_SelectedTrace()
    {
        var (capture, trace, _) = SelectedTrace();
        Assert.AreSame(capture, trace.Capture);
    }

    [TestMethod]
    public void Should_KeepUnrelatedCapturedSpans_When_TraceEvidence_SelectedTrace()
    {
        var (_, trace, _) = SelectedTrace();
        Assert.HasCount(2, trace.Capture.Spans);
    }

    [TestMethod]
    public void Should_SelectOnlyItsOwnSpans_When_TraceEvidence_SelectedTrace()
    {
        var (_, trace, selected) = SelectedTrace();
        Assert.AreSame(selected, Assert.ContainsSingle(trace.Spans));
    }

    [TestMethod]
    public void Should_BeTheOnlyCall_When_TraceEvidenceCalls_CrossServiceClientServerChain()
    {
        var call = Assert.ContainsSingle(MixedCallTrace().Calls);
        Assert.AreEqual(("orders", "payments"), (call.From, call.To));
    }

    [TestMethod]
    public void Should_KeepEverySpanRecord_When_TraceEvidence_RepeatedExport()
    {
        Assert.HasCount(8, MixedCallTrace().Spans);
    }

    [TestMethod]
    public void Should_BeDistinctAndOrdered_When_TraceEvidence_Services()
    {
        Assert.AreSequenceEqual(["a", "test", "z"], StoreTrace().Services);
    }

    [TestMethod]
    public void Should_BeDistinctByNamespace_When_TraceEvidenceStores_OfOneService()
    {
        Assert.AreSequenceEqual(["another", "first"],
            StoreTrace().StoresOf("a").Select(store => store.Namespace).Order(StringComparer.Ordinal));
    }

    [TestMethod]
    public void Should_BeEmpty_When_TraceEvidenceStores_OfAnUnknownService()
    {
        Assert.IsEmpty(StoreTrace().StoresOf("missing"));
    }

    [TestMethod]
    public void Should_BeDistinct_When_TraceEvidenceStores_SameNamespaceOnDifferentServers()
    {
        var trace = TestSpan.Trace(TestSpan.Create("orders", "1", store: "orders", server: "db-a"),
            TestSpan.Create("orders", "2", store: "orders", server: "db-b"));
        Assert.AreSequenceEqual(["db-a", "db-b"],
            trace.StoresOf("orders").Select(store => store.ServerAddress).Order(StringComparer.Ordinal));
    }

    private static (TelemetrySnapshot Capture, TraceEvidence Trace, SpanRecord Selected) SelectedTrace()
    {
        var selected = TestSpan.Create("orders", "selected");
        var unrelated = new SpanRecord(TestSpan.UnrelatedTraceId, "unrelated", "other operation");
        var capture = TestSpan.Only(selected, unrelated);
        return (capture, new TraceEvidence(selected.TraceId, capture), selected);
    }

    private static TraceEvidence MixedCallTrace()
    {
        var client = TestSpan.Create("orders", "client", SpanKind.Client);
        var server = TestSpan.Create("payments", "server", SpanKind.Server, "client");
        return TestSpan.Trace(client, server, server,
            TestSpan.Create("payments", "root", SpanKind.Server),
            TestSpan.Create("payments", "orphan", SpanKind.Server, "missing"),
            TestSpan.Create("orders", "same-service", SpanKind.Server, "client"),
            TestSpan.Create("payments", "internal", SpanKind.Internal, "client"),
            TestSpan.Create("payments", "wrong-parent", SpanKind.Server, "internal"));
    }

    private static TraceEvidence StoreTrace()
    {
        return TestSpan.Trace(TestSpan.Create("z", "1", store: "second"),
            TestSpan.Create("a", "2", store: "first"), TestSpan.Create("a", "3", store: "first"),
            TestSpan.Create("a", "4", store: "another"), TestSpan.Create("test", "5"));
    }
}
