namespace Nami.UnitTests.Policies;

[TestClass]
[TestCategory("Fast")]
public sealed class TelemetryPolicyTests
{
    private const string Root = "0000000000000001";
    private const string SpanId = "0000000000000002";
    private const string Header = "http.request.header.x-request-id";
    private static readonly string[] s_headerValue = ["value"];

    [TestMethod]
    [DataRow("connected")]
    [DataRow("duplicates")]
    [DataRow("capture")]
    [DataRow("errors")]
    [DataRow("exports")]
    [DataRow("semconv")]
    public void Check_NoPublishedEvidence_RejectsTheInvocation(string policy)
    {
        using var invocation = TelemetryInvocation.Begin([Policy(policy)]);
        Assert.AreEqual(
            $"[{Policy(policy).Name}] No trace was published. Capture through TelemetryCapture.TraceAsync inside the test.",
            Assert.ContainsSingle(invocation.Violations()));
    }

    [TestMethod]
    public void ConnectedTrace_CapturedChildOfRoot_AcceptsTheTrace()
    {
        Assert.IsEmpty(Violations(new ConnectedTraceAttribute(), Span()));
    }

    [TestMethod]
    [DataRow(null, "starts a second root")]
    [DataRow("missing", "names parent missing")]
    [DataRow(SpanId, "has cyclic parentage")]
    public void ConnectedTrace_InvalidParent_IdentifiesTheDisconnectedSpan(string? parent, string message)
    {
        var failure =
            Assert.ContainsSingle(Violations(new ConnectedTraceAttribute(), Span() with { ParentSpanId = parent }));
        Assert.Contains(message, failure);
        Assert.Contains("operation", failure);
    }

    [TestMethod]
    public void NoDuplicateSpans_SeparateCapturedObservations_RejectsRetransmission()
    {
        var span = Span();
        using var invocation = TelemetryInvocation.Begin([new NoDuplicateSpansAttribute()]);
        TelemetryInvocation.Publish(Published(span));
        TelemetryInvocation.Publish(Published(span with { }));
        Assert.Contains("was exported 2 times", Assert.ContainsSingle(invocation.Violations()));
    }

    [TestMethod]
    public void NoDuplicateSpans_TwoViewsOfOneObservation_AcceptsTheSharedCapture()
    {
        var span = Span();
        using var invocation = TelemetryInvocation.Begin([new NoDuplicateSpansAttribute()]);
        TelemetryInvocation.Publish(Published(span));
        TelemetryInvocation.Publish(Published(span));
        Assert.IsEmpty(invocation.Violations());
    }

    [TestMethod]
    public void NoDuplicateSpans_ParentAndChildRepeatOperation_RejectsDoubleInstrumentation()
    {
        var parent = Span();
        var child = parent with { SpanId = "0000000000000003", ParentSpanId = SpanId };
        Assert.Contains("repeats its parent",
            Assert.ContainsSingle(Violations(new NoDuplicateSpansAttribute(), parent, child)));
    }

    [TestMethod]
    public void CapturesExactly_SelectedHeader_AcceptsOnlyTheRequestedField()
    {
        Assert.IsEmpty(Violations(new CapturesExactlyAttribute(Header),
            Span() with { Attributes = new Dictionary<string, object?> { [Header] = s_headerValue } }));
    }

    [TestMethod]
    public void CapturesExactly_MissingSelectedHeader_ReportsTheMissingEvidence()
    {
        Assert.AreEqual($"[CapturesExactly] '{Header}' was selected, but no span captured it.",
            Assert.ContainsSingle(Violations(new CapturesExactlyAttribute(Header), Span())));
    }

    [TestMethod]
    public void CapturesExactly_UnselectedHeader_RejectsUnexpectedCapture()
    {
        var failures = Violations(new CapturesExactlyAttribute(),
            Span() with { Attributes = new Dictionary<string, object?> { [Header] = s_headerValue } });
        Assert.Contains($"captured unselected '{Header}'", Assert.ContainsSingle(failures));
    }

    [TestMethod]
    public void NoErrorTelemetry_ErrorOutsideSelectedTrace_DoesNotRejectTheTrace()
    {
        var other = Span() with { TraceId = TestSpan.UnrelatedTraceId, Status = ActivityStatusCode.Error };
        Assert.IsEmpty(Violations(new NoErrorTelemetryAttribute(), Span(), other));
    }

    [TestMethod]
    public void NoErrorTelemetry_ErrorStatus_ReportsTheOriginalDescription()
    {
        Assert.Contains("ended with error status: declined", Assert.ContainsSingle(Violations(
            new NoErrorTelemetryAttribute(),
            Span() with { Status = ActivityStatusCode.Error, StatusDescription = "declined" })));
    }

    [TestMethod]
    public void NoErrorTelemetry_ExceptionEvent_ReportsTypeAndMessage()
    {
        var span = Span() with
        {
            Events =
            [
                new SpanEventRecord("exception", 1,
                    new Dictionary<string, object?>
                        { ["exception.type"] = "OrderFailure", ["exception.message"] = "declined" })
            ]
        };
        var failure = Assert.ContainsSingle(Violations(new NoErrorTelemetryAttribute(), span));
        Assert.Contains("OrderFailure", failure);
        Assert.Contains("declined", failure);
    }

