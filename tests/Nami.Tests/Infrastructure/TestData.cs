using System.Globalization;
using System.Text.Json.Nodes;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Nami.Tests.Infrastructure;

internal static class TestData
{
    internal static readonly DateTimeOffset s_now =
        DateTimeOffset.Parse("2026-10-24T12:00:00Z", CultureInfo.InvariantCulture);

    internal static string HostBinary => Path.Combine(ProcessRunner.RepositoryRoot, "artifacts", "publish", "nami");

    internal static ExportTraceServiceRequest ParseTraces(string json)
    {
        return ExportTraceServiceRequest.Parser.ParseJson(ProtobufJson(json));
    }

    internal static string ProtobufJson(string json)
    {
        var node = JsonNode.Parse(json)!;

        ConvertIds(node);
        return node.ToJsonString();

        void ConvertIds(JsonNode? value)
        {
            switch (value)
            {
                case JsonObject obj:
                    {
                        foreach (var pair in obj.ToArray())
                        {
                            if (pair.Key is "traceId" or "spanId" or "parentSpanId" && pair.Value is JsonValue)
                                obj[pair.Key] = Convert.ToBase64String(Convert.FromHexString(pair.Value.GetValue<string>()));
                            else ConvertIds(pair.Value);
                        }
                    }
                    break;
                case JsonArray array:
                    {
                        foreach (var item in array)
                            ConvertIds(item);
                    }
                    break;
            }
        }
    }

    // Not TimeConversions.ToUnixNanoUnsigned on purpose: tests compare the times the product stores with this value,
    // so it stays a second, independent computation of the same conversion. The two agree from the Unix epoch to
    // 2262, where this one's signed arithmetic overflows (the product's unsigned range lasts until 2554); both throw
    // outside their range, this one OverflowException and the product ArgumentOutOfRangeException.
    internal static ulong Nanos(DateTimeOffset value)
    {
        return checked((ulong)((value.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) * 100));
    }
}
