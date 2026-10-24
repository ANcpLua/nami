using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using Nami.Collector.Storage;
using Nami.Tests.Infrastructure;
using Nami.Tools;
namespace Nami.Collector.Tests;

[TestClass]
[DependsOn(typeof(IngestStoreTests), nameof(IngestStoreTests.Catalog_EmbeddedConstants_MatchRegistrySnapshot))]
public sealed class ToolEdgeTests(TestContext context)
{
    private static readonly int[] DescendingIds = [3, 2, 1];
    private static ToolRunResult Run(ISnapshot snapshot,string name,string json)
    {using var input=JsonDocument.Parse(json);return ToolDispatcher.Run(name,snapshot,input.RootElement,new ToolClock(snapshot.CapturedAt));}

    [TestMethod]
    [DataRow("-1q",DisplayName="Unsupported relative unit")]
    [DataRow("tomorrow",DisplayName="Invalid timestamp")]
    [DataRow("2030-01-01T00:00:00Z",DisplayName="Future timestamp")]
    [DataRow("2026-02-30T00:00:00Z",DisplayName="Invalid calendar date")]
    public async Task Since_FutureMalformedAndOverflow_Refused(string since)
    {
        await using var store=new DuckDbStore(":memory:",timeProvider:new FakeTimeProvider(TestData.Now));
        var snapshot=await store.ReadSnapshotAsync("default",ct:context.CancellationToken);
        var result=Run(snapshot,"sources_status",new JsonObject{["since"]=since}.ToJsonString());
        Assert.IsTrue(result.IsError);Assert.AreEqual("invalid_argument",result.Content.GetProperty("code").GetString());
        Assert.AreEqual("since",result.Content.GetProperty("field").GetString());
    }

    [TestMethod]
    [DataRow("sources_status", "{\"max_bytes\":1024.0}", DisplayName="Decimal byte budget")]
    [DataRow("traces_slowest", "{\"limit\":1e1}", DisplayName="Exponent row limit")]
    public async Task ToolArguments_IntegralJsonNumbers_Accepted(string tool,string input)
    {
        await using var store=new DuckDbStore(":memory:",timeProvider:new FakeTimeProvider(TestData.Now));
        var result=Run(await store.ReadSnapshotAsync("default",ct:context.CancellationToken),tool,input);
        Assert.IsFalse(result.IsError,result.Content.GetRawText());Assert.IsTrue(result.Content.TryGetProperty("freshness",out _));
    }

    [TestMethod]
    [DataRow(false,DisplayName="Oversized unknown field value")]
    [DataRow(true,DisplayName="Oversized unknown property name")]
    public async Task Refusal_LargeUnknownName_RespectsByteBudget(bool property)
    {
        await using var store=new DuckDbStore(":memory:",timeProvider:new FakeTimeProvider(TestData.Now));
        var huge=new string('x',100000);var input=new JsonObject{["max_bytes"]=1024};
        if(property)input[huge]=1;else input["fields"]=new JsonArray(huge);
        var result=Run(await store.ReadSnapshotAsync("default",ct:context.CancellationToken),"sources_status",input.ToJsonString());
        Assert.IsTrue(result.IsError);Assert.AreEqual("invalid_argument",result.Content.GetProperty("code").GetString());
        Assert.IsLessThanOrEqualTo(1024,Encoding.UTF8.GetByteCount(result.Content.GetRawText()));
        Assert.IsTrue(result.Content.TryGetProperty("field",out _));
    }

    [TestMethod]
    public async Task Cursor_ExpiredSnapshot_NamesMissingResource()
    {
        var clock=new FakeTimeProvider(TestData.Now);await using var store=new DuckDbStore(":memory:",timeProvider:clock,snapshotLifetime:TimeSpan.FromMinutes(1));
        await TestData.SeedTraces(store,OtlpData.Traces(TestData.Nanos(TestData.Now.AddSeconds(-1)),count:30),context.CancellationToken);
        var service=new NamiToolService(store);
        using var initial=JsonDocument.Parse("{\"limit\":100,\"max_bytes\":1024}");
        var first=await service.InvokeAsync("traces_slowest",initial.RootElement,"default",context.CancellationToken);Assert.IsFalse(first.IsError);
        var id=first.Content.GetProperty("snapshot_id").GetString();var cursor=first.Content.GetProperty("next_cursor").GetString();clock.Advance(TimeSpan.FromMinutes(1));
        using var input=JsonDocument.Parse(new JsonObject{["limit"]=100,["max_bytes"]=1024,["cursor"]=cursor}.ToJsonString());
        var failed=await service.InvokeAsync("traces_slowest",input.RootElement,"default",context.CancellationToken);
        Assert.IsTrue(failed.IsError);Assert.AreEqual("not_found",failed.Content.GetProperty("code").GetString());
        Assert.AreEqual("snapshot",failed.Content.GetProperty("resource_type").GetString());Assert.AreEqual(id,failed.Content.GetProperty("resource_id").GetString());
    }