    [TestMethod]
    [DataRow(0L, null, null, true)]
    [DataRow(1L, "rejected", null, false)]
    [DataRow(0L, null, "coalesced", false)]
    public void ExportsCleanly_ReceiptOutcome_PreservesRejectionsAndWarnings(long rejected, string? error,
        string? warning, bool accepted)
    {
        using var invocation = TelemetryInvocation.Begin([new ExportsCleanlyAttribute()]);
        var receipt = new ExportReceipt("traces", 1, rejected, error) { Warnings = warning is null ? [] : [warning] };
        TelemetryInvocation.Publish(new PublishedTrace(new TraceEvidence(TestSpan.TraceId,
            new TelemetrySnapshot([Span()], [], [], [receipt])), Root));
        var failures = invocation.Violations();
        Assert.HasCount(accepted ? 0 : 1, failures);
        if (!accepted) Assert.Contains(error ?? warning!, failures[0]);
    }

    [TestMethod]
    public void ExportsCleanly_NoReceipt_RejectsMissingExportEvidence()
    {
        Assert.AreEqual("[ExportsCleanly] The capture recorded no export.",
            Assert.ContainsSingle(Violations(new ExportsCleanlyAttribute(), Span())));
    }

    [TestMethod]
    [DataRow("http.response.status_code", "200", false)]
    [DataRow("http.response.status_code", 200L, true)]
    [DataRow("http.request.method", "CUSTOM", true)]
    [DataRow("http.request.method", 1L, false)]
    [DataRow("custom.attribute", true, true)]
    public void SemanticConventions_GeneratedRegistry_UsesItsDeclaredValueType(string key, object value, bool valid)
    {
        var failures = Violations(new ConformsToSemanticConventionsAttribute(),
            Span() with { Attributes = new Dictionary<string, object?> { [key] = value } });
        Assert.HasCount(valid ? 0 : 1, failures);
        if (!valid) Assert.Contains($"attribute '{key}' does not match registry type", failures[0]);
    }

    [TestMethod]
    public void SemanticConventions_DeprecatedAttribute_ReportsTheGeneratedRegistryDecision()
    {
        Assert.Contains("captured deprecated attribute 'db.system'", Assert.ContainsSingle(Violations(
            new ConformsToSemanticConventionsAttribute(),
            Span() with { Attributes = new Dictionary<string, object?> { ["db.system"] = "postgresql" } })));
    }

    [TestMethod]
    public void Verify_PolicyViolation_FailsPassingResultAndPreservesOriginalFailure()
    {
        using var invocation = TelemetryInvocation.Begin([new ConnectedTraceAttribute()]);
        TelemetryInvocation.Publish(Published(Span() with { ParentSpanId = "missing" }));
        var passing = new TestResult { Outcome = UnitTestOutcome.Passed };
        var original = new AssertFailedException("original assertion");
        var failed = new TestResult { Outcome = UnitTestOutcome.Failed, TestFailureException = original };
        var store = new EvidenceStore();
        invocation.Verify([passing, failed], store);
        Assert.AreEqual(UnitTestOutcome.Failed, passing.Outcome);
        Assert.Contains("names parent missing", passing.TestFailureException!.Message);
        Assert.AreSequenceEqual([EvidenceStore.Location], passing.ResultFiles!);
        Assert.Contains("names parent missing", store.Text!);
        Assert.AreSame(original, failed.TestFailureException);
        Assert.IsNull(failed.ResultFiles);
    }

    [TestMethod]
    public void Verify_SatisfiedPolicy_KeepsPassingResultWithoutWritingEvidence()
    {
        using var invocation = TelemetryInvocation.Begin([new ConnectedTraceAttribute()]);
        TelemetryInvocation.Publish(Published(Span()));
        var result = new TestResult { Outcome = UnitTestOutcome.Passed };
        var store = new EvidenceStore();
        invocation.Verify([result], store);
        Assert.AreEqual(UnitTestOutcome.Passed, result.Outcome);
        Assert.IsNull(result.TestFailureException);
        Assert.IsNull(store.Text);
    }

    private static TelemetryPolicyAttribute Policy(string name)
    {
        return name switch
        {
            "connected" => new ConnectedTraceAttribute(), "duplicates" => new NoDuplicateSpansAttribute(),
            "capture" => new CapturesExactlyAttribute(), "errors" => new NoErrorTelemetryAttribute(),
            "exports" => new ExportsCleanlyAttribute(), "semconv" => new ConformsToSemanticConventionsAttribute(),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
    }

    private static SpanRecord Span()
    {
        return new SpanRecord(TestSpan.TraceId, SpanId, "operation")
        {
            ParentSpanId = Root, Kind = SpanKind.Server,
            Resource = new ResourceRecord(new Dictionary<string, object?> { ["service.name"] = "test" })
        };
    }

    private static PublishedTrace Published(params SpanRecord[] spans)
    {
        return new PublishedTrace(new TraceEvidence(TestSpan.TraceId,
            new TelemetrySnapshot(spans, [], [], [])), Root);
    }

    private static IReadOnlyList<string> Violations(TelemetryPolicyAttribute policy, params SpanRecord[] spans)
    {
        using var invocation = TelemetryInvocation.Begin([policy]);
        TelemetryInvocation.Publish(Published(spans));
        return invocation.Violations();
    }

    private sealed class EvidenceStore : IEvidenceStore
    {
        internal const string Location = "memory://policy-evidence";
        internal string? Text { get; private set; }

        public string Save(string text)
        {
            Text = text;
            return Location;
        }
    }
}
