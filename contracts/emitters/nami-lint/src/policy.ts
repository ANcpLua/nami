// =============================================================================
// The nami wire-naming policy, in one place.
// =============================================================================
// Every rule in this library reads its definition of "correct" from here, so
// the convention exists exactly once and cannot drift between rules.
// =============================================================================

import type { Model, ModelProperty, Namespace, Scalar, Type } from "@typespec/compiler";
import { isArrayModelType } from "@typespec/compiler";

/** Namespace that owns the product contract. Rules ignore everything outside it. */
export const PRODUCT_NAMESPACE = "Nami.Api.Contracts";

/** Namespace declaring the shared identity scalars. */
export const COMMON_NAMESPACE = "Nami.Api.Contracts.Common";

/** The single legal shape of a JSON body property name and of a query/path parameter name. */
export const SNAKE_CASE = /^[a-z][a-z0-9]*(_[a-z0-9]+)*$/;

/**
 * camelCase -> snake_case, the only casing transform in the contract.
 *
 * `traceId` -> `trace_id`, `p95DurationMs` -> `p95_duration_ms`,
 * `k8sPodUid` -> `k8s_pod_uid`, `wwwAuthenticate` -> `www_authenticate`.
 */
export function snakeCase(name: string): string {
  return name
    .replace(/([a-z0-9])([A-Z])/g, "$1_$2")
    .replace(/([A-Z]+)([A-Z][a-z])/g, "$1_$2")
    .toLowerCase();
}

// =============================================================================
// Wire-name derivation
// =============================================================================
// A wire name is `snakeCase(propertyName)` unless it appears below. Deriving it
// is what makes casing a non-decision: there is no per-property judgement call
// to get wrong, and a reviewer never has to ask whether a given spelling was
// intended. The exceptions are the cases where the wire name genuinely differs
// from the property name — never a casing fix, which the derivation already
// covers.

/** `Model.property` -> the wire name it ships, with the reason it differs. */
export interface WireNameException {
  readonly wire: string;
  readonly reason: string;
}

export const WIRE_NAME_EXCEPTIONS: Readonly<Record<string, WireNameException>> = {
  // The TypeSpec names are prefixed only to stay unique inside the model; on the
  // wire the object already *is* the scope, so OTLP names the fields bare.
  "InstrumentationScope.scopeName": { wire: "name", reason: "OTLP InstrumentationScope.name" },
  "InstrumentationScope.scopeVersion": { wire: "version", reason: "OTLP InstrumentationScope.version" },
  "InstrumentationScope.scopeAttributes": { wire: "attributes", reason: "OTLP InstrumentationScope.attributes" },
  // GenAI is one token in semantic conventions, so it does not split.
  "SessionStats.sessionsWithGenAi": { wire: "sessions_with_genai", reason: "'genai' is a single semconv token" },
};

/** The wire name a property must ship: derived, or the registered exception. */
export function expectedWireName(modelName: string, propertyName: string): string {
  return WIRE_NAME_EXCEPTIONS[`${modelName}.${propertyName}`]?.wire ?? snakeCase(propertyName);
}

// =============================================================================
// Identity model
// =============================================================================
// A nami identity is a scalar plus the property name it is always spelled with.
// Declaring both directions is the point: it makes "same value, different key"
// unrepresentable rather than merely discouraged.

export interface IdentityBinding {
  /** Scalar declared in `Nami.Api.Contracts.Common`. */
  readonly scalar: string;
  /** The property name a single value of this identity uses when it is the subject. */
  readonly singular: string;
  /** The property name a collection of this identity uses when it is the subject. */
  readonly plural: string;
  /**
   * Names reserved for this identity: a property called one of these must be
   * typed as the scalar. This is an exact list, never an `endsWith("Id")` test —
   * the contract has other `*Id` properties (ResourceId, FailureId, …) that are
   * their own types and must not be captured.
   */
  readonly reserved: readonly string[];
  /**
   * Qualified names that carry this identity while naming a *relationship* to it
   * rather than the subject itself — `parentSpanId`, `previousSessionId`. Each
   * one is registered here with the relationship it expresses.
   *
   * This is a closed list on purpose. A suffix test (`endsWith("TraceId")`)
   * would silently admit `candidateTraceId`, `rootTraceId`, and every future
   * coinage — which is how three spellings of one identity appeared in the first
   * place. A new edge is a deliberate one-line entry, not an emergent property.
   */
  readonly edges: Readonly<Record<string, string>>;
}

