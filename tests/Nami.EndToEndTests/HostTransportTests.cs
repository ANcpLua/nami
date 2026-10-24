using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Nami.Tests.Infrastructure;
using Nami.Collector.Storage;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
namespace Nami.EndToEndTests;

[TestClass]
[TestCategory("EndToEnd")]
public sealed class HostTransportTests(TestContext context)
{
    private static readonly string[] ToolNames = ["changes_recent","errors_recent","logs_search","sources_status","traces_get","traces_slowest"];
    private static JsonElement Content(CallToolResult result) => (object?)result.StructuredContent is JsonElement json ? json : throw new InvalidOperationException("No structured content");

    [TestMethod,Timeout(90000, CooperativeCancellation = true)]
    [DataRow("json","traces",DisplayName="Traces HTTP JSON")]
    [DataRow("protobuf","traces",DisplayName="Traces HTTP protobuf")]
    [DataRow("grpc","traces",DisplayName="Traces gRPC")]
    [DataRow("json","logs",DisplayName="Logs HTTP JSON")]
    [DataRow("protobuf","logs",DisplayName="Logs HTTP protobuf")]
    [DataRow("grpc","logs",DisplayName="Logs gRPC")]
    [DataRow("json","metrics",DisplayName="Metrics HTTP JSON")]
    [DataRow("protobuf","metrics",DisplayName="Metrics HTTP protobuf")]
    [DataRow("grpc","metrics",DisplayName="Metrics gRPC")]
    public async Task Ingest_ThreeTransports_SameRows(string transport,string signal)
    {
        var dir=context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory");
        await using(var host=await McpHarness.StartAsync(TestData.HostBinary,false,dir,context.CancellationToken))
        {
            var time=TestData.Nanos(DateTimeOffset.UtcNow.AddSeconds(-2));
            var payload=signal switch{"traces"=>OtlpData.Traces(time),"logs"=>OtlpData.Logs(time),_=>OtlpData.Metrics(time)};
            if(transport=="json")
            {using var response=await host.SendJsonAsync(signal,payload,context.CancellationToken);Assert.AreEqual(HttpStatusCode.OK,response.StatusCode);}
            else if(transport=="protobuf")
            {
                IMessage request=signal switch{"traces"=>TestData.ParseTraces(payload),"logs"=>ExportLogsServiceRequest.Parser.ParseJson(TestData.ProtobufJson(payload)),_=>ExportMetricsServiceRequest.Parser.ParseJson(payload)};
                using var content=new ByteArrayContent(request.ToByteArray());content.Headers.ContentType=new("application/x-protobuf");
                using var response=await host.Http.PostAsync($"http://127.0.0.1:{host.OtlpPort}/v1/{signal}",content,context.CancellationToken);Assert.AreEqual(HttpStatusCode.OK,response.StatusCode);
            }
            else
            {
                using var channel=GrpcChannel.ForAddress($"http://127.0.0.1:{host.GrpcPort}");
                if(signal=="traces")await ExportGrpc(channel,"TraceService",TestData.ParseTraces(payload),ExportTraceServiceResponse.Parser,context.CancellationToken);
                else if(signal=="logs")await ExportGrpc(channel,"LogsService",ExportLogsServiceRequest.Parser.ParseJson(TestData.ProtobufJson(payload)),ExportLogsServiceResponse.Parser,context.CancellationToken);
                else await ExportGrpc(channel,"MetricsService",ExportMetricsServiceRequest.Parser.ParseJson(payload),ExportMetricsServiceResponse.Parser,context.CancellationToken);
            }
            var result=await host.CallAsync("sources_status","{}",context.CancellationToken);Assert.IsFalse(result.IsError==true);
            var service=Content(result).GetProperty("services").EnumerateArray().Single(static r=>r.GetProperty("service").GetString()=="checkout");
            var status=service.GetProperty("signals").EnumerateArray().Single(r=>r.GetProperty("signal").GetString()==signal);
            Assert.AreEqual("active",status.GetProperty("kind").GetString());
        }
        await using var store=new DuckDbStore(Path.Combine(dir,"nami.duckdb"));
        var snapshot=await store.ReadSnapshotAsync("default",ct:context.CancellationToken);
        if(signal=="traces") {Assert.HasCount(1,snapshot.Spans);Assert.AreEqual("GET /orders",snapshot.Spans[0].Name);}
        else if(signal=="logs") {Assert.HasCount(1,snapshot.Logs);Assert.Contains("order accepted",snapshot.Logs[0].Body!);}
        else
        {
            Assert.HasCount(1,snapshot.Metrics);Assert.AreEqual("http.server.active_requests",snapshot.Metrics[0].MetricName);
            var points=await store.ListMetricPointsAsync(new MetricPointQuery { ProjectId="default",MetricName=snapshot.Metrics[0].MetricName,SeriesId=snapshot.Metrics[0].SeriesId,Limit=10 },context.CancellationToken);
            using var data=JsonDocument.Parse(Assert.ContainsSingle(points).Observation.DataJson!);
            Assert.AreEqual("1",data.RootElement.GetProperty("value").GetProperty("value").GetString());
        }
    }

