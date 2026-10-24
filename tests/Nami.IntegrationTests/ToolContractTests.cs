using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using Nami.Api.Contracts;
using Nami.Collector.Storage;
using Nami.Tests.Infrastructure;
using Nami.Tools;
namespace Nami.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
[DependsOn(typeof(IngestStoreTests), nameof(IngestStoreTests.Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
public sealed class ToolContractTests(TestContext context)
{
    private static JsonElement Input(string value) { using var document = JsonDocument.Parse(value); return document.RootElement.Clone(); }
    private static ToolRunResult Run(string name, ISnapshot snapshot, string json = "{}") => ToolDispatcher.Run(name, snapshot, Input(json), new ToolClock(snapshot.CapturedAt));
    private async Task<ISnapshot> SeedAll(DuckDbStore store, FakeTimeProvider clock)
    {
        var time = TestData.Nanos(TestData.Now.AddSeconds(-5));
        var tracePayload = JsonNode.Parse(OtlpData.Traces(time, error:true, exceptionEvent:true))!;
        tracePayload["resourceSpans"]![0]!["resource"]!["attributes"]!.AsArray().Add((JsonNode)OtlpData.Attribute("service.version", "1.0.0"));
        await TestData.SeedTraces(store, tracePayload.ToJsonString(), context.CancellationToken);
        await TestData.SeedLogs(store, OtlpData.Logs(time, exception:true, trace:1.ToString("x32",System.Globalization.CultureInfo.InvariantCulture), span:1.ToString("x16",System.Globalization.CultureInfo.InvariantCulture)), clock, context.CancellationToken);
        await TestData.SeedMetrics(store, OtlpData.Metrics(time), context.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));
        return await store.ReadSnapshotAsync("default", ct:context.CancellationToken);
    }

    [TestMethod]
    public async Task SourcesStatus_NoRecords_ReportsSilentWithDuration()
    {
        await using var store = new DuckDbStore(":memory:", timeProvider:new FakeTimeProvider(TestData.Now));
        var result = Run("sources_status", await store.ReadSnapshotAsync("default", ct:context.CancellationToken));
        Assert.IsFalse(result.IsError);
        foreach (var signal in result.Content.GetProperty("services")[0].GetProperty("signals").EnumerateArray())
        { Assert.AreEqual("silent", signal.GetProperty("kind").GetString()); Assert.AreEqual("PT1H", signal.GetProperty("silent_for").GetString()); }
        Assert.IsFalse(result.Content.GetProperty("freshness").GetProperty("complete").GetBoolean());
    }

    [TestMethod]
    [DataRow("sources_status", DisplayName="Sources same snapshot")]
    [DataRow("errors_recent", DisplayName="Errors same snapshot")]
    [DataRow("traces_get", DisplayName="Trace same snapshot")]
    [DataRow("traces_slowest", DisplayName="Tail same snapshot")]
    [DataRow("logs_search", DisplayName="Logs same snapshot")]
    [DataRow("changes_recent", DisplayName="Changes same snapshot")]
    public async Task AnyTool_SameSnapshot_ByteIdentical(string name)
    {
        var clock = new FakeTimeProvider(TestData.Now); await using var store = new DuckDbStore(":memory:", timeProvider:clock);
        var snapshot = await SeedAll(store, clock);
        var args = name == "traces_get" ? "{\"trace_id\":\""+1.ToString("x32",System.Globalization.CultureInfo.InvariantCulture)+"\"}" : name == "logs_search" ? "{\"query\":\"accepted\"}" : "{}";
        var first = Run(name,snapshot,args); var second = Run(name,snapshot,args);
        Assert.IsFalse(first.IsError); Assert.AreEqual(first.Content.GetRawText(),second.Content.GetRawText());
    }

    [TestMethod]
    [DataRow("sources_status",DisplayName="Restart sources")]
    [DataRow("errors_recent",DisplayName="Restart errors")]
    [DataRow("traces_get",DisplayName="Restart trace")]
    [DataRow("traces_slowest",DisplayName="Restart tail")]
    [DataRow("logs_search",DisplayName="Restart logs")]
    [DataRow("changes_recent",DisplayName="Restart changes")]
    public async Task AnyTool_AfterRestart_SameAnswer(string name)
    {
        var path=Path.Combine(context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"),"restart.duckdb"); var clock=new FakeTimeProvider(TestData.Now);
        string id; string before;
        var args = name == "traces_get" ? "{\"trace_id\":\""+1.ToString("x32",CultureInfo.InvariantCulture)+"\"}" : name == "logs_search" ? "{\"query\":\"accepted\"}" : "{}";
        await using(var store=new DuckDbStore(path,timeProvider:clock))
        { var snapshot=await SeedAll(store,clock); id=snapshot.Id; before=Run(name,snapshot,args).Content.GetRawText(); }
        clock.Advance(TimeSpan.FromMinutes(5));
        await using(var store=new DuckDbStore(path,timeProvider:clock))
        { var saved=await store.ReadSnapshotAsync("default",id,context.CancellationToken); Assert.AreEqual(before,Run(name,saved,args).Content.GetRawText()); }
    }

    [TestMethod]
    [DataRow("sources_status","service","services", DisplayName="Project sources")]
    [DataRow("errors_recent","error_type","groups", DisplayName="Project errors")]
    [DataRow("traces_slowest","trace_id","traces", DisplayName="Project tail")]
    [DataRow("logs_search","body","records", DisplayName="Project logs")]
    [DataRow("changes_recent","to","changes", DisplayName="Project changes")]
    public async Task Fields_EachTool_ProjectsDeclaredMembers(string name,string field,string member)
    {
        var clock=new FakeTimeProvider(TestData.Now); await using var store=new DuckDbStore(":memory:",timeProvider:clock);
        var snapshot=await SeedAll(store,clock); var args=new JsonObject{["fields"]=new JsonArray(field)};
        if(name=="logs_search")args["query"]="accepted";
        var result=Run(name,snapshot,args.ToJsonString()); Assert.IsFalse(result.IsError);
        Assert.IsGreaterThan(0,result.Content.GetProperty(member).GetArrayLength());
        foreach(var item in result.Content.GetProperty(member).EnumerateArray())
            Assert.AreEqual(field,Assert.ContainsSingle(item.EnumerateObject().Select(static p=>p.Name).ToArray()));
    }

    [TestMethod]
    public async Task Fields_Unknown_RefusedNamingAllowed()
    {
        await using var store=new DuckDbStore(":memory:",timeProvider:new FakeTimeProvider(TestData.Now));
        var snapshot=await store.ReadSnapshotAsync("default",ct:context.CancellationToken);
        var result=Run("sources_status",snapshot,"{\"fields\":[\"does_not_exist\"]}");
        Assert.IsTrue(result.IsError); Assert.AreEqual("fields",result.Content.GetProperty("field").GetString());
        Assert.Contains("service",result.Content.GetProperty("allowed").EnumerateArray().Select(static x=>x.GetString()).ToArray());
    }

    [TestMethod]
    [DataRow("profiles",DisplayName="Profiles unavailable")]
    [DataRow("baggage",DisplayName="Baggage unavailable")]
    public async Task ProfilesOrBaggage_ReturnsDataUnavailable(string field)
    {
        await using var store=new DuckDbStore(":memory:",timeProvider:new FakeTimeProvider(TestData.Now));
        var result=Run("sources_status",await store.ReadSnapshotAsync("default",ct:context.CancellationToken),new JsonObject{["fields"]=new JsonArray(field)}.ToJsonString());
        Assert.IsTrue(result.IsError); Assert.AreEqual("data_unavailable",result.Content.GetProperty("code").GetString());
    }

    [TestMethod]
    [DataRow("quote",DisplayName="Literal quote")]
    [DataRow("backslash",DisplayName="Literal backslash")]
    [DataRow("unicode",DisplayName="Literal Unicode")]
    [DataRow("regex",DisplayName="Regex characters are literal")]
    public async Task LogsSearch_QuotesBackslashesNonAscii_MatchLiterally(string scenario)
    {
        var query=scenario switch{"quote"=>"\"","backslash"=>"\\","unicode"=>"ü",_=>".*"};
        var clock=new FakeTimeProvider(TestData.Now); await using var store=new DuckDbStore(":memory:",timeProvider:clock);
        await TestData.SeedLogs(store,OtlpData.Logs(TestData.Nanos(TestData.Now.AddSeconds(-2)),"prefix "+query+" suffix"),clock,context.CancellationToken);
        var snapshot=await store.ReadSnapshotAsync("default",ct:context.CancellationToken);
        var result=Run("logs_search",snapshot,new JsonObject{["query"]=query}.ToJsonString());
        Assert.IsFalse(result.IsError); Assert.AreEqual("prefix "+query+" suffix",result.Content.GetProperty("records")[0].GetProperty("body").GetString());
        var wrong=Run("logs_search",snapshot,"{\"query\":\"PREFIX\"}");Assert.AreEqual(0,wrong.Content.GetProperty("records").GetArrayLength());
    }

    [TestMethod]
    public async Task Exception_LogEventAndSpanEvent_GroupTogether()
    {
        var clock=new FakeTimeProvider(TestData.Now);await using var store=new DuckDbStore(":memory:",timeProvider:clock);
        var snapshot=await SeedAll(store,clock);var result=Run("errors_recent",snapshot);
        var group=Assert.ContainsSingle(result.Content.GetProperty("groups").EnumerateArray().ToArray());
        Assert.AreEqual("OrderFailure",group.GetProperty("error_type").GetString());Assert.AreEqual(2,group.GetProperty("count").GetInt32());
        Assert.IsFalse(result.Content.GetRawText().Contains("stacktrace",StringComparison.Ordinal));
        Assert.IsFalse(result.Content.GetRawText().Contains("SensitiveStackTrace",StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Window_WithinWatermark_EmptyIsComplete()
    {
        var clock=new FakeTimeProvider(TestData.Now);await using var store=new DuckDbStore(":memory:",timeProvider:clock);
        await TestData.SeedLogs(store,OtlpData.Logs(TestData.Nanos(TestData.Now.AddSeconds(-2)),"one"),clock,context.CancellationToken);
        var snapshot=await store.ReadSnapshotAsync("default",ct:context.CancellationToken);
        var result=Run("logs_search",snapshot,"{\"query\":\"absent\"}");
        Assert.AreEqual(0,result.Content.GetProperty("records").GetArrayLength());Assert.IsTrue(result.Content.GetProperty("freshness").GetProperty("complete").GetBoolean());
        var missing=Run("errors_recent",snapshot);Assert.IsFalse(missing.Content.GetProperty("freshness").GetProperty("complete").GetBoolean());
    }

    [TestMethod]
    public async Task Since_Boundaries_HalfOpenAgainstWatermark()
    {
        var clock=new FakeTimeProvider(TestData.Now);await using var store=new DuckDbStore(":memory:",timeProvider:clock);
        var start=TestData.Now.AddMinutes(-15);
        await TestData.SeedTraces(store,OtlpData.Traces(TestData.Nanos(start),offset:0),context.CancellationToken);
        await TestData.SeedTraces(store,OtlpData.Traces(TestData.Nanos(start)-1,offset:1),context.CancellationToken);
        await TestData.SeedTraces(store,OtlpData.Traces(TestData.Nanos(TestData.Now),offset:2),context.CancellationToken);
        await TestData.SeedTraces(store,OtlpData.Traces(TestData.Nanos(TestData.Now)+1,offset:3),context.CancellationToken);
        var result=Run("traces_slowest",await store.ReadSnapshotAsync("default",ct:context.CancellationToken),"{\"since\":\"-15m\"}");
        var item=Assert.ContainsSingle(result.Content.GetProperty("traces").EnumerateArray().ToArray());Assert.AreEqual(1.ToString("x32",System.Globalization.CultureInfo.InvariantCulture),item.GetProperty("trace_id").GetString());
    }

    [TestMethod]
    [DataRow(1023,DisplayName="Below minimum budget")]
    [DataRow(262145,DisplayName="Above maximum budget")]
    public async Task ToolError_EachRefusalClass_HasOneShape(int budget)
    {
        await using var store=new DuckDbStore(":memory:",timeProvider:new FakeTimeProvider(TestData.Now));
        var result=Run("sources_status",await store.ReadSnapshotAsync("default",ct:context.CancellationToken),new JsonObject{["max_bytes"]=budget}.ToJsonString());
        Assert.IsTrue(result.IsError); Assert.AreEqual("invalid_argument",result.Content.GetProperty("code").GetString());
        Assert.AreEqual("max_bytes",result.Content.GetProperty("field").GetString()); Assert.IsNull(ContractJson.Validate("Common.ToolError",result.Content));
    }

    [TestMethod,Timeout(600000, CooperativeCancellation = true)]
    public async Task ErrorsRecent_FiveHundredThousandSpans_UnderMaxBytes()
    {
        var clock=new FakeTimeProvider(TestData.Now);await using var store=new DuckDbStore(":memory:",jobQueueCapacity:2,timeProvider:clock);
        const int batchSize=5000;const int total=500000;
        for(var offset=0;offset<total;offset+=batchSize)
        {
            var attrs=new JsonArray(OtlpData.Attribute("error.type","Failure-"+(offset/batchSize).ToString("D3",CultureInfo.InvariantCulture)));
            await TestData.SeedTraces(store,OtlpData.Traces(TestData.Nanos(TestData.Now.AddSeconds(-2)),batchSize,offset,attributes:attrs,error:true),context.CancellationToken);
            if ((offset + batchSize) % 100000 == 0) context.WriteLine($"Accepted {offset + batchSize} OTLP spans for the byte-budget proof.");
        }
        var snapshot=await store.ReadSnapshotAsync("default",ct:context.CancellationToken);Assert.HasCount(total,snapshot.Spans);
        var result=Run("errors_recent",snapshot,"{\"max_bytes\":4096}");Assert.IsFalse(result.IsError);
        Assert.IsLessThanOrEqualTo(4096,Encoding.UTF8.GetByteCount(result.Content.GetRawText()));
        Assert.IsTrue(result.Content.GetProperty("freshness").GetProperty("truncated").GetBoolean());
        var cursor=result.Content.GetProperty("next_cursor").GetString();Assert.IsNotNull(cursor);
        var all=new HashSet<string>(StringComparer.Ordinal);
        while(true)
        {
            foreach(var item in result.Content.GetProperty("groups").EnumerateArray())
            {Assert.IsTrue(all.Add(item.GetProperty("error_type").GetString()!));Assert.AreEqual(batchSize,item.GetProperty("count").GetInt32());}
            if(!result.Content.TryGetProperty("next_cursor",out var next))break;
            result=Run("errors_recent",snapshot,new JsonObject{["max_bytes"]=4096,["cursor"]=next.GetString()}.ToJsonString());Assert.IsFalse(result.IsError);
        }
        Assert.HasCount(total/batchSize,all);
    }
}