export const IDENTITIES: readonly IdentityBinding[] = [
  {
    scalar: "TraceId",
    singular: "traceId",
    plural: "traceIds",
    reserved: ["traceId", "traceIds"],
    edges: {
      exampleTraceId: "one bounded evidence example trace",
      exampleTraceIds: "bounded evidence example traces",
      nearestSuccessTraceId: "the closest successful execution used for comparison",
      exemplarTraceIds: "metric exemplar traces",
      historicalExemplarTraceIds: "trace links retained without complete original metric exemplars",
      recurrences: "bounded trace evidence for failure recurrences",
    },
  },
  {
    scalar: "SpanId",
    singular: "spanId",
    plural: "spanIds",
    reserved: ["spanId", "spanIds", "parentSpanId"],
    // The OTLP parent link; absent on a root span.
    edges: {
      parentSpanId: "the span this span is a child of",
      missingParentSpanIds: "parents referenced but absent from storage",
      aSpanId: "execution a's span in a trace comparison",
      bSpanId: "execution b's span in a trace comparison",
    },
  },
  {
    scalar: "SessionId",
    singular: "sessionId",
    plural: "sessionIds",
    reserved: ["sessionId", "sessionIds", "previousSessionId"],
    edges: {
      // Session continuity across a reconnect.
      previousSessionId: "the session this one continues from",
      // A CI workflow run is emitted as one session; `ci_log` and
      // `get_failure_context` name it for the agent in the run's own vocabulary
      // while keeping the session identity.
      ciRunId: "the CI workflow run whose telemetry is grouped under this session",
      baselineCiRunId: "the CI run a ci_log comparison compares with",
    },
  },
  {
    scalar: "UserId",
    singular: "userId",
    plural: "userIds",
    reserved: ["userId", "userIds"],
    edges: {},
  },
];

/** Property name -> the identity binding and arity that name is reserved for. */
export interface ReservedName {
  readonly identity: IdentityBinding;
  readonly array: boolean;
}

const RESERVED_NAMES = new Map<string, ReservedName>();
for (const identity of IDENTITIES) {
  for (const name of identity.reserved) {
    RESERVED_NAMES.set(name, { identity, array: name === identity.plural });
  }
}

/**
 * Names that are reserved for an identity scalar. A property carrying one of
 * these names must be typed as that scalar — never as a bare `string`.
 */
export function reservedNameFor(propertyName: string): ReservedName | undefined {
  return RESERVED_NAMES.get(propertyName);
}

const EDGE_NAMES = new Map<string, IdentityBinding>();
for (const identity of IDENTITIES) {
  for (const edge of Object.keys(identity.edges)) {
    EDGE_NAMES.set(edge, identity);
  }
}

/**
 * The identity a registered edge name qualifies, if the name is an edge.
 *
 * `reserved` and `edges` overlap but are not the same list, and the gap is
 * load-bearing. `previousSessionId` is in both: it can only ever be a
 * `SessionId`, so the reserved check pins the exact scalar. `ciRunId` is an edge
 * only, because the same edge legitimately qualifies different identities —
 * `SessionId` on the ci_log shapes, and another run identity could be declared
 * elsewhere. Which of those is right is a modelling decision no lint rule can make;
 * that it is a validated scalar at all is not.
 */
export function edgeNameFor(propertyName: string): IdentityBinding | undefined {
  return EDGE_NAMES.get(propertyName);
}

/**
 * Whether `type` is a scalar this contract declares, rather than a TypeSpec
 * built-in.
 *
 * A declared scalar is where `@pattern`, `@minLength` and `@maxLength` live, so
 * this is the difference between a run id that is checked at the boundary and
 * one that accepts any string at all.
 */
export function isDeclaredScalar(type: Type): boolean {
  if (type.kind !== "Scalar") return false;
  let outermost: string | undefined;
  for (let ns = (type as Scalar).namespace; ns?.name; ns = ns.namespace) {
    outermost = ns.name;
  }
  return outermost !== undefined && outermost !== "TypeSpec";
}

/**
 * Whether a property carrying this identity may be spelled `name`.
 *
 * Exactly three spellings are legal: the bare token (`traceId`), its plural
 * (`traceIds`), or a registered edge (`parentSpanId`). Nothing is inferred.
 */