    private static async Task ExportGrpc<TRequest,TResponse>(GrpcChannel channel,string service,TRequest request,MessageParser<TResponse> parser,CancellationToken cancellationToken)
        where TRequest:class,IMessage<TRequest> where TResponse:class,IMessage<TResponse>
    {
        var signal=service=="TraceService"?"trace":service=="LogsService"?"logs":"metrics";
        var method=new Method<TRequest,TResponse>(MethodType.Unary,$"opentelemetry.proto.collector.{signal}.v1.{service}","Export",
            Marshallers.Create<TRequest>(static r=>r.ToByteArray(),bytes=>(TRequest)request.Descriptor.Parser.ParseFrom(bytes)),
            Marshallers.Create<TResponse>(static r=>r.ToByteArray(),bytes=>parser.ParseFrom(bytes)));
        using var call=channel.CreateCallInvoker().AsyncUnaryCall(method,null,new CallOptions(cancellationToken:cancellationToken),request);
        var response=await call.ResponseAsync;
        Assert.IsNotNull(response);
    }

    public static IEnumerable<TestDataRow<(string Tool,string Transport)>> ToolTransportCases()
    {
        var tools=Directory.GetFiles(Path.Combine(ProcessRunner.RepositoryRoot,"contracts/json-schema"),"*.input.json")
            .Select(static path=>Path.GetFileName(path)[..^".input.json".Length]).Order(StringComparer.Ordinal).ToArray();
        if(tools.Length!=6)throw new InvalidOperationException("Expected six generated tool schemas.");
        foreach(var tool in tools)
            foreach(var transport in new[]{"json","protobuf","grpc"})
                yield return new((tool,transport)){DisplayName=tool+" via "+transport};
    }

