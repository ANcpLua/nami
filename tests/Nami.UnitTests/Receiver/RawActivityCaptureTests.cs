using System.Text.Json;
using Nami.SemanticConventions;
using Nami.Tests.Capture;

namespace Nami.UnitTests.Receiver;

[TestClass]
[TestCategory("Fast")]
public class RawActivityCaptureTests
{
    public enum RawViolation
    {
        Missing,
        Null,
        Scalar,
        ObjectArray,
        List,
        Reordered,
        DuplicateLost
    }

    private const string Key = "http.request.header.x-example";
    private const string DeprecatedGrpcMetadata = "rpc.grpc.request.metadata.authorization";
    private const string RepeatedKey = "probe.repeated";
    private const string GrpcMethod = "grpc.method";
    private const string NumericArrayKey = "probe.status_codes";

    private const string NativeHttpSource = "System.Net.Http";

    [TestMethod]
    public void Should_KeepOnlyThePath_When_RawActivityCaptureSnapshot_UrlWithQuery()
    {
        Assert.AreEqual("/ok", MutatedAfterCapture().Path);
    }

    [TestMethod]
    public void Should_KeepTheOriginalValues_When_RawActivityCaptureSnapshot_ArrayMutatedOrReplacedAfterCapture()
    {
        CapturedActivityAssertions.RequireStringValues(MutatedAfterCapture().Tags, Key, "first", "first", "",
            "comma,kept");
    }

    [TestMethod]
    public void Should_KeepTheValueTheActivityReports_When_RawActivityCaptureSnapshot_TagKeyAddedTwice()
    {
        using var activity = new Activity("repeated-tag");
        activity.AddTag(RepeatedKey, "first").AddTag(RepeatedKey, "second").Start();
        Assert.AreEqual(activity.GetTagItem(RepeatedKey), ActivitySnapshot.Capture(activity).Tags[RepeatedKey]);
    }

    [TestMethod]
    public void Should_KeepTheOriginalValues_When_RawActivityCaptureSnapshot_Int64ArrayMutatedAfterCapture()
    {
        long[] statusCodes = [200, 404];
        using var activity = new Activity("numeric-array");
        activity.SetTag(NumericArrayKey, statusCodes).Start();
        var snapshot = ActivitySnapshot.Capture(activity);
        statusCodes[0] = 500;
        Assert.AreSequenceEqual([200L, 404L], Assert.IsInstanceOfType<long[]>(snapshot.Tags[NumericArrayKey]));
    }