export function isAllowedIdentityName(
  identity: IdentityBinding,
  array: boolean,
  name: string,
): boolean {
  if (name === (array ? identity.plural : identity.singular)) return true;
  return Object.hasOwn(identity.edges, name);
}

/** How the allowed spelling reads in a diagnostic. */
export function describeAllowedNames(identity: IdentityBinding, array: boolean): string {
  const subject = `'${array ? identity.plural : identity.singular}'`;
  const edges = Object.keys(identity.edges);
  return edges.length === 0
    ? subject
    : `${subject} or a registered edge (${edges.map((edge) => `'${edge}'`).join(", ")})`;
}

// =============================================================================
// Type inspection
// =============================================================================

/** True when `type` is nested anywhere inside the named namespace. */
export function isInNamespace(type: Type, fullName: string): boolean {
  const segments = fullName.split(".");
  for (let ns = namespaceOf(type); ns; ns = ns.namespace) {
    if (matchesFrom(ns, segments)) return true;
  }
  return false;
}

function matchesFrom(leaf: Namespace, segments: readonly string[]): boolean {
  let cursor: Namespace | undefined = leaf;
  for (let i = segments.length - 1; i >= 0; i--) {
    if (!cursor || cursor.name !== segments[i]) return false;
    cursor = cursor.namespace;
  }
  return true;
}

function namespaceOf(type: Type): Namespace | undefined {
  if (type.kind === "Namespace") return type;
  if (type.kind === "ModelProperty") {
    return type.model ? namespaceOf(type.model) : undefined;
  }
  return "namespace" in type ? (type.namespace as Namespace | undefined) : undefined;
}

export interface ResolvedIdentity {
  readonly identity: IdentityBinding;
  readonly array: boolean;
}

/**
 * The shared identity scalar a property carries, if any — looking through
 * optionality and one level of array.
 */
export function identityOfProperty(property: ModelProperty): ResolvedIdentity | undefined {
  const type = property.type;
  if (type.kind === "Model" && isArrayModelType(type as Model)) {
    const element = (type as Model).indexer?.value;
    const identity = element ? identityOfType(element) : undefined;
    return identity && { identity, array: true };
  }
  const identity = identityOfType(type);
  return identity && { identity, array: false };
}

function identityOfType(type: Type): IdentityBinding | undefined {
  if (type.kind !== "Scalar") return undefined;
  const scalar = type as Scalar;
  if (!isInNamespace(scalar, COMMON_NAMESPACE)) return undefined;
  return IDENTITIES.find((candidate) => candidate.scalar === scalar.name);
}

/**
 * A scalar that could be mistaken for a shared identity because its name ends
 * with one — e.g. a `RunnerSessionId` against `SessionId`.
 */
export function confusableIdentityFor(scalar: Scalar): IdentityBinding | undefined {
  if (isInNamespace(scalar, COMMON_NAMESPACE)) return undefined;
  return IDENTITIES.find(
    (candidate) => scalar.name !== candidate.scalar && scalar.name.endsWith(candidate.scalar),
  );
}

/** The doc phrase a confusable scalar must carry to declare its relationship. */
export function relationshipPhrase(identity: IdentityBinding): string {
  return `${COMMON_NAMESPACE}.${identity.scalar}`;
}

// =============================================================================
// Contract scope
// =============================================================================
// The wire-naming and identity rules above look only at the product contract.
// The redundancy, duplication and storage rules look at every namespace that
// holds contract declarations, and at nothing else: the in-repo emitter
// libraries are compiled into the same program and must not be linted as if
// they were part of the contract.

/** Namespaces whose declarations are contract: wire, tools, storage, ingestion, configuration. */
export const CONTRACT_NAMESPACES: readonly string[] = ["Nami.Api.Contracts", "Nami.Models", "Nami.Shapes"];

/**
 * Namespace that declares the in-repo emitter decorators (`emitters/csharp/lib/main.tsp`).
 *
 * The linter reads those decorators by their TypeSpec name from the type graph
 * and never imports emitter code, so it works the same with `--no-emit`, in the
 * editor and in tests, and cannot drift from an emitter's private state keys.
 */
export const EMIT_NAMESPACE = "Nami.Api.Schema.Emit.CSharp";

/**
 * Emitter decorators that make a declaration a root of the contract: something
 * outside the TypeSpec sources consumes its target and every type it names.
 */
