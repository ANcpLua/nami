using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nami.Tests;

/// <summary>Generated metadata for the harness's telemetry archives and lifecycle evidence.</summary>
[JsonSourceGenerationOptions(WriteIndented = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    Converters = [typeof(TelemetryValueJsonConverter)])]
[JsonSerializable(typeof(TelemetrySnapshot))]
[JsonSerializable(typeof(NumberPoint))]
internal sealed partial class TelemetryJsonContext : JsonSerializerContext;

/// <summary>Writes the finite OTLP value model without inspecting arbitrary CLR members.</summary>
internal sealed class TelemetryValueJsonConverter : JsonConverter<object>
{
    public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.Clone();
    }

    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        WriteValue(writer, value);
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case JsonElement element: element.WriteTo(writer); break;
            case string text: writer.WriteStringValue(text); break;
            case bool boolean: writer.WriteBooleanValue(boolean); break;
            case byte[] bytes: writer.WriteBase64StringValue(bytes); break;
            case byte number: writer.WriteNumberValue(number); break;
            case sbyte number: writer.WriteNumberValue(number); break;
            case short number: writer.WriteNumberValue(number); break;
            case ushort number: writer.WriteNumberValue(number); break;
            case int number: writer.WriteNumberValue(number); break;
            case uint number: writer.WriteNumberValue(number); break;
            case long number: writer.WriteNumberValue(number); break;
            case ulong number: writer.WriteNumberValue(number); break;
            case decimal number: writer.WriteNumberValue(number); break;
            case float number:
                if (float.IsFinite(number)) writer.WriteNumberValue(number);
                else WriteDouble(writer, number);
                break;
            case double number: WriteDouble(writer, number); break;
            case IReadOnlyDictionary<string, object?> dictionary:
                writer.WriteStartObject();
                foreach (var (key, item) in dictionary)
                {
                    writer.WritePropertyName(key);
                    WriteValue(writer, item);
                }

                writer.WriteEndObject();
                break;
            case IEnumerable items:
                writer.WriteStartArray();
                foreach (var item in items) WriteValue(writer, item);
                writer.WriteEndArray();
                break;
            default: throw new JsonException($"Unsupported telemetry value type: {value.GetType().FullName}");
        }
    }

    private static void WriteDouble(Utf8JsonWriter writer, double value)
    {
        if (double.IsFinite(value)) writer.WriteNumberValue(value);
        else
        {
            writer.WriteStringValue(double.IsNaN(value) ? "NaN" :
                double.IsPositiveInfinity(value) ? "Infinity" : "-Infinity");
        }
    }
}
