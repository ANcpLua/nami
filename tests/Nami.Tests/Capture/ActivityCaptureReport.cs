using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using JetBrains.Annotations;

namespace Nami.Tests.Capture;

/// <summary>The shared machine-readable report for raw native producer checks.</summary>
[PublicAPI]
public static class ActivityCaptureReport
{
    public const int SchemaVersion = 1;

    public static string Serialize(string profile, IReadOnlyList<ActivitySnapshot> spans,
        IReadOnlyList<ActivitySnapshot> afterDisposal)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("status", "passed");
            writer.WriteNumber("schema_version", SchemaVersion);
            writer.WriteString("profile", profile);
            writer.WriteString("runtime", RuntimeInformation.FrameworkDescription);
            writer.WriteBoolean("dynamicCodeSupported", RuntimeFeature.IsDynamicCodeSupported);
            writer.WriteNumber("span_count", spans.Count);
            WriteSpans(writer, "spans", spans);
            WriteSpans(writer, "after_disposal_spans", afterDisposal);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteSpans(Utf8JsonWriter writer, string name, IReadOnlyList<ActivitySnapshot> spans)
    {
        writer.WriteStartArray(name);
        foreach (var span in spans)
        {
            writer.WriteStartObject();
            writer.WriteString("path", span.Path);
            writer.WriteString("source", span.Source);
            writer.WriteString("operation", span.Operation);
            writer.WriteString("kind", span.Kind.ToString());
            writer.WriteString("status", span.Status.ToString());
            writer.WriteString("trace_id", span.Trace.ToHexString());
            writer.WriteString("span_id", span.Span.ToHexString());
            writer.WriteString("parent_span_id", span.Parent.ToHexString());
            writer.WriteStartObject("captured");
            foreach (var key in span.Tags.Keys.Where(CapturedActivityAssertions.IsCapturedField)
                         .Order(StringComparer.Ordinal))
            {
                writer.WriteStartArray(key);
                foreach (var value in CapturedActivityAssertions.GetStringValues(span.Tags, key))
                    writer.WriteStringValue(value);
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
