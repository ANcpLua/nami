import type { Model, ModelProperty, Program, Union } from "@typespec/compiler";
import { getDiscriminatedUnion, getDiscriminator, isArrayModelType, resolveEncodedName, walkPropertiesInherited } from "@typespec/compiler";
import { mapType, type MixedUnionShape } from "./type-map.js";
import { resolveProperty } from "./semantics.js";

const quote = (value: string): string => JSON.stringify(value);
const pascal = (value: string): string => value[0]!.toUpperCase() + value.slice(1);
type WriterCase = { type: string; name: string; body: string };

/** A switch expression on the closed hierarchy makes a new leaf invalidate both writers. */
function writer(method: string, type: string, cases: WriterCase[], json = false, nullable = true): string {
  const options = json ? ", JsonSerializerOptions options" : "";
  const argument = json ? ", options" : "";
  return `    private static void ${method}(Utf8JsonWriter writer, ${type}${nullable ? "?" : ""} value${options})
    {
        _ = value switch
        {
${cases.map(c => `            ${c.type} typed => ${method}${c.name}(writer, typed${argument}),`).join("\n")}
            null => ${nullable ? "WriteNull(writer)" : 'throw new InvalidOperationException("The tagged value was not initialized.")'},
        };
    }
${cases.map(c => `    private static bool ${method}${c.name}(Utf8JsonWriter writer, ${c.type} typed${options})
    {
        ${c.body}
        return true;
    }`).join("\n")}`;
}