    [TestMethod]
    [DataRow("logs_search","{}","query",DisplayName="Missing query")]
    [DataRow("logs_search","{\"query\":\"\"}","query",DisplayName="Empty query")]
    [DataRow("logs_search","{\"query\":17}","query",DisplayName="Wrong query type")]
    [DataRow("traces_get","{\"trace_id\":\"bad\"}","trace_id",DisplayName="Malformed trace identity")]
    [DataRow("traces_slowest","{\"limit\":0}","limit",DisplayName="Limit below minimum")]
    [DataRow("traces_slowest","{\"limit\":101}","limit",DisplayName="Limit above maximum")]
    [DataRow("sources_status","{\"fields\":{}}","fields",DisplayName="Wrong fields type")]
    public async Task InvalidInputs_IdentifyRepairableField(string tool,string input,string field)
    {
        await using var store=new DuckDbStore(":memory:",timeProvider:new FakeTimeProvider(TestData.Now));
        var result=Run(await store.ReadSnapshotAsync("default",ct:context.CancellationToken),tool,input);
        Assert.IsTrue(result.IsError);Assert.AreEqual("invalid_argument",result.Content.GetProperty("code").GetString());
        Assert.AreEqual(field,result.Content.GetProperty("field").GetString());
    }

    [TestMethod]
    public async Task Window_BeyondWatermark_NeverComplete()
    {
        await using var store=new DuckDbStore(":memory:",timeProvider:new FakeTimeProvider(TestData.Now));
        var snapshot=await store.ReadSnapshotAsync("default",ct:context.CancellationToken);
        var result=Run(snapshot,"errors_recent","{}");Assert.IsFalse(result.IsError);
        Assert.AreEqual(0,result.Content.GetProperty("groups").GetArrayLength());Assert.IsFalse(result.Content.GetProperty("freshness").GetProperty("complete").GetBoolean());
    }

    [TestMethod]
    public async Task Fields_TraceProjection_OmitsUnrequestedData()
    {
        var clock=new FakeTimeProvider(TestData.Now);await using var store=new DuckDbStore(":memory:",timeProvider:clock);
        await TestData.SeedTraces(store,OtlpData.Traces(TestData.Nanos(TestData.Now.AddSeconds(-1))),context.CancellationToken);
        var snapshot=await store.ReadSnapshotAsync("default",ct:context.CancellationToken);
        var result=Run(snapshot,"traces_get",new JsonObject{["trace_id"]=1.ToString("x32",CultureInfo.InvariantCulture),["fields"]=new JsonArray("trace_id","span_count")}.ToJsonString());
        Assert.IsFalse(result.IsError);var trace=result.Content.GetProperty("trace");
        Assert.AreEqual(1,trace.GetProperty("span_count").GetInt32());Assert.IsFalse(trace.TryGetProperty("spans",out _));Assert.HasCount(2,trace.EnumerateObject().ToArray());
    }

    [TestMethod]
    public async Task Cursor_PagesCoverAllRowsWithoutDuplicates()
    {
        var clock=new FakeTimeProvider(TestData.Now);await using var store=new DuckDbStore(":memory:",timeProvider:clock);
        await TestData.SeedTraces(store,OtlpData.Traces(TestData.Nanos(TestData.Now.AddSeconds(-1)),count:40),context.CancellationToken);
        var snapshot=await store.ReadSnapshotAsync("default",ct:context.CancellationToken);
        var input=new JsonObject{["limit"]=100,["max_bytes"]=1024};var seen=new HashSet<string>(StringComparer.Ordinal);int pages=0;
        while(true)
        {
            var result=Run(snapshot,"traces_slowest",input.ToJsonString());Assert.IsFalse(result.IsError);
            Assert.IsLessThanOrEqualTo(1024,Encoding.UTF8.GetByteCount(result.Content.GetRawText()));
            foreach(var row in result.Content.GetProperty("traces").EnumerateArray())Assert.IsTrue(seen.Add(row.GetProperty("trace_id").GetString()!));
            pages++;
            if(!result.Content.TryGetProperty("next_cursor",out var cursor))break;
            input["cursor"]=cursor.GetString();
        }
        Assert.IsGreaterThan(1,pages);Assert.HasCount(40,seen);
    }

