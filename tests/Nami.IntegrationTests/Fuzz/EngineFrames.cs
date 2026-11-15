using System.Globalization;
using System.Text;
using System.Text.Json;
using Nami.Api.Contracts;

namespace Nami.IntegrationTests.Fuzz;

/// <summary>One frame the TypeScript host writes to the engine, and whether the engine owes it an answer.</summary>
internal sealed record EngineFrame(string Id, string Text, bool ExpectsReply, string Note);

/// <summary>
///     Frames exactly as <c>engine.ts</c> encodes them: <c>JSON.stringify(EngineCommandJson.toJson(request))</c> and a
///     newline. The tool arguments are the agent's, re-serialized the way JSON.parse and JSON.stringify would: duplicate
///     keys collapse, non-finite numbers become null, and a lone surrogate is written as a <c>\udXXX</c> escape. The
///     traceparent and tracestate are the agent's <c>_meta</c> strings, forwarded unchecked.
/// </summary>
internal sealed class EngineFrameMutator(Random random)
{
    private static readonly string?[] s_traceContext =
    [
        null, "", "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01", "00-00000000000000000000000000000000-0000000000000000-00",
        "ff-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01", "garbage", "\uD800", "a\uDFFF", "😀",
        "vendor=value,other=\u0000", new string('t', 70_000)
    ];

    private int _sequence;

    internal EngineFrame Call(ToolCase input)
    {
        var id = Next();
        var traceparent = s_traceContext[random.Next(s_traceContext.Length)];
        var tracestate = random.Next(3) == 0 ? s_traceContext[random.Next(s_traceContext.Length)] : null;
        var arguments = JsStringify(input.Json);
        var text = $"{{\"id\":{JsString(id)},\"revision\":{JsString(ContractRevision.Value)},\"method\":\"call\",\"name\":{JsString(input.Tool)}," +
                   $"\"input\":{arguments},\"credential\":null,\"traceparent\":{JsNullable(traceparent)},\"tracestate\":{JsNullable(tracestate)}}}";
        return new EngineFrame(id, text, true,
            $"{input.Tool} {input.Note}; traceparent {Describe(traceparent)}; tracestate {Describe(tracestate)}");
    }

    internal EngineFrame Health()
    {
        var id = Next();
        return new EngineFrame(id, $"{{\"id\":{JsString(id)},\"revision\":{JsString(ContractRevision.Value)},\"method\":\"health\"}}", true, "health");
    }

    internal static EngineFrame Cancel(string id) =>
        new(id, $"{{\"id\":{JsString(id)},\"revision\":{JsString(ContractRevision.Value)},\"method\":\"cancel\"}}", false, "cancel " + id);

    private string Next() => (++_sequence).ToString(CultureInfo.InvariantCulture);

    private static string Describe(string? value) => value switch
    {
        null => "null",
        { Length: > 32 } => $"{value.Length} chars",
        _ => string.Concat(value.Select(static c => c is < ' ' or >= '\uD800' and <= '\uDFFF' ? $"\\u{(int)c:x4}" : c.ToString()))
    };

    private static string JsNullable(string? value) => value is null ? "null" : JsString(value);

    /// <summary>JSON.stringify of a string: well-formed, so a lone surrogate becomes an escape.</summary>
    internal static string JsString(string value)
    {
        var builder = new StringBuilder(value.Length + 2).Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                case < ' ':
                    builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}");
                    break;
                default:
                    if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                    {
                        builder.Append(c).Append(value[++i]);
                    }
                    else if (char.IsSurrogate(c))
                    {
                        builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}");
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.Append('"').ToString();
    }

    /// <summary>What JSON.stringify(JSON.parse(text)) yields; read token by token, so a lone surrogate survives.</summary>
    internal static string JsStringify(string json)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json), new JsonReaderOptions { MaxDepth = 100_000 });
        reader.Read();
        var builder = new StringBuilder(json.Length);
        Write(ref reader, builder);
        return builder.ToString();
    }

    private static void Write(ref Utf8JsonReader reader, StringBuilder builder)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
                // JSON.parse keeps a repeated key at its first position with its last value.
                var members = new List<(string Name, string Value)>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var name = Text(ref reader);
                    reader.Read();
                    var value = new StringBuilder();
                    Write(ref reader, value);
                    var index = members.FindIndex(member => member.Name == name);
                    if (index >= 0) members[index] = (name, value.ToString());
                    else members.Add((name, value.ToString()));
                }

                builder.Append('{').AppendJoin(',', members.Select(static member => JsString(member.Name) + ":" + member.Value))
                    .Append('}');
                break;
            case JsonTokenType.StartArray:
                builder.Append('[');
                var first = true;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (!first) builder.Append(',');
                    first = false;
                    Write(ref reader, builder);
                }

                builder.Append(']');
                break;
            case JsonTokenType.String:
                builder.Append(JsString(Text(ref reader)));
                break;
            case JsonTokenType.Number:
                var number = double.Parse(Encoding.UTF8.GetString(reader.ValueSpan), NumberStyles.Float, CultureInfo.InvariantCulture);
                builder.Append(double.IsFinite(number) ? JsNumber(number) : "null");
                break;
            default:
                builder.Append(Encoding.UTF8.GetString(reader.ValueSpan));
                break;
        }
    }

    private static string Text(ref Utf8JsonReader reader) => Unescape(Encoding.UTF8.GetString(reader.ValueSpan));

    private static string JsNumber(double value)
    {
        if (value == 0) return "0";
        if (double.IsInteger(value) && Math.Abs(value) < 1e21)
            return value.ToString("F0", CultureInfo.InvariantCulture);
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Replace("E+", "e+", StringComparison.Ordinal).Replace("E-", "e-", StringComparison.Ordinal);
    }

    /// <summary>Decodes JSON string escapes and keeps a lone surrogate as the UTF-16 unit it names.</summary>
    internal static string Unescape(string escaped)
    {
        var builder = new StringBuilder(escaped.Length);
        for (var i = 0; i < escaped.Length; i++)
        {
            if (escaped[i] != '\\')
            {
                builder.Append(escaped[i]);
                continue;
            }

            var c = escaped[++i];
            builder.Append(c switch
            {
                'b' => '\b',
                'f' => '\f',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                'u' => (char)int.Parse(escaped.AsSpan(i + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture),
                _ => c
            });
            if (c == 'u') i += 4;
        }

        return builder.ToString();
    }
}