/** Primitive JSON alternatives and one tagged object, shared by wire and storage outputs. */
export function renderMixedUnion(program: Program, union: Union, shape: MixedUnionShape, visibility = "public"): string {
  const name = union.name!;
  const objectType = mapType(program, shape.objectMember!.type);
  const object = shape.objectMember!.type;
  const modelDiscriminator = object.kind === "Model" ? getDiscriminator(program, object) : undefined;
  const elementType = `${name}${shape.hasNull ? "?" : ""}`;
  const declarations: string[] = [];
  const reads: string[] = [];
  const json: WriterCase[] = [];
  const stable: WriterCase[] = [];
  for (const [key, variant] of union.variants) {
    const type = variant.type;
    if (type.kind === "Intrinsic" && type.name === "null") continue;
    if (typeof key !== "string") throw new Error(`${union.name} requires named alternatives`);
    const caseName = pascal(key);
    const array = type.kind === "Model" && isArrayModelType(type);
    const primitive = type.kind === "Scalar" ? mapType(program, type) : undefined;
    const caseType = array ? `IReadOnlyList<${elementType}>` : primitive ?? objectType;
    declarations.push(`    public sealed class ${caseName}(${caseType} value) : ${name}
    {
        public ${caseType} Value { get; } = value;
    }`);
    let read: string, write: string, canonical: string;
    if (primitive === "string" || primitive === "bool" || primitive === "double") {
      const suffix = primitive === "string" ? "String" : primitive === "bool" ? "Boolean" : "Number";
      const tokens = primitive === "bool" ? "case JsonTokenType.True: case JsonTokenType.False:" : `case JsonTokenType.${suffix}:`;
      read = `${tokens} return new ${name}.${caseName}(reader.Get${primitive === "double" ? "Double" : suffix}()${primitive === "string" ? "!" : ""});`;
      write = `writer.Write${suffix}Value(typed.Value);`;
      canonical = primitive === "double" ? "WriteJsNumber(writer, typed.Value);" : write;
    } else if (array) {
      read = `case JsonTokenType.StartArray:
            {
                var items = new List<${elementType}>();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray) return new ${name}.${caseName}(items);
                    items.Add(Read(ref reader, typeToConvert, options)${shape.hasNull ? "" : " ?? throw new JsonException(\"Null array item.\")"});
                }
                throw new JsonException("Unterminated ${name} array.");
            }`;
      write = "writer.WriteStartArray(); foreach (var item in typed.Value) WriteJson(writer, item, options); writer.WriteEndArray();";
      canonical = "writer.WriteStartArray(); foreach (var item in typed.Value) WriteStable(writer, item); writer.WriteEndArray();";
    } else if (type === shape.objectMember!.type) {
      read = modelDiscriminator ? `case JsonTokenType.StartObject:
            {
                using var document = JsonDocument.ParseValue(ref reader);
                if (!document.RootElement.TryGetProperty(${quote(modelDiscriminator.propertyName)}, out var tag) || tag.ValueKind != JsonValueKind.String)
                    throw new JsonException("${object.name} requires ${modelDiscriminator.propertyName}.");
                return new ${name}.${caseName}(document.RootElement.Deserialize(options.GetTypeInfo<${objectType}>()) ?? throw new JsonException("Null object case."));
            }` : `case JsonTokenType.StartObject: return new ${name}.${caseName}(JsonSerializer.Deserialize(ref reader, options.GetTypeInfo<${objectType}>()));`;
      write = `JsonSerializer.Serialize(writer, typed.Value, options.GetTypeInfo<${objectType}>());`;
      canonical = "WriteStableObject(writer, typed.Value);";
    } else throw new Error(`Unsupported mixed union alternative ${name}.${key}`);
    reads.push(read);
    json.push({ type: `${name}.${caseName}`, name: caseName, body: write });
    stable.push({ type: `${name}.${caseName}`, name: caseName, body: canonical });
  }

  let tagProperty: string;
  let variants: [string, Model][];
  if (object.kind === "Union") {
    const [tagged] = getDiscriminatedUnion(program, object);
    if (!tagged) throw new Error(`${object.name} needs a discriminator`);
    tagProperty = tagged.options.discriminatorPropertyName;
    variants = [...tagged.variants].map(([tag, model]) => {
      if (model.kind !== "Model") throw new Error(`${object.name}.${tag} must be a model`);
      return [tag, model];
    });
  } else {
    const discriminator = getDiscriminator(program, object);
    if (!discriminator) throw new Error(`${object.name} needs a discriminator`);
    tagProperty = discriminator.propertyName;
    const leaves = (model: Model): Model[] => model.derivedModels.length ? model.derivedModels.flatMap(leaves) : [model];
    variants = leaves(object).map(model => {
      const tag = [...walkPropertiesInherited(model)].find(p => p.name === tagProperty)?.type;
      if (tag?.kind !== "String") throw new Error(`${model.name} needs a literal ${tagProperty}`);
      return [tag.value, model];
    });
  }
  const objects = variants.map(([tag, model], index): WriterCase => {
    const entries = [{ key: tagProperty, write: `writer.WriteStringValue(${quote(tag)});` }];
    for (const property of walkPropertiesInherited(model)) {
      const key = resolveEncodedName(program, property, "application/json");
      if (key === tagProperty) continue;
      entries.push({ key, write: stableProperty(program, property, `typed.${pascal(property.name)}`) });
    }
    entries.sort((a, b) => a.key < b.key ? -1 : a.key > b.key ? 1 : 0);
    return { type: mapType(program, model), name: `Case${index}`, body: `writer.WriteStartObject();\n${entries.map(e => `        writer.WritePropertyName(${quote(e.key)}); ${e.write}`).join("\n")}\n        writer.WriteEndObject();` };
  });
  return `[JsonConverter(typeof(${name}JsonConverter))]
${visibility} closed class ${name}
{
    private protected ${name}() { }
${declarations.join("\n")}
    public string ToStableString()
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            WriteStable(writer, this);
        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
    public static string StableString(${name}? value) => value is null ? "null" : value.ToStableString();
    internal static void Serialize(Utf8JsonWriter writer, ${name} value, JsonSerializerOptions options) => WriteJson(writer, value, options);
    private static bool WriteNull(Utf8JsonWriter writer) { writer.WriteNullValue(); return true; }
${writer("WriteJson", name, json, true)}
${writer("WriteStable", name, stable)}
${writer("WriteStableObject", objectType, objects, false, false)}
${ecmaScriptNumber}
}
${visibility} sealed class ${name}JsonConverter : JsonConverter<${name}>
{
    public override ${name}? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null: ${shape.hasNull ? "return null;" : 'throw new JsonException("Null is not allowed.");'}
${reads.map(read => `            ${read}`).join("\n")}
            default: throw new JsonException($"${name} cannot start with token '{reader.TokenType}'.");
        }
    }
    public override void Write(Utf8JsonWriter writer, ${name} value, JsonSerializerOptions options) => ${name}.Serialize(writer, value, options);
}
`;
}

