/**
 * The MCP's generated contract module, beside the TypeScript types the emitter writes there:
 *
 * - contract-schemas.json: one JSON Schema document per tool input and output model
 *   (`Mcp.Tools.*Input`, `Mcp.Tools.*Output`) and per body the MCP reads from the Collector
 *   outside the analysis routes. Each document is ready for `fromJsonSchema(document,
 *   validator)` as it is: every `$ref` is inlined except the cycles of a recursive type, which
 *   stay in the document's own `$defs`; inheritance is flattened into one sealed object;
 *   OpenAPI-only keywords are dropped and numeric formats become bounds. What remains is plain
 *   JSON Schema that hosts without `$ref` resolution (ChatGPT) read with every type, and that Ajv
 *   compiles in strict mode.
 * - headers.ts: the request header names the OpenAPI declares.
 *
 *   node scripts/emit-mcp-schemas.mjs
 */
import { mkdir, writeFile } from "node:fs/promises";
import { encodedIntegerPattern } from "./lib/encoded-integers.mjs";
import { dirname } from "node:path";
import { csharpGeneratedDirectory, mcpHeadersPath, mcpContractSchemasPath, readOpenApi } from "./lib/paths.mjs";

const openapi = await readOpenApi();
const components = openapi.components ?? {};
const schemas = components.schemas ?? {};
const refPrefix = "#/components/schemas/";

// Keywords whose value is a schema, a map of schemas or a list of schemas. Every other keyword
// (enum, const, default, examples, required, description, ...) is data and is copied as is.
const schemaKeywords = new Set([
  "items", "additionalProperties", "unevaluatedProperties", "not", "if", "then", "else", "contains",
  "propertyNames", "contentSchema", "unevaluatedItems",
]);
const schemaMapKeywords = new Set(["properties", "patternProperties", "dependentSchemas"]);
const schemaListKeywords = new Set(["allOf", "anyOf", "oneOf", "prefixItems"]);

// OpenAPI keywords JSON Schema does not have; the variants' own constants already discriminate.
const droppedKeywords = new Set(["discriminator"]);

// Format assertions JSON Schema defines for strings and Ajv's formats implement.
const stringFormats = new Set(["date-time", "date", "time", "duration", "uri", "uri-reference", "email", "uuid", "hostname", "ipv4", "ipv6"]);
// Numeric OpenAPI formats are not JSON Schema; the fixed-width integers keep their range as bounds.
const integerRanges = new Map([
  ["int8", [-(2 ** 7), 2 ** 7 - 1]],
  ["int16", [-(2 ** 15), 2 ** 15 - 1]],
  ["int32", [-(2 ** 31), 2 ** 31 - 1]],
  ["uint8", [0, 2 ** 8 - 1]],
  ["uint16", [0, 2 ** 16 - 1]],
  ["uint32", [0, 2 ** 32 - 1]],
]);
const unboundedNumericFormats = new Set(["int64", "uint64", "float", "double", "decimal", "decimal128"]);

function compareNames(left, right) {
  return left === right ? 0 : left < right ? -1 : 1;
}