    [TestMethod]
    [DataRow(RawViolation.Missing)]
    [DataRow(RawViolation.Null)]
    [DataRow(RawViolation.Scalar)]
    [DataRow(RawViolation.ObjectArray)]
    [DataRow(RawViolation.List)]
    [DataRow(RawViolation.Reordered)]
    [DataRow(RawViolation.DuplicateLost)]
    public void Should_FailNamingTheKey_When_RawActivityCapture_WrongClrTypeOrChangedSequence(RawViolation violation)
    {
        var tags = Tags(violation);
        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            CapturedActivityAssertions.RequireStringValues(tags, Key, "first", "first", ""));
        Assert.Contains(Key, error.Message);
    }

    [TestMethod]
    [DataRow(RawViolation.Missing)]
    [DataRow(RawViolation.Null)]
    [DataRow(RawViolation.Scalar)]
    [DataRow(RawViolation.ObjectArray)]
    [DataRow(RawViolation.List)]
    [DataRow(RawViolation.Reordered)]
    [DataRow(RawViolation.DuplicateLost)]
    public void Should_FailNamingTheExpectedStringArray_When_RawActivityCapture_WrongClrTypeOrChangedSequence(
        RawViolation violation)
    {
        var tags = Tags(violation);
        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            CapturedActivityAssertions.RequireStringValues(tags, Key, "first", "first", ""));
        Assert.Contains("string[]", error.Message);
    }

    [TestMethod]
    public void Should_PassParentCheck_When_RawActivityCapture_ChildOfTheExpectedParent()
    {
        using var parent = new Activity("parent");
        parent.SetIdFormat(ActivityIdFormat.W3C).Start();
        using var child = new Activity("child");
        child.Start();
        CapturedActivityAssertions.RequireParentContext(ActivitySnapshot.Capture(child), parent.Context);
    }

    [TestMethod]
    public void Should_FailAsTraceChanged_When_RawActivityCapture_MatchingParentSpanInDifferentTrace()
    {
        using var parent = new Activity("parent");
        parent.SetIdFormat(ActivityIdFormat.W3C).Start();
        using var child = new Activity("child");
        child.Start();
        var wrongTrace = ActivitySnapshot.Capture(child) with { Trace = ActivityTraceId.CreateRandom() };
        var parentContext = parent.Context;
        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            CapturedActivityAssertions.RequireParentContext(wrongTrace, parentContext));
        Assert.Contains("trace changed", error.Message);
    }

    [TestMethod]
    public void Should_FailNamingTheExpectedTrace_When_RawActivityCapture_MatchingParentSpanInDifferentTrace()
    {
        using var parent = new Activity("parent");
        parent.SetIdFormat(ActivityIdFormat.W3C).Start();
        using var child = new Activity("child");
        child.Start();
        var wrongTrace = ActivitySnapshot.Capture(child) with { Trace = ActivityTraceId.CreateRandom() };
        var parentContext = parent.Context;
        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            CapturedActivityAssertions.RequireParentContext(wrongTrace, parentContext));
        Assert.Contains(parent.TraceId.ToHexString(), error.Message);
    }

    [TestMethod]
    public void Should_Pass_When_RawActivityCapture_UniqueSpanIdentity()
    {
        using var activity = new Activity("operation");
        activity.SetIdFormat(ActivityIdFormat.W3C).Start();
        CapturedActivityAssertions.RequireUniqueSpanIdentities([ActivitySnapshot.Capture(activity)]);
    }

    [TestMethod]
    public void Should_BeRejected_When_RawActivityCapture_DuplicateSpanIdentity()
    {
        using var activity = new Activity("operation");
        activity.SetIdFormat(ActivityIdFormat.W3C).Start();
        var snapshot = ActivitySnapshot.Capture(activity);
        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            CapturedActivityAssertions.RequireUniqueSpanIdentities([snapshot, snapshot]));
        Assert.Contains("duplicate", error.Message);
    }

    [TestMethod]
    public void Should_UseSchemaVersionOne_When_RawActivityCaptureReport_HttpAndGrpc()
    {
        using var report = HttpAndGrpcReport(out _);
        Assert.AreEqual(1, report.RootElement.GetProperty("schema_version").GetInt32());
    }

    [TestMethod]
    public void Should_HavePassedStatus_When_RawActivityCaptureReport_ValidCapture()
    {
        using var report = HttpAndGrpcReport(out _);
        Assert.AreEqual("passed", report.RootElement.GetProperty("status").GetString());
    }

    [TestMethod]
    public void Should_CountBothSpans_When_RawActivityCaptureReport_HttpAndGrpc()
    {
        using var report = HttpAndGrpcReport(out _);
        Assert.AreEqual(2, report.RootElement.GetProperty("span_count").GetInt32());
    }

    [TestMethod]
    public void Should_HaveNoAfterDisposalSpans_When_RawActivityCaptureReport_NoLateSpans()
    {
        using var report = HttpAndGrpcReport(out _);
        Assert.AreEqual(0, report.RootElement.GetProperty("after_disposal_spans").GetArrayLength());
    }

    [TestMethod]
    public void Should_HaveTheUrlPath_When_RawActivityCaptureReport_HttpSpan()
    {
        using var report = HttpAndGrpcReport(out _);
        Assert.AreEqual("/ok", report.RootElement.GetProperty("spans")[0].GetProperty("path").GetString());
    }

    [TestMethod]
    public void Should_HaveTheMethodAsPath_When_RawActivityCaptureReport_GrpcSpan()
    {
        using var report = HttpAndGrpcReport(out _);
        Assert.AreEqual("/capture.Service/Unary",
            report.RootElement.GetProperty("spans")[1].GetProperty("path").GetString());
    }

    [TestMethod]
    public void Should_HaveItsTraceId_When_RawActivityCaptureReport_HttpSpan()
    {
        using var report = HttpAndGrpcReport(out var httpTraceId);
        Assert.AreEqual(httpTraceId, report.RootElement.GetProperty("spans")[0].GetProperty("trace_id").GetString());
    }

    [TestMethod]
    public void Should_KeepEmptyAndCommaValues_When_RawActivityCaptureReport_HttpHeaderArray()
    {
        using var report = HttpAndGrpcReport(out _);
        string?[] capturedValues =
        [
            .. report.RootElement.GetProperty("spans")[0].GetProperty("captured").GetProperty(Key).EnumerateArray()
                .Select(value => value.GetString())
        ];
        Assert.AreSequenceEqual(["", "a,b"], capturedValues);
    }

    [TestMethod]
    public void Should_KeepItsValue_When_RawActivityCaptureReport_GrpcBinaryMetadata()
    {
        using var report = HttpAndGrpcReport(out _);
        Assert.AreEqual("AAH/",
            report.RootElement.GetProperty("spans")[1].GetProperty("captured")
                .GetProperty("rpc.response.metadata.trace-bin")[0].GetString());
    }

    [TestMethod]
    public void Should_NotPass_When_RawActivityCaptureReport_StringListForClrArray()
    {
        List<string> invalidValues = ["value"];
        using var activity = new Activity("wrong-clr-type");
        activity.SetTag(Key, invalidValues).Start();
        var snapshot = ActivitySnapshot.Capture(activity);
        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            ActivityCaptureReport.Serialize("configured", [snapshot], []));
        Assert.Contains("expected CLR string[]", error.Message);
    }

    [TestMethod]
    public void Should_Pass_When_RawActivityCaptureDisposal_StoppedNativeSpan()
    {
        using var activity = new Activity("after-disposal");
        activity.SetIdFormat(ActivityIdFormat.W3C).Start();
        CapturedActivityAssertions.RequireCaptureStopped([NativeHttpSnapshot(activity)], NativeHttpSource);
    }

    [TestMethod]
    public void Should_Fail_When_RawActivityCaptureDisposal_MissingNativeSpan()
    {
        _ = Assert.ThrowsExactly<InvalidOperationException>(() =>
            CapturedActivityAssertions.RequireCaptureStopped([], NativeHttpSource));
    }

    [TestMethod]
    [DataRow(Key)]
    [DataRow(DeprecatedGrpcMetadata)]
    public void Should_FailAsOutlived_When_RawActivityCaptureDisposal_LeftoverCapturedField(string field)
    {
        using var activity = new Activity("after-disposal");
        activity.SetIdFormat(ActivityIdFormat.W3C).Start();
        var captured = NativeHttpSnapshot(activity) with
        {
            Tags = new Dictionary<string, object?> { [field] = new[] { "leftover" } }
        };
        var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            CapturedActivityAssertions.RequireCaptureStopped([captured], NativeHttpSource));
        Assert.Contains("outlived", error.Message);
    }

    private static ActivitySnapshot NativeHttpSnapshot(Activity activity)
    {
        return ActivitySnapshot.Capture(activity) with { Source = NativeHttpSource };
    }

    private static ActivitySnapshot MutatedAfterCapture()
    {
        string[] values = ["first", "first", "", "comma,kept"];
        using var activity = new Activity("http");
        activity.SetTag(UrlAttributes.Full, "https://example.invalid/ok?ignored=true")
            .SetTag(Key, values).Start();
        var snapshot = ActivitySnapshot.Capture(activity);
        values[0] = "mutated";
        string[] replacement = ["replaced"];
        activity.SetTag(Key, replacement);
        return snapshot;
    }

    private static Dictionary<string, object?> Tags(RawViolation violation)
    {
        var tags = new Dictionary<string, object?>();
        if (violation != RawViolation.Missing)
        {
            tags[Key] = violation switch
            {
                RawViolation.Missing => throw new UnreachableException("handled above"),
                RawViolation.Null => null,
                RawViolation.Scalar => "first",
                RawViolation.ObjectArray => new object[] { "first", "first", "" },
                RawViolation.List => new List<string> { "first", "first", "" },
                RawViolation.Reordered => new[] { "", "first", "first" },
                RawViolation.DuplicateLost => new[] { "first", "" },
                _ => throw new ArgumentOutOfRangeException(nameof(violation))
            };
        }

        return tags;
    }

    private static JsonDocument HttpAndGrpcReport(out string httpTraceId)
    {
        string[] httpValues = ["", "a,b"];
        string[] grpcValues = ["AAH/"];
        using var http = new Activity("http");
        http.SetIdFormat(ActivityIdFormat.W3C)
            .SetTag(UrlAttributes.Full, "https://example.invalid/ok")
            .SetTag(Key, httpValues).Start();
        using var grpc = new Activity("grpc");
        grpc.SetTag(GrpcMethod, "/capture.Service/Unary")
            .SetTag("rpc.response.metadata.trace-bin", grpcValues).Start();
        httpTraceId = http.TraceId.ToHexString();
        return JsonDocument.Parse(ActivityCaptureReport.Serialize("configured",
            [ActivitySnapshot.Capture(http), ActivitySnapshot.Capture(grpc)], []));
    }
}