export const ROOT_DECORATORS: Readonly<Record<string, string>> = {
  tool: "tool input, result or error",
  wireRoot: "wire revision root",
  duckDbTable: "storage row",
  storageProjection: "ingestion-to-storage projection",
  storageProjectionUsing: "named representation conversion",
  modelProjection: "row or API projection",
  projectionSource: "projection source property",
  catalogProjection: "attribute catalog projection",
  attributeProjection: "stored-to-API attribute projection",
  storageAttributeProjection: "ingestion-to-stored attribute projection",
  slotValue: "typed slot value",
};

/** Emitter decorator naming the generated consumer file of a whole namespace. */
export const OUTPUT_DECORATOR = "csharpOutput";

/**
 * Emitter decorator that maps a namespace into the contracts assembly.
 *
 * For every namespace it maps, the C# emitter writes each model, enum and tagged
 * union to `internal/nami.contracts` and the TypeScript emitter writes the same
 * declarations to `src/nami.mcp/src/contracts/generated`
 * (`emitters/csharp/src/emitter.ts`, `emitters/ts-types/src/index.ts`).
 * Hand-written code can use those generated types, and the TypeSpec program
 * cannot see whether it does, so a declaration with a generated type counts as
 * reached. At 48bdb08 tests do use two such types that no tool reaches:
 * `OTel.Metrics.MetricPointData` and `OTel.Metrics.MetricNumberValue`
 * (`tests/Nami.UnitTests/ContractTests.cs`, `tests/Nami.IntegrationTests/UnionCompilerTests.cs`).
 */
export const GENERATED_NAMESPACE_DECORATOR = "csharpNamespace";

/** The declaration kinds both emitters turn into a type of their own. */
export const GENERATED_TYPE_KINDS: readonly string[] = ["Model", "Enum", "Union"];

/**
 * Decorators, by full name, that make an emitter generate a type from a scalar.
 * A scalar without one is written by no emitter as a type: C# and TypeScript
 * use its base type, and only a schema entry carries its name.
 */
export const BRAND_DECORATORS: readonly string[] = [
  "Nami.Api.Schema.Emit.CSharp.csharpBrand",
  "Nami.Api.Schema.Emit.TsTypes.tsBrand",
];

/** Emitter decorators whose target and type arguments are wire roots: they enter the contract revision. */
export const WIRE_ROOT_DECORATORS: readonly string[] = ["wireRoot", "tool"];

/** Standard-library decorators that request an artifact for their target on their own. */
export const ROOT_LIBRARY_DECORATORS: Readonly<Record<string, string>> = {
  "TypeSpec.JsonSchema.jsonSchema": "JSON Schema document",
};

// =============================================================================
// Storage policy
// =============================================================================

/** Emitter decorators describing a DuckDB table and its columns. */
export const TABLE_DECORATOR = "duckDbTable";
export const COLUMN_DECORATOR = "duckDbColumn";

/**
 * Secondary indexes one table may declare before `storage-index-budget` asks
 * for a review. Set between the second largest table at 48bdb08 (`logs`, 5)
 * and the largest (`spans`, 19), so the gate reports today's outlier and any
 * table that grows towards it. Override for one run with
 * `--linter-rule '@ancplua/typespec-nami-lint/storage-index-budget={"maxIndexes":N}'`.
 */
export const STORAGE_INDEX_BUDGET = 8;

/**
 * The page the composite-index rule rests on: "Indexes are only eligible for
 * index scans if they index a single column without expressions." This is the
 * documentation of DuckDB 1.5, the line nami runs through DuckDB.NET 1.5.6.1;
 * the 1.3, 1.4 and 2.0 pages carry the same sentence (read on 12 Nov 2026).
 * Move the link with the engine. The rule repeats the sentence and checks
 * structure only; it makes no claim about measured performance.
 */
export const DUCKDB_INDEX_SCAN_URL = "https://duckdb.org/docs/1.5/guides/performance/indexing#art-index-scans";

// =============================================================================
// Duplicate-shape policy
// =============================================================================

/**
 * Own properties a model must declare before it can be reported as a copy of
 * another model. A model that only extends or spreads its source declares the
 * derivation in the source text and is never a copy; a one-property model is
 * too small for "same shape" to mean "same thing".
 */
export const MIN_DUPLICATE_SHAPE_PROPERTIES = 2;