function isObject(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function refName(node) {
  if (!node.$ref.startsWith(refPrefix)) throw new Error(`Unsupported $ref ${node.$ref}: only component schemas are inlined.`);
  const name = node.$ref.slice(refPrefix.length);
  if (!(name in schemas)) throw new Error(`$ref ${node.$ref} names no component schema.`);
  return name;
}

// -- Recursive types: the strongly connected components of the reference graph ---------------

function references(node, found = new Set()) {
  if (Array.isArray(node)) {
    for (const item of node) references(item, found);
  } else if (isObject(node)) {
    if (typeof node.$ref === "string") found.add(refName(node));
    for (const [keyword, value] of Object.entries(node)) {
      if (schemaKeywords.has(keyword) || schemaListKeywords.has(keyword)) references(value, found);
      else if (schemaMapKeywords.has(keyword) && isObject(value)) for (const child of Object.values(value)) references(child, found);
    }
  }
  return found;
}

const edges = new Map(Object.entries(schemas).map(([name, schema]) => [name, references(schema)]));

/** Every definition on a reference cycle: those stay referenced, everything else is inlined. */
function recursiveDefinitions() {
  const index = new Map();
  const low = new Map();
  const stack = [];
  const onStack = new Set();
  const recursive = new Set();
  let counter = 0;
  const visit = (name) => {
    index.set(name, counter);
    low.set(name, counter);
    counter += 1;
    stack.push(name);
    onStack.add(name);
    for (const next of edges.get(name)) {
      if (!index.has(next)) {
        visit(next);
        low.set(name, Math.min(low.get(name), low.get(next)));
      } else if (onStack.has(next)) {
        low.set(name, Math.min(low.get(name), index.get(next)));
      }
    }
    if (low.get(name) !== index.get(name)) return;
    const component = [];
    for (;;) {
      const member = stack.pop();
      onStack.delete(member);
      component.push(member);
      if (member === name) break;
    }
    if (component.length > 1 || edges.get(name).has(name)) for (const member of component) recursive.add(member);
  };
  for (const name of [...edges.keys()].sort(compareNames)) if (!index.has(name)) visit(name);
  return recursive;
}

const recursive = recursiveDefinitions();

// -- Normalization -----------------------------------------------------------------------------

// JSON Schema numeric bounds do not apply to decimal strings. Encode the exact 64-bit boundary
// in a portable pattern so every validator agrees with the Collector.
/** Formats become plain JSON Schema: kept string formats, bounds for fixed-width integers. */
function normalizeFormat(schema, context) {
  const format = schema.format;
  if (format === undefined) return schema;
  const { format: _, ...rest } = schema;
  if (schema.type === "string") {
    if (stringFormats.has(format)) return schema;
    if (format === "int64" || format === "uint64") {
      const { minimum: _min, maximum: _max, exclusiveMinimum: _xmin, exclusiveMaximum: _xmax, ...text } = rest;
      return { ...text, pattern: encodedIntegerPattern(schema, context), maxLength: 20 };
    }
    throw new Error(`${context}: string format '${format}' has no JSON Schema meaning; add it to stringFormats or the contract.`);
  }
  if (schema.type === "integer" || schema.type === "number") {
    const range = integerRanges.get(format);
    if (range !== undefined) {
      return {
        ...rest,
        minimum: Math.max(range[0], schema.minimum ?? range[0]),
        maximum: Math.min(range[1], schema.maximum ?? range[1]),
      };
    }
    if (format === "int64" || format === "uint64") {
      if (!Number.isSafeInteger(schema.minimum) || !Number.isSafeInteger(schema.maximum))
        throw new Error(`${context}: numeric 64-bit values need explicit safe-integer bounds; use Common.JsonInteger or @encode(string).`);
    }
    if (unboundedNumericFormats.has(format)) return rest;
  }
  throw new Error(`${context}: format '${format}' on type ${JSON.stringify(schema.type)} is not mapped.`);
}

/** A sealing keyword's meaning as additionalProperties: `{ not: {} }` and `false` both forbid. */
function sealing(value, context) {
  if (value === false || (isObject(value) && Object.keys(value).length === 1 && isObject(value.not) && Object.keys(value.not).length === 0)) {
    return false;
  }
  if (isObject(value)) return normalize(value, context);
  throw new Error(`${context}: unsupported unevaluatedProperties ${JSON.stringify(value)}.`);
}

const definitions = new Map();

/** A component schema, normalized once and shared by every place that inlines it. */
function definition(name) {
  let normalized = definitions.get(name);
  if (normalized === undefined) {
    normalized = normalize(schemas[name], name);
    definitions.set(name, normalized);
  }
  return normalized;
}

function normalize(node, context) {
  if (!isObject(node)) throw new Error(`${context}: expected a schema object, got ${JSON.stringify(node)}.`);

  if (typeof node.$ref === "string") {
    const name = refName(node);
    const { $ref: _, ...siblings } = node;
    const overlay = normalizeKeywords(siblings, context);
    // A use site's description, default or readOnly describes this use; it wins over the type's.
    return recursive.has(name) ? { $ref: `#/$defs/${name}`, ...overlay } : { ...definition(name), ...overlay };
  }

  if (Array.isArray(node.allOf)) return flatten(node, context);

  const { unevaluatedProperties, ...rest } = node;
  const normalized = normalizeKeywords(rest, context);
  if (unevaluatedProperties !== undefined) {
    if (normalized.anyOf || normalized.oneOf) {
      throw new Error(`${context}: unevaluatedProperties beside anyOf/oneOf cannot be expressed as additionalProperties.`);
    }
    normalized.additionalProperties = sealing(unevaluatedProperties, `${context}.unevaluatedProperties`);
  }
  return normalized;
}

/** Inheritance (`allOf` of one base `$ref` plus the derived members) as one object. */
function flatten(node, context) {
  const { allOf, properties, required, unevaluatedProperties, ...rest } = node;
  const merged = { type: "object" };
  let mergedProperties = {};
  let mergedRequired = [];
  let inheritedSealing;
  for (const base of allOf) {
    if (!isObject(base) || typeof base.$ref !== "string" || Object.keys(base).length !== 1) {
      throw new Error(`${context}: allOf is only supported as inheritance from one $ref base.`);
    }
    const name = refName(base);
    if (recursive.has(name)) throw new Error(`${context}: inherits from recursive ${name}.`);
    const inherited = definition(name);
    if (inherited.type !== "object" || inherited.anyOf || inherited.oneOf) {
      throw new Error(`${context}: base ${name} is not a plain object schema.`);
    }
    mergedProperties = { ...mergedProperties, ...inherited.properties };
    mergedRequired = [...mergedRequired, ...(inherited.required ?? [])];
    inheritedSealing = inherited.additionalProperties;
  }
  // The derivative documents and seals itself; the base's description and sealing describe the base.
  Object.assign(merged, normalizeKeywords(rest, context));
  merged.properties = { ...mergedProperties, ...normalizeKeywords({ properties: properties ?? {} }, context).properties };
  const allRequired = [...new Set([...mergedRequired, ...(required ?? [])])];
  if (allRequired.length > 0) merged.required = allRequired;
  if (unevaluatedProperties !== undefined) merged.additionalProperties = sealing(unevaluatedProperties, `${context}.unevaluatedProperties`);
  else if (inheritedSealing !== undefined) merged.additionalProperties = inheritedSealing;
  return merged;
}

function normalizeKeywords(node, context) {
  const result = {};
  for (const [keyword, value] of Object.entries(node)) {
    if (droppedKeywords.has(keyword)) continue;
    if (keyword === "additionalProperties" && isObject(value) && Object.keys(value).length === 0) {
      result.additionalProperties = true;
    } else if (schemaKeywords.has(keyword) && isObject(value)) {
      result[keyword] = normalize(value, `${context}.${keyword}`);
    } else if (schemaMapKeywords.has(keyword) && isObject(value)) {
      result[keyword] = Object.fromEntries(
        Object.entries(value).map(([name, child]) => [name, normalize(child, `${context}.${name}`)]),
      );
    } else if (schemaListKeywords.has(keyword) && Array.isArray(value)) {
      result[keyword] = value.map((child, index) => normalize(child, `${context}.${keyword}[${index}]`));
    } else {
      result[keyword] = value;
    }
  }
  if (result.contentEncoding === "base64") {
    // RFC 4648 canonical padding and pad bits. Both runtimes validate this before
    // the C# bytes converter can silently normalize a non-canonical spelling.
    result.pattern = "^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/][AQgw]==|[A-Za-z0-9+/]{2}[AEIMQUYcgkosw048]=)?(?![\\s\\S])";
  }
  return normalizeFormat(result, context);
}

/** The recursive definitions a document references, directly or through one another. */
function referencedDefinitions(node, found = new Set()) {
  if (Array.isArray(node)) {
    for (const item of node) referencedDefinitions(item, found);
  } else if (isObject(node)) {
    if (typeof node.$ref === "string") {
      const name = node.$ref.slice("#/$defs/".length);
      if (!found.has(name)) {
        found.add(name);
        referencedDefinitions(definition(name), found);
      }
    }
    for (const [keyword, value] of Object.entries(node)) {
      if (keyword !== "$ref") referencedDefinitions(value, found);
    }
  }
  return found;
}

function document(name) {
  const body = definition(name);
  const needed = [...referencedDefinitions(body)].sort(compareNames);
  return needed.length === 0
    ? body
    : { ...body, $defs: Object.fromEntries(needed.map((definitionName) => [definitionName, definition(definitionName)])) };
}

const allDocuments = Object.fromEntries(Object.keys(schemas).sort(compareNames).map((name) => [name, document(name)]));
const assertions = new Set(["$ref", "$defs", "type", "properties", "required", "additionalProperties", "items", "enum", "const", "anyOf", "oneOf", "allOf", "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "minLength", "maxLength", "minItems", "maxItems", "pattern"]);
const annotations = new Set(["description", "title", "default", "examples", "readOnly", "writeOnly", "deprecated", "contentEncoding"]);
function checkSupported(node) {
  if (!isObject(node)) return;
  for (const [key, value] of Object.entries(node)) {
    if (!assertions.has(key) && !annotations.has(key)) throw new Error(`C# contract validator does not implement '${key}'. Define a portable TypeSpec constraint before emitting it.`);
    if (schemaMapKeywords.has(key) || key === "$defs") Object.values(value).forEach(checkSupported);
    else if (schemaListKeywords.has(key)) value.forEach(checkSupported);
    else if (schemaKeywords.has(key)) checkSupported(value);
  }
}
Object.values(allDocuments).forEach(checkSupported);
await mkdir(`${csharpGeneratedDirectory}/Api/Contracts`, { recursive: true });
await writeFile(`${csharpGeneratedDirectory}/Api/Contracts/contract-schemas.json`, `${JSON.stringify(allDocuments)}\n`);

await mkdir(dirname(mcpContractSchemasPath), { recursive: true });
await writeFile(mcpContractSchemasPath, `${JSON.stringify(allDocuments)}\n`);

// -- Header names --------------------------------------------------------------------------------

function screamingSnake(name) {
  return name.replace(/([a-z0-9])([A-Z])/gu, "$1_$2").toUpperCase();
}

const headers = new Map();
function addHeader(constant, value, description) {
  if (headers.has(constant)) throw new Error(`Header constant ${constant} is declared twice.`);
  headers.set(constant, { value, description });
}
for (const [name, scheme] of Object.entries(components.securitySchemes ?? {}).sort(([a], [b]) => compareNames(a, b))) {
  if (scheme.type === "apiKey" && scheme.in === "header") {
    addHeader(`${screamingSnake(name)}_HEADER`, scheme.name, `Header of the \`${name}\` security scheme.`);
  }
}
for (const [name, parameter] of Object.entries(components.parameters ?? {}).sort(([a], [b]) => compareNames(a, b))) {
  if (parameter.in === "header") {
    const shortName = name.slice(name.lastIndexOf(".") + 1);
    addHeader(screamingSnake(shortName.endsWith("Header") ? shortName : `${shortName}Header`), parameter.name, `Header of the \`${name}\` parameter.`);
  }
}

await writeFile(
  mcpHeadersPath,
  "// <auto-generated/>\n// Copyright (c) 2025-2026 ancplua\n\n" +
    [...headers].map(([constant, { value, description }]) =>
      `/** ${description} */\nexport const ${constant} = ${JSON.stringify(value)};\n`).join("\n"),
);

console.log(`emit-mcp-schemas: ${Object.keys(allDocuments).length} documents -> ${mcpContractSchemasPath}; ${headers.size} header names -> ${mcpHeadersPath}`);
