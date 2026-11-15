using System.Collections;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Nami.IntegrationTests.Fuzz;

/// <summary>
///     Shrinks a protobuf case that shows a finding: it removes repeated elements (halves first), clears fields and
///     halves strings for as long as the same finding signature persists, so the reproducer keeps only what the defect
///     needs. Cases that are not a decodable protobuf body stay as they are.
/// </summary>
internal static class OtlpMinimizer
{
    internal static async Task<OtlpCase> MinimizeAsync(OtlpCase input, Func<OtlpCase, Task<bool>> fails, int budget)
    {
        if (input is not { ContentType: "application/x-protobuf", ContentEncoding: null } ||
            TryParse(input) is not { } current)
            return input;

        var progress = true;
        while (progress && budget > 0)
        {
            progress = false;
            foreach (var reduction in Reductions(current))
            {
                if (budget-- <= 0) break;
                var candidate = OtlpMutator.Clone(current);
                if (!reduction(candidate)) continue;
                var smaller = input with { Body = candidate.ToByteArray(), Note = "minimized: " + input.Note };
                if (!await fails(smaller)) continue;
                current = candidate;
                progress = true;
                break;
            }
        }

        return input with { Body = current.ToByteArray(), Note = "minimized: " + input.Note };
    }

    private static IMessage? TryParse(OtlpCase input)
    {
        try
        {
            using var stream = new MemoryStream(input.Body);
            using var coded = CodedInputStream.CreateWithLimits(stream, int.MaxValue, 100_000);
            return input.Signal switch
            {
                FuzzSignal.Traces => ExportTraceServiceRequest.Parser.ParseFrom(coded),
                FuzzSignal.Logs => ExportLogsServiceRequest.Parser.ParseFrom(coded),
                _ => ExportMetricsServiceRequest.Parser.ParseFrom(coded)
            };
        }
        catch (InvalidProtocolBufferException)
        {
            return null;
        }
    }

    /// <summary>Each reduction finds its target by path in a fresh clone; it answers false when it changes nothing.</summary>
    private static IEnumerable<Func<IMessage, bool>> Reductions(IMessage root)
    {
        var paths = new List<(List<(int Field, int Index)> Path, FieldDescriptor Field, int Count)>();
        Walk(root, [], paths);
        // Large lists first, in halves, then single elements, then whole fields, then strings.
        foreach (var (path, field, count) in paths.Where(static p => p.Field.IsRepeated && p.Count > 1)
                     .OrderByDescending(static p => p.Count))
        {
            var half = count / 2;
            yield return message => Resolve(message, path) is { } owner && RemoveRange(owner, field, half, count - half);
            yield return message => Resolve(message, path) is { } owner && RemoveRange(owner, field, 0, half);
        }

        foreach (var (path, field, count) in paths.Where(static p => p.Field.IsRepeated))
        {
            for (var index = count - 1; index >= 0; index--)
            {
                var at = index;
                yield return message => Resolve(message, path) is { } owner && RemoveRange(owner, field, at, 1);
            }
        }

        foreach (var (path, field, _) in paths.Where(static p => !p.Field.IsRepeated))
        {
            yield return message =>
            {
                if (Resolve(message, path) is not { } owner || !IsSet(owner, field)) return false;
                field.Accessor.Clear(owner);
                return true;
            };
            if (field.FieldType == FieldType.String)
            {
                yield return message =>
                {
                    if (Resolve(message, path) is not { } owner || field.Accessor.GetValue(owner) is not string { Length: > 1 } text)
                        return false;
                    field.Accessor.SetValue(owner, text[..(text.Length / 2)]);
                    return true;
                };
            }
        }
    }

    private static void Walk(IMessage message, List<(int Field, int Index)> path,
        List<(List<(int Field, int Index)> Path, FieldDescriptor Field, int Count)> paths)
    {
        foreach (var field in message.Descriptor.Fields.InDeclarationOrder())
        {
            var value = field.Accessor.GetValue(message);
            if (field.IsRepeated)
            {
                var list = (IList)value;
                if (list.Count == 0) continue;
                paths.Add((path, field, list.Count));
                if (field.FieldType != FieldType.Message) continue;
                for (var i = 0; i < list.Count; i++) Walk((IMessage)list[i]!, [.. path, (field.FieldNumber, i)], paths);
            }
            else
            {
                if (field.FieldType == FieldType.Message)
                {
                    if (value is not IMessage child) continue;
                    paths.Add((path, field, 1));
                    Walk(child, [.. path, (field.FieldNumber, -1)], paths);
                }
                else if (IsSet(message, field))
                {
                    paths.Add((path, field, 1));
                }
            }
        }
    }

    // IFieldAccessor.HasValue throws for proto3 fields without presence; a default value is an unset one.
    private static bool IsSet(IMessage message, FieldDescriptor field) => field.Accessor.GetValue(message) switch
    {
        null => false,
        string text => text.Length > 0,
        ByteString bytes => !bytes.IsEmpty,
        bool flag => flag,
        IMessage => true,
        var number => Convert.ToDouble(number, System.Globalization.CultureInfo.InvariantCulture) != 0
    };

    private static IMessage? Resolve(IMessage root, List<(int Field, int Index)> path)
    {
        var current = root;
        foreach (var (number, index) in path)
        {
            var field = current.Descriptor.FindFieldByNumber(number);
            var value = field.Accessor.GetValue(current);
            if (index < 0)
            {
                if (value is not IMessage child) return null;
                current = child;
            }
            else
            {
                if (value is not IList list || index >= list.Count) return null;
                current = (IMessage)list[index]!;
            }
        }

        return current;
    }

    private static bool RemoveRange(IMessage owner, FieldDescriptor field, int start, int count)
    {
        var list = (IList)field.Accessor.GetValue(owner);
        if (start + count > list.Count || count <= 0) return false;
        for (var i = 0; i < count; i++) list.RemoveAt(start);
        return true;
    }
}
