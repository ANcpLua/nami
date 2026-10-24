using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

// The source-generator half of dotnet/runtime#133673: a union-level [JsonNumberHandling(Strict)]
// under JsonSerializerDefaults.Web, through a JsonSerializerContext, against the reflection
// result for the same types.

var reflection = JsonSerializerOptions.Web;
Console.WriteLine($"context NumberHandling={SrcGenContext.Default.Options.NumberHandling}, naming={SrcGenContext.Default.Options.PropertyNamingPolicy}");

Run("reflection, plain", json => JsonSerializer.Deserialize<Plain>(json, reflection));
Run("reflection, strict attr", json => JsonSerializer.Deserialize<Strict>(json, reflection));
Run("source-gen, plain", json => JsonSerializer.Deserialize(json, SrcGenContext.Default.Plain));
Run("source-gen, strict attr", json => JsonSerializer.Deserialize(json, SrcGenContext.Default.Strict));

Console.WriteLine("--- serialization through the context ---");
Console.WriteLine($"ser Plain(\"x\") => {JsonSerializer.Serialize<Plain>("x", SrcGenContext.Default.Plain)}");
Console.WriteLine($"ser Strict(7) => {JsonSerializer.Serialize<Strict>(7, SrcGenContext.Default.Strict)}");

static void Run<T>(string label, Func<string, T> f) where T : struct, IUnion
{
    foreach (var json in new[] { "42", "\"hello\"", "\"42\"" })
    {
        try
        {
            var v = f(json).Value;
            Console.WriteLine($"{label,-26} {json,-8} => {v?.GetType().Name}({v})");
        }
        catch (Exception e) { Console.WriteLine($"{label,-26} {json,-8} => {e.GetType().Name}: {e.Message.Split('.')[0]}"); }
    }
}

public union Plain(int, string);

[JsonNumberHandling(JsonNumberHandling.Strict)]
public union Strict(int, string);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Plain))]
[JsonSerializable(typeof(Strict))]
public partial class SrcGenContext : JsonSerializerContext { }