function stableProperty(program: Program, property: ModelProperty, expression: string): string {
  const type = mapType(program, property.type);
  if (type === "string") return `writer.WriteStringValue(${expression});`;
  if (type === "bool") return `writer.WriteBooleanValue(${expression});`;
  if (["long", "int", "short", "ulong", "uint"].includes(type)) {
    const text = `${expression}.ToString(System.Globalization.CultureInfo.InvariantCulture)`;
    return resolveProperty(program, property).encoding?.type.name === "string" ? `writer.WriteStringValue(${text});` : `writer.WriteRawValue(${text}, skipInputValidation: true);`;
  }
  if (type === "double" || type === "float") return `WriteJsNumber(writer, ${expression});`;
  if (type === "ReadOnlyMemory<byte>") return `writer.WriteStringValue(Convert.ToBase64String(${expression}.Span));`;
  if (type.startsWith("IReadOnlyDictionary<string, ")) return `writer.WriteStartObject(); foreach (var (key, nested) in ${expression}.OrderBy(static item => item.Key, StringComparer.Ordinal)) { writer.WritePropertyName(key); WriteStable(writer, nested); } writer.WriteEndObject();`;
  if (type.startsWith("IReadOnlyList<")) return `writer.WriteStartArray(); foreach (var item in ${expression}) WriteStable(writer, item); writer.WriteEndArray();`;
  throw new Error(`No canonical writer for ${property.name}: ${type}`);
}

const ecmaScriptNumber = `
    /// <summary>A double as ECMAScript Number::toString prints it, so both languages agree on identity.</summary>
    private static void WriteJsNumber(Utf8JsonWriter writer, double value)
    {
        if (double.IsNaN(value)) { writer.WriteStringValue("NaN"); return; }
        if (double.IsPositiveInfinity(value)) { writer.WriteStringValue("Infinity"); return; }
        if (double.IsNegativeInfinity(value)) { writer.WriteStringValue("-Infinity"); return; }
        if (value == 0) { writer.WriteRawValue("0", skipInputValidation: true); return; }
        var negative = value < 0;
        var round = Math.Abs(value).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        var exponentIndex = round.IndexOfAny(['E', 'e']);
        var mantissa = exponentIndex < 0 ? round : round[..exponentIndex];
        var exponent = exponentIndex < 0 ? 0 : int.Parse(round[(exponentIndex + 1)..], System.Globalization.CultureInfo.InvariantCulture);
        var pointIndex = mantissa.IndexOf('.');
        var integerPart = pointIndex < 0 ? mantissa : mantissa[..pointIndex];
        var fraction = pointIndex < 0 ? "" : mantissa[(pointIndex + 1)..];
        var digits = (integerPart + fraction).TrimStart('0');
        var n = integerPart.TrimStart('0').Length + exponent;
        if (integerPart.TrimStart('0').Length == 0)
        {
            var leadingZeros = fraction.Length - fraction.TrimStart('0').Length;
            n = -leadingZeros + exponent;
        }

        digits = digits.TrimEnd('0');
        var k = digits.Length;
        string text;
        if (k <= n && n <= 21) text = digits + new string('0', n - k);
        else if (0 < n && n <= 21) text = digits[..n] + "." + digits[n..];
        else if (-6 < n && n <= 0) text = "0." + new string('0', -n) + digits;
        else
        {
            var e = n - 1;
            var sign = e >= 0 ? "+" : "-";
            text = (k == 1 ? digits : digits[..1] + "." + digits[1..]) + "e" + sign + Math.Abs(e).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        writer.WriteRawValue((negative ? "-" : "") + text, skipInputValidation: true);
    }
`;