    [TestMethod,Timeout(90000, CooperativeCancellation = true)]
    [DynamicData(nameof(ToolTransportCases))]
    public async Task EveryTool_ThreeOtlpTransports_ReturnsTypedEvidence(string tool,string transport)
    {
        await using var host=await McpHarness.StartAsync(TestData.HostBinary,false,context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"),context.CancellationToken);
        var time=TestData.Nanos(DateTimeOffset.UtcNow.AddSeconds(-3));
        var traces=JsonNode.Parse(OtlpData.Traces(time,error:true,exceptionEvent:true))!;
        traces["resourceSpans"]![0]!["resource"]!["attributes"]!.AsArray().Add((JsonNode)OtlpData.Attribute("service.version","1.0.0"));
        var logs=OtlpData.Logs(time,exception:true,trace:1.ToString("x32",System.Globalization.CultureInfo.InvariantCulture),span:1.ToString("x16",System.Globalization.CultureInfo.InvariantCulture));
        var metrics=OtlpData.Metrics(time);
        foreach(var signal in new[]{"traces","logs","metrics"})
        {
            var payload=signal=="traces"?traces.ToJsonString():signal=="logs"?logs:metrics;
            if(transport=="json")
            {using var response=await host.SendJsonAsync(signal,payload,context.CancellationToken);Assert.AreEqual(HttpStatusCode.OK,response.StatusCode);}
            else if(transport=="protobuf")
            {
                IMessage request=signal switch{"traces"=>TestData.ParseTraces(payload),"logs"=>ExportLogsServiceRequest.Parser.ParseJson(TestData.ProtobufJson(payload)),_=>ExportMetricsServiceRequest.Parser.ParseJson(payload)};
                using var content=new ByteArrayContent(request.ToByteArray());content.Headers.ContentType=new("application/x-protobuf");
                using var response=await host.Http.PostAsync($"http://127.0.0.1:{host.OtlpPort}/v1/{signal}",content,context.CancellationToken);Assert.AreEqual(HttpStatusCode.OK,response.StatusCode);
            }
            else
            {
                using var channel=GrpcChannel.ForAddress($"http://127.0.0.1:{host.GrpcPort}");
                if(signal=="traces")await ExportGrpc(channel,"TraceService",TestData.ParseTraces(payload),ExportTraceServiceResponse.Parser,context.CancellationToken);
                else if(signal=="logs")await ExportGrpc(channel,"LogsService",ExportLogsServiceRequest.Parser.ParseJson(TestData.ProtobufJson(payload)),ExportLogsServiceResponse.Parser,context.CancellationToken);
                else await ExportGrpc(channel,"MetricsService",ExportMetricsServiceRequest.Parser.ParseJson(payload),ExportMetricsServiceResponse.Parser,context.CancellationToken);
            }
        }
        var args=tool=="traces_get"?"{\"trace_id\":\""+1.ToString("x32",System.Globalization.CultureInfo.InvariantCulture)+"\"}":tool=="logs_search"?"{\"query\":\"order\"}":"{}";
        var result=await host.CallAsync(tool,args,context.CancellationToken);Assert.IsFalse(result.IsError==true);
        var value=Content(result);Assert.IsNull(Nami.Api.Contracts.ContractJson.Validate(Nami.Api.Contracts.ContractTypes.Analysis(tool).Output,value));
        if(tool=="traces_get")Assert.AreEqual(1,value.GetProperty("trace").GetProperty("span_count").GetInt32());
        else
        {
            var member=tool switch{"sources_status"=>"services","errors_recent"=>"groups","traces_slowest"=>"traces","logs_search"=>"records",_=>"changes"};
            Assert.AreEqual(1,value.GetProperty(member).GetArrayLength());
            if(tool=="errors_recent")Assert.AreEqual(2,value.GetProperty("groups")[0].GetProperty("count").GetInt32());
            if(tool=="logs_search")Assert.AreEqual("order accepted",value.GetProperty("records")[0].GetProperty("body").GetString());
            if(tool=="changes_recent")Assert.AreEqual("1.0.0",value.GetProperty("changes")[0].GetProperty("to").GetString());
        }
    }

    [TestMethod,Timeout(90000, CooperativeCancellation = true)]
    [DataRow(false,DisplayName="HTTP MCP result")]
    [DataRow(true,DisplayName="stdio MCP result")]
    public async Task Result_ThreeParts_OneSource(bool stdio)
    {
        await using var host=await McpHarness.StartAsync(TestData.HostBinary,stdio,context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"),context.CancellationToken);
        var result=await host.CallAsync("sources_status","{}",context.CancellationToken);
        Assert.IsFalse(result.IsError==true);Assert.HasCount(2,result.Content);
        Assert.AreEqual(Content(result).GetRawText(),Assert.IsExactInstanceOfType<TextContentBlock>(result.Content[1]).Text);
        Assert.Contains("incomplete",Assert.IsExactInstanceOfType<TextContentBlock>(result.Content[0]).Text);
    }

    [TestMethod,Timeout(90000, CooperativeCancellation = true)]
    public async Task ToolsList_Snapshot_MatchesAnnotationsAndCache()
    {
        await using var host=await McpHarness.StartAsync(TestData.HostBinary,false,context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"),context.CancellationToken);
        var list=await host.Client.ListToolsAsync(new ListToolsRequestParams(),context.CancellationToken);
        Assert.AreSequenceEqual(ToolNames,list.Tools.Select(static t=>t.Name).ToArray());
        Assert.AreEqual(TimeSpan.FromHours(1),list.TimeToLive);
        Assert.AreEqual(CacheScope.Public,list.CacheScope);
        foreach(var tool in list.Tools){Assert.IsNotNull(tool.Annotations);Assert.IsTrue(tool.Annotations.ReadOnlyHint);Assert.IsTrue(tool.Annotations.IdempotentHint);Assert.IsFalse(tool.Annotations.DestructiveHint);Assert.IsFalse(tool.Annotations.OpenWorldHint);Assert.IsNotNull(tool.OutputSchema);}
        var second=await host.Client.ListToolsAsync(new ListToolsRequestParams(),context.CancellationToken);
        Assert.AreSequenceEqual(list.Tools.Select(static t=>t.Name).ToArray(),second.Tools.Select(static t=>t.Name).ToArray());
        for(var i=0;i<list.Tools.Count;i++)Assert.IsTrue(JsonElement.DeepEquals(list.Tools[i].InputSchema,second.Tools[i].InputSchema));
        var complete=JsonSerializer.SerializeToElement(list,ModelContextProtocol.McpJsonUtilities.DefaultOptions.GetTypeInfo<ListToolsResult>());
        var actualPath=Path.Combine(context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"),"tools-list.actual.json");
        await File.WriteAllTextAsync(actualPath,complete.GetRawText(),context.CancellationToken);context.AddResultFile(actualPath);
        var expectedPath=Path.Combine(ProcessRunner.RepositoryRoot,"tests/Nami.EndToEndTests/Fixtures/tools-list.json");
        Assert.IsTrue(File.Exists(expectedPath),"The committed tools/list golden is missing. Actual: "+actualPath);
        using var expected=JsonDocument.Parse(await File.ReadAllTextAsync(expectedPath,context.CancellationToken));
        Assert.IsTrue(JsonElement.DeepEquals(expected.RootElement,complete),"Full tools/list manifest differs from committed golden. Actual: "+actualPath);
    }

    [TestMethod,Timeout(90000, CooperativeCancellation = true)]
    public async Task SourcesStatus_AfterOneRequest_LastSeenWithinOneSecond()
    {
        await using var host=await McpHarness.StartAsync(TestData.HostBinary,false,context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"),context.CancellationToken);
        var before=DateTimeOffset.UtcNow;
        using var response=await host.SendJsonAsync("traces",OtlpData.Traces(TestData.Nanos(before.AddYears(2))),context.CancellationToken);Assert.AreEqual(HttpStatusCode.OK,response.StatusCode);
        var result=Content(await host.CallAsync("sources_status","{}",context.CancellationToken));
        var signals=result.GetProperty("services").EnumerateArray().Single(static s=>s.GetProperty("service").GetString()=="checkout").GetProperty("signals");
        var trace=signals.EnumerateArray().Single(static s=>s.GetProperty("signal").GetString()=="traces");
        var last=DateTimeOffset.Parse(trace.GetProperty("last_seen").GetString()!,System.Globalization.CultureInfo.InvariantCulture);
        Assert.IsTrue(last>=before&&last<=DateTimeOffset.UtcNow);Assert.IsLessThanOrEqualTo(1,(last-before).TotalSeconds);
    }

    [TestMethod,Timeout(90000, CooperativeCancellation = true)]
    public async Task Http_KeyAndResourceConflict_Refused()
    {
        const string keys="{\"a\":[{\"key\":\"test-a\",\"roles\":[\"ingest\",\"read\"]}],\"b\":[{\"key\":\"test-b\",\"roles\":[\"ingest\",\"read\"]}]}";
        await using var host=await McpHarness.StartAsync(TestData.HostBinary,false,context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"),context.CancellationToken,keys,"test-a");
        using var missing=await host.SendJsonAsync("traces",OtlpData.Traces(TestData.Nanos(DateTimeOffset.UtcNow.AddSeconds(-2)),project:"a"),context.CancellationToken);Assert.AreEqual(HttpStatusCode.Unauthorized,missing.StatusCode);
        using var conflict=await host.SendJsonAsync("traces",OtlpData.Traces(TestData.Nanos(DateTimeOffset.UtcNow.AddSeconds(-2)),project:"b"),context.CancellationToken,"test-a");Assert.AreEqual(HttpStatusCode.BadRequest,conflict.StatusCode);
        using var valid=await host.SendJsonAsync("traces",OtlpData.Traces(TestData.Nanos(DateTimeOffset.UtcNow.AddSeconds(-2)),project:"a"),context.CancellationToken,"test-a");Assert.AreEqual(HttpStatusCode.OK,valid.StatusCode);
        var response=Content(await host.CallAsync("traces_slowest","{}",context.CancellationToken));Assert.HasCount(1,response.GetProperty("traces").EnumerateArray().ToArray());
    }

    [TestMethod,Timeout(90000, CooperativeCancellation = true)]
    public async Task IncomingMeta_Traceparent_ParentsToolSpan()
    {
        const string traceId="0123456789abcdef0123456789abcdef";const string parentId="fedcba9876543210";
        const string keys="{\"nami\":[{\"key\":\"self-read\",\"roles\":[\"read\"]}]}";
        await using var host=await McpHarness.StartAsync(TestData.HostBinary,false,context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"),context.CancellationToken,keys,"self-read");
        await host.Client.CallToolAsync(new CallToolRequestParams{Name="sources_status",Meta=new JsonObject{["traceparent"]="00-"+traceId+"-"+parentId+"-01"}},context.CancellationToken);
        var deadline=DateTimeOffset.UtcNow.AddSeconds(20);JsonElement? recorded=null;
        while(DateTimeOffset.UtcNow<deadline)
        {
            var result=await host.CallAsync("traces_get","{\"trace_id\":\""+traceId+"\"}",context.CancellationToken);
            if(result.IsError!=true){recorded=Content(result).GetProperty("trace");break;}
            await Task.Delay(100,context.CancellationToken);
        }
        Assert.IsNotNull(recorded,"Self-exported MCP span was not ingested");
        Assert.AreEqual(parentId,recorded.Value.GetProperty("spans")[0].GetProperty("parent_span_id").GetString());
    }
    [TestMethod,Timeout(90000, CooperativeCancellation = true)]
    public async Task Http_KeyRolesAndCrossProjectReads_AreEnforced()
    {
        const string keys="{\"a\":[{\"key\":\"ingest-a\",\"roles\":[\"ingest\"]},{\"key\":\"read-a\",\"roles\":[\"read\"]}],\"b\":[{\"key\":\"read-b\",\"roles\":[\"read\"]}]}";
        await using var host=await McpHarness.StartAsync(TestData.HostBinary,false,context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"),context.CancellationToken,keys,"read-a");
        var json=OtlpData.Traces(TestData.Nanos(DateTimeOffset.UtcNow.AddSeconds(-2)),project:"a");
        using var refused=await host.SendJsonAsync("traces",json,context.CancellationToken,"read-a");Assert.AreEqual(HttpStatusCode.Forbidden,refused.StatusCode);
        using var accepted=await host.SendJsonAsync("traces",json,context.CancellationToken,"ingest-a");Assert.AreEqual(HttpStatusCode.OK,accepted.StatusCode);
        using var unauthorized=new HttpRequestMessage(HttpMethod.Post,$"http://127.0.0.1:{host.HttpPort}/mcp"){Content=new StringContent("{}",Encoding.UTF8,"application/json")};
        unauthorized.Headers.Add("x-otlp-api-key","ingest-a");using var answer=await host.Http.SendAsync(unauthorized,context.CancellationToken);Assert.AreEqual(HttpStatusCode.Forbidden,answer.StatusCode);
        var transport=new HttpClientTransport(new HttpClientTransportOptions{Endpoint=new Uri($"http://127.0.0.1:{host.HttpPort}/mcp"),AdditionalHeaders=new Dictionary<string,string>{{"x-otlp-api-key","read-b"}}});
        await using var otherTransport=transport;
        await using var client=await McpClient.CreateAsync(transport,new McpClientOptions{ProtocolVersion="2026-07-28"},cancellationToken:context.CancellationToken);
        var foreign=await client.CallToolAsync(new CallToolRequestParams{Name="traces_slowest"},context.CancellationToken);
        Assert.AreEqual(0,Content(foreign).GetProperty("traces").GetArrayLength());
    }

    [TestMethod,Timeout(90000, CooperativeCancellation = true)]
    [DataRow(false,DisplayName="Missing traceparent")]
    [DataRow(true,DisplayName="Malformed traceparent")]
    public async Task IncomingMeta_AbsentOrInvalid_StartsRoot(bool invalid)
    {
        const string keys="{\"nami\":[{\"key\":\"self-read\",\"roles\":[\"read\"]}]}";
        await using var host=await McpHarness.StartAsync(TestData.HostBinary,false,context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"),context.CancellationToken,keys,"self-read");
        var call=new CallToolRequestParams{Name="sources_status"};
        if(invalid)call.Meta=new JsonObject{["traceparent"]="invalid"};
        var first=await host.Client.CallToolAsync(call,context.CancellationToken);Assert.IsFalse(first.IsError==true);
        var deadline=DateTimeOffset.UtcNow.AddSeconds(20);JsonElement? root=null;
        while(DateTimeOffset.UtcNow<deadline)
        {
            // Polling emits its own spans; query the operation under test before applying the top-ten limit.
            var rows=Content(await host.CallAsync("traces_slowest","{\"operation\":\"tools/call sources_status\"}",context.CancellationToken)).GetProperty("traces");
            var matches=rows.EnumerateArray().Where(static r=>r.GetProperty("operation").GetString()=="tools/call sources_status").ToArray();
            if(matches.Length>0)
            {
                var traceId=matches[0].GetProperty("trace_id").GetString();
                var trace=Content(await host.CallAsync("traces_get",new JsonObject{["trace_id"]=traceId}.ToJsonString(),context.CancellationToken));
                root=trace.GetProperty("trace").GetProperty("root_span");break;
            }
            await Task.Delay(100,context.CancellationToken);
        }
        Assert.IsNotNull(root,"Root tool span was not exported");Assert.IsFalse(root.Value.TryGetProperty("parent_span_id",out _));
    }

    [TestMethod,Timeout(90000, CooperativeCancellation = true)]
    public async Task Health_ReturnsGeneratedSchemaAndRevision()
    {
        await using var host=await McpHarness.StartAsync(TestData.HostBinary,false,context.TestTempDirectory ?? throw new InvalidOperationException("Missing test temp directory"),context.CancellationToken);
        using var response=await host.Http.GetAsync($"http://127.0.0.1:{host.HttpPort}/health",context.CancellationToken);
        Assert.AreEqual(HttpStatusCode.OK,response.StatusCode);
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.CancellationToken));
        Assert.IsNull(Nami.Api.Contracts.ContractJson.Validate("Health.HealthReport",json.RootElement));
        Assert.AreEqual(Nami.Api.Contracts.ContractRevision.Value,json.RootElement.GetProperty("contract_revision").GetString());
        Assert.AreEqual("healthy",json.RootElement.GetProperty("status").GetString());
    }

}