    [TestMethod]
    public async Task Cursor_ChangedQueryAndMalformedValue_Refused()
    {
        var clock=new FakeTimeProvider(TestData.Now);await using var store=new DuckDbStore(":memory:",timeProvider:clock);
        await TestData.SeedTraces(store,OtlpData.Traces(TestData.Nanos(TestData.Now.AddSeconds(-1)),count:30),context.CancellationToken);
        var snapshot=await store.ReadSnapshotAsync("default",ct:context.CancellationToken);
        var first=Run(snapshot,"traces_slowest","{\"limit\":100,\"max_bytes\":1024}");
        var cursor=first.Content.GetProperty("next_cursor").GetString();
        foreach(var bad in new[]{new JsonObject{["limit"]=99,["max_bytes"]=1024,["cursor"]=cursor},new JsonObject{["cursor"]="not base64"}})
        {var result=Run(snapshot,"traces_slowest",bad.ToJsonString());Assert.IsTrue(result.IsError);Assert.AreEqual("cursor",result.Content.GetProperty("field").GetString());}
    }

    [TestMethod]
    public async Task TracesSlowest_OrdersByDurationWithStableIds()
    {
        var clock=new FakeTimeProvider(TestData.Now);await using var store=new DuckDbStore(":memory:",timeProvider:clock);
        await TestData.SeedTraces(store,OtlpData.Traces(TestData.Nanos(TestData.Now.AddSeconds(-1)),count:3),context.CancellationToken);
        var result=Run(await store.ReadSnapshotAsync("default",ct:context.CancellationToken),"traces_slowest","{}");
        Assert.AreSequenceEqual(DescendingIds.Select(static n=>n.ToString("x32",CultureInfo.InvariantCulture)).ToArray(),result.Content.GetProperty("traces").EnumerateArray().Select(static t=>t.GetProperty("trace_id").GetString()).ToArray());
    }

    [TestMethod]
    public async Task TracesSlowest_OperationAndLimit_FilterBeforeRanking()
    {
        var clock=new FakeTimeProvider(TestData.Now);await using var store=new DuckDbStore(":memory:",timeProvider:clock);
        var start=TestData.Nanos(TestData.Now.AddSeconds(-2));
        await TestData.SeedTraces(store,OtlpData.Traces(start,count:3),context.CancellationToken);
        var other=JsonNode.Parse(OtlpData.Traces(start,offset:99,operation:"POST /other"))!;
        other["resourceSpans"]![0]!["scopeSpans"]![0]!["spans"]![0]!["endTimeUnixNano"]=(start+1000000000).ToString(CultureInfo.InvariantCulture);
        await TestData.SeedTraces(store,other.ToJsonString(),context.CancellationToken);
        var result=Run(await store.ReadSnapshotAsync("default",ct:context.CancellationToken),"traces_slowest","{\"operation\":\"GET /orders\",\"limit\":2}");
        Assert.IsFalse(result.IsError);var rows=result.Content.GetProperty("traces").EnumerateArray().ToArray();
        Assert.HasCount(2,rows);Assert.AreEqual(3.ToString("x32",CultureInfo.InvariantCulture),rows[0].GetProperty("trace_id").GetString());
        Assert.AreEqual(2.ToString("x32",CultureInfo.InvariantCulture),rows[1].GetProperty("trace_id").GetString());
    }

    [TestMethod]
    public async Task ChangesRecent_RepeatedResourceDoesNotCreateChange()
    {
        var clock=new FakeTimeProvider(TestData.Now);await using var store=new DuckDbStore(":memory:",timeProvider:clock);
        async Task AddVersion(string version,int id)
        {
            var input=JsonNode.Parse(OtlpData.Traces(TestData.Nanos(TestData.Now.AddSeconds(-1)),offset:id))!;
            input["resourceSpans"]![0]!["resource"]!["attributes"]!.AsArray().Add((JsonNode)OtlpData.Attribute("service.version",version));
            await TestData.SeedTraces(store,input.ToJsonString(),context.CancellationToken);_ = await store.ReadSnapshotAsync("default",ct:context.CancellationToken);
        }
        await AddVersion("1.0.0",0);clock.Advance(TimeSpan.FromSeconds(2));await AddVersion("2.0.0",1);await AddVersion("2.0.0",2);clock.Advance(TimeSpan.FromSeconds(1));
        var result=Run(await store.ReadSnapshotAsync("default",ct:context.CancellationToken),"changes_recent","{\"since\":\"2026-10-24T12:00:01Z\"}");
        var change=Assert.ContainsSingle(result.Content.GetProperty("changes").EnumerateArray().ToArray());
        Assert.AreEqual("1.0.0",change.GetProperty("from").GetString());Assert.AreEqual("2.0.0",change.GetProperty("to").GetString());
    }
}
