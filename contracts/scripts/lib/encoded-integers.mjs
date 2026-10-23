// A decimal interval is split by length, then by the first differing digit. No Number
// conversion: all 64 bits, including a use site's narrower bounds, survive normalization.
function decimalInterval(minimum, maximum) {
  if (minimum > maximum) return [];
  const result = [];
  function digits(low, high) {
    if (low === high) return low;
    if (low.length === 0) return "";
    if (/^0+$/u.test(low) && /^9+$/u.test(high)) return `[0-9]{${low.length}}`;
    if (low[0] === high[0]) return low[0] + digits(low.slice(1), high.slice(1));
    const tail = low.length - 1;
    const parts = [low[0] + digits(low.slice(1), "9".repeat(tail))];
    const first = Number(low[0]) + 1;
    const last = Number(high[0]) - 1;
    if (first <= last) parts.push((first === last ? String(first) : `[${first}-${last}]`) + (tail ? `[0-9]{${tail}}` : ""));
    parts.push(high[0] + digits("0".repeat(tail), high.slice(1)));
    return `(?:${parts.join("|")})`;
  }
  for (let length = minimum.toString().length; length <= maximum.toString().length; length++) {
    const low = minimum > (length === 1 ? 0n : 10n ** BigInt(length - 1)) ? minimum : length === 1 ? 0n : 10n ** BigInt(length - 1);
    const high = maximum < 10n ** BigInt(length) - 1n ? maximum : 10n ** BigInt(length) - 1n;
    if (low <= high) result.push(digits(low.toString(), high.toString()));
  }
  return result;
}

export function encodedIntegerPattern(schema, context) {
  const unsigned = schema.format === "uint64";
  let minimum = unsigned ? 0n : -(1n << 63n);
  let maximum = unsigned ? (1n << 64n) - 1n : (1n << 63n) - 1n;
  for (const [key, exclusive, lower] of [["minimum", false, true], ["exclusiveMinimum", true, true], ["maximum", false, false], ["exclusiveMaximum", true, false]]) {
    if (schema[key] === undefined) continue;
    if (!Number.isSafeInteger(schema[key])) throw new Error(`${context}.${key}: encoded integer bounds must be exact safe integers in OpenAPI.`);
    const bound = BigInt(schema[key]) + (exclusive ? (lower ? 1n : -1n) : 0n);
    if (lower && bound > minimum) minimum = bound;
    if (!lower && bound < maximum) maximum = bound;
  }
  const positive = maximum < 0n ? [] : decimalInterval(minimum > 0n ? minimum : 0n, maximum);
  const negative = minimum >= 0n ? [] : decimalInterval(maximum < 0n ? -maximum : 1n, -minimum).map((part) => `-${part}`);
  return `^(?:${[...positive, ...negative].join("|") || "(?!)"})(?![\\s\\S])`;
}

