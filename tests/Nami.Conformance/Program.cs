using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Nami.Tests.Infrastructure;

if(args.Length!=1)throw new ArgumentException("Expected the NativeAOT Nami binary path.");
using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));
var binary=Path.GetFullPath(args[0]);
var directory=Path.Combine(Path.GetTempPath(),"nami-conformance-"+Guid.NewGuid().ToString("N"));
string snapshotId;
string stableResult;
await using(var host=await McpHarness.StartAsync(binary,true,directory,timeout.Token))
{
    var nanos=checked((ulong)((DateTimeOffset.UtcNow.AddSeconds(-3).UtcTicks-DateTimeOffset.UnixEpoch.UtcTicks)*100));
    foreach(var signal in new[]{"traces","logs","metrics"})
    {
        var json=signal switch{"traces"=>OtlpData.Traces(nanos),"logs"=>OtlpData.Logs(nanos),_=>OtlpData.Metrics(nanos)};
        using var response=await host.SendJsonAsync(signal,json,timeout.Token);
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException("OTLP failed: "+signal+" "+response.StatusCode);
    }
    var tools=await host.Client.ListToolsAsync(cancellationToken:timeout.Token);
    if(tools.Count!=6)throw new InvalidOperationException("Expected six tools.");
    foreach(var tool in tools)
    {
        var input=tool.Name switch{"traces_get"=>"{\"trace_id\":\""+1.ToString("x32",System.Globalization.CultureInfo.InvariantCulture)+"\"}","logs_search"=>"{\"query\":\"order\"}",_=>"{}"};
        var result=await host.CallAsync(tool.Name,input,timeout.Token);
        if(result.IsError==true)throw new InvalidOperationException("Tool failed: "+tool.Name);
        var content=Structured(result);
        if(!content.TryGetProperty("freshness",out var freshness)||!freshness.GetProperty("complete").GetBoolean())throw new InvalidOperationException("Incomplete ingested evidence: "+tool.Name);
        if(result.Content.Count!=2||result.Content[1] is not TextContentBlock text||text.Text!=content.GetRawText())throw new InvalidOperationException("Content mismatch: "+tool.Name);
        if(tool.Name=="traces_get"&&content.GetProperty("trace").GetProperty("span_count").GetInt32()!=1)throw new InvalidOperationException("Trace not preserved.");
        if(tool.Name=="logs_search"&&content.GetProperty("records").GetArrayLength()!=1)throw new InvalidOperationException("Log not preserved.");
        Console.WriteLine("PASS "+tool.Name);
    }
    var first=Structured(await host.CallAsync("logs_search","{\"query\":\"order\"}",timeout.Token));
    snapshotId=first.GetProperty("snapshot_id").GetString() ?? throw new InvalidOperationException("Snapshot ID absent.");
    stableResult=first.GetRawText();
    var again=Structured(await host.CallAsync("logs_search",Replay(snapshotId),timeout.Token));
    if(again.GetRawText()!=stableResult)throw new InvalidOperationException("Snapshot replay changed the result.");
    Console.WriteLine("PASS native snapshot replay");
}
await using(var restarted=await McpHarness.StartAsync(binary,true,directory,timeout.Token))
{
    var replay=await restarted.CallAsync("logs_search",Replay(snapshotId),timeout.Token);
    if(replay.IsError==true||Structured(replay).GetRawText()!=stableResult)throw new InvalidOperationException("Restart changed the persisted snapshot result.");
    Console.WriteLine("PASS native snapshot restart");
}
Console.WriteLine("PASS NativeAOT OTLP and stdio MCP conformance");
static string Replay(string id)=>new JsonObject{["query"]="order",["snapshot_id"]=id}.ToJsonString();
static JsonElement Structured(CallToolResult result)=>(object?)result.StructuredContent is JsonElement content?content:throw new InvalidOperationException("Missing structured result.");
