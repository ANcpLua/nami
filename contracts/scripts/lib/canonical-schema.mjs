const presentationKeys = new Set(["description", "example", "examples", "externalDocs", "summary", "title"]);
const schemaMaps = new Set(["properties", "patternProperties", "$defs", "schemas", "dependentSchemas"]);

/** Remove annotations, never fields merely named description, title or summary. */
export function canonicalize(value, namesAreData = false) {
  if (Array.isArray(value)) return value.map((item) => canonicalize(item));
  if (typeof value !== "object" || value === null) return value;
  return Object.fromEntries(Object.keys(value)
    .filter((key) => namesAreData || !presentationKeys.has(key))
    .sort()
    .map((key) => [key, canonicalize(value[key], !namesAreData && schemaMaps.has(key))]));
}
