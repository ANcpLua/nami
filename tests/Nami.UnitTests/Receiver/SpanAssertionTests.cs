namespace Nami.UnitTests.Receiver;

[TestClass]
[TestCategory("Fast")]
public class SpanAssertionTests
{
    private const string Parent = "0011223344556677";
    private const string Child = "8899aabbccddeeff";
    private const string Source = "native.source";
    private const string Operation = "request";

    [TestMethod]
    public void Should_BeReturned_When_SpanAssertions_NativeSpanWithMatchingSourceOperationAndKind()
    {
        var span = NativeSpan();
        Assert.AreSame(span, SpanAssertions.HasSingleNativeSpan([span], Source, Operation, SpanKind.Server));
    }

    [TestMethod]
    [DataRow("replacement.source", Operation, SpanKind.Server, 1)]
    [DataRow(Source, "replacement", SpanKind.Server, 1)]
    [DataRow(Source, Operation, SpanKind.Internal, 1)]
    [DataRow(Source, Operation, SpanKind.Server, 2)]
    public void Should_FailNativeOwnership_When_SpanAssertions_SubstitutedOrDuplicateInstrumentation(
        string source, string operation, SpanKind kind, int observedCount)
    {
        var observed = Enumerable.Repeat(NativeSpan(), observedCount);
        Assert.ThrowsExactly<AssertFailedException>(() =>
            SpanAssertions.HasSingleNativeSpan(observed, source, operation, kind));
    }

    [TestMethod]
    public void Should_PassParentCheck_When_SpanAssertions_SpanUnderItsParentContext()
    {
        SpanAssertions.HasParentContext(NativeSpan(), new SpanRecord(TestSpan.TraceId, Parent, "parent"));
    }

    [TestMethod]
    [DataRow(TestSpan.UnrelatedTraceId, Child, Parent)]
    [DataRow(TestSpan.TraceId, Child, Child)]
    [DataRow(TestSpan.TraceId, Parent, Parent)]
    public void Should_FailParentCheck_When_SpanAssertions_DisconnectedTraceWrongParentOrReusedIdentity(
        string traceId, string spanId, string parentSpanId)
    {
        var parent = new SpanRecord(TestSpan.TraceId, Parent, "parent");
        var changed = new SpanRecord(traceId, spanId, Operation)
        {
            ParentSpanId = parentSpanId
        };
        Assert.ThrowsExactly<AssertFailedException>(() => SpanAssertions.HasParentContext(changed, parent));
    }

    private static SpanRecord NativeSpan()
    {
        return new SpanRecord(TestSpan.TraceId, Child, Operation)
        {
            ParentSpanId = Parent,
            Kind = SpanKind.Server,
            Scope = new InstrumentationScopeRecord { Name = Source }
        };
    }
}
