using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Nami.EndToEndTests;

internal static class ToolResults
{
    internal static JsonElement Structured(CallToolResult result)
    {
        return (object?)result.StructuredContent is JsonElement json
            ? json
            : throw new InvalidOperationException("No structured content");
    }
}
