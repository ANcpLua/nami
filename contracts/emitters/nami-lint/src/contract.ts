// =============================================================================
// The contract as the whole-program rules see it, computed once per program.
// =============================================================================
// Every rule that asks a question about the contract as a whole (what is a
// root, what is reachable, what does a table look like) reads the answer from
// here. Two properties hold for everything in this file:
//
//   deterministic  every list is sorted by a key taken from the sources (a full
//                  type name or a declared ordinal), never by the order files
//                  were found on disk, and nothing reads the clock, the
//                  environment or the file system
//   decoupled      emitter metadata is read from decorator applications by the
//                  decorator's TypeSpec name; no emitter module is imported
// =============================================================================

import type {
  DecoratorApplication,
  DiagnosticTarget,
  Enum,
  Interface,
  Model,
  ModelProperty,
  Namespace,
  Operation,
  Program,
  Scalar,
  Type,
  Union,
  Value,
} from "@typespec/compiler";
import { getDiscriminator, getLocationContext, getNamespaceFullName, getTypeName } from "@typespec/compiler";
import { SyntaxKind } from "@typespec/compiler/ast";
import type { Node, ObjectLiteralNode } from "@typespec/compiler/ast";
import {
  BRAND_DECORATORS,
  COLUMN_DECORATOR,
  COMMON_NAMESPACE,
  CONTRACT_NAMESPACES,
  EMIT_NAMESPACE,
  GENERATED_NAMESPACE_DECORATOR,
  GENERATED_TYPE_KINDS,
  IDENTITIES,
  OUTPUT_DECORATOR,
  ROOT_DECORATORS,
  ROOT_LIBRARY_DECORATORS,
  TABLE_DECORATOR,
  WIRE_ROOT_DECORATORS,
  isInNamespace,
} from "./policy.js";

// =============================================================================
// Names and ordering
// =============================================================================

/** A named type declaration the contract can own. */
export type Declaration = Model | Union | Enum | Scalar;

/** `Namespace.Name`, the one key every list in this library is sorted by. */
export function fullName(type: { name?: string; namespace?: Namespace }): string {
  const namespace = type.namespace ? getNamespaceFullName(type.namespace) : "";
  const name = type.name ?? "(anonymous)";
  return namespace ? `${namespace}.${name}` : name;
}

/** Ordinal string comparison: the same order on every machine and locale. */
export function compareText(left: string, right: string): number {
  return left < right ? -1 : left > right ? 1 : 0;
}

/** Whether a type is declared in one of the contract namespaces of the project being compiled. */
export function isContractType(program: Program, type: Type): boolean {
  if (!CONTRACT_NAMESPACES.some((namespace) => isInNamespace(type, namespace))) return false;
  return getLocationContext(program, type).type === "project";
}

// =============================================================================
// Emitter decorators, read by name
// =============================================================================

/** One application of a decorator declared in `EMIT_NAMESPACE`. */
export interface EmitDecorator {
  /** Decorator name without the `@`. */
  readonly name: string;
  readonly application: DecoratorApplication;
}

function decoratorFullName(application: DecoratorApplication): string | undefined {
  const definition = application.definition;
  if (!definition) return undefined;
  const namespace = getNamespaceFullName(definition.namespace);
  const name = definition.name.replace(/^@/, "");
  return namespace ? `${namespace}.${name}` : name;
}

/** The emitter decorators applied to a type, in source order, augment decorators included. */
export function emitDecorators(type: Type): EmitDecorator[] {
  const applications = "decorators" in type ? (type.decorators as DecoratorApplication[]) : [];
  const result: EmitDecorator[] = [];
  for (const application of applications) {
    const name = decoratorFullName(application);
    if (name?.startsWith(`${EMIT_NAMESPACE}.`)) {
      result.push({ name: name.slice(EMIT_NAMESPACE.length + 1), application });
    }
  }
  return result;
}

/** The first application of the named emitter decorator on a type. */
export function emitDecorator(type: Type, name: string): EmitDecorator | undefined {
  return emitDecorators(type).find((decorator) => decorator.name === name);
}

/**
 * The types a decorator application names: a type passed as an argument, and a
 * type referenced from inside a value argument with `typeref(T)` (TypeSpec
 * 1.18.0, behind the `typeref` feature), however deep in an object or array.
 */
function typeArguments(application: DecoratorApplication): Type[] {
  const result: Type[] = [];
  const collect = (entity: Type | Value): void => {
    if (entity.entityKind === "Type") {
      result.push(entity);
      return;
    }
    switch (entity.valueKind) {
      case "TypeRefValue":
        result.push(entity.target);
        break;
      case "ObjectValue":
        for (const property of entity.properties.values()) collect(property.value);
        break;
      case "ArrayValue":
        for (const value of entity.values) collect(value);
        break;
      default:
        break;
    }
  };
  for (const argument of application.args) collect(argument.value);
  return result;
}

/** The object passed as a decorator's first argument, when it is one. */
function objectArgument(application: DecoratorApplication): Record<string, unknown> | undefined {
  const value = application.args[0]?.jsValue;
  return typeof value === "object" && value !== null ? (value as Record<string, unknown>) : undefined;
}

/** The syntax node of one property of an object-literal decorator argument. */
function objectArgumentNode(application: DecoratorApplication, property: string): Node | undefined {
  const node = application.args[0]?.node as Node | undefined;
  if (node?.kind !== SyntaxKind.ObjectLiteral) return undefined;
  for (const member of (node as ObjectLiteralNode).properties) {
    if (member.kind === SyntaxKind.ObjectLiteralProperty && member.id.sv === property) return member.value;
  }
  return undefined;
}

// =============================================================================
// Declarations, roots and reachability
// =============================================================================

export interface ContractGraph {
  /** Every named contract declaration of the project, sorted by full name. */
  readonly declarations: readonly Declaration[];
  /**
   * Whether an in-repo emitter generates a type from this declaration for a
   * project outside `contracts/`, where hand-written code can use it.
   */
  hasGeneratedType(declaration: Declaration): boolean;
  /**
   * Whether something outside the TypeSpec sources consumes this declaration,
   * directly or through another: a tool, wire root, route, storage row,
   * projection or generated consumer file, and, unless `generatedTypes` is
   * false, a type an emitter generates for hand-written code.
   */
  isReachable(declaration: Declaration, generatedTypes?: boolean): boolean;
  /** Declarations this one references, directly or indirectly, itself excluded. Sorted by full name. */
  dependencies(declaration: Declaration): readonly Declaration[];
  /** Types declared as wire roots, sorted by full name. */
  readonly wireRoots: readonly (Model | Union)[];
}

const graphs = new WeakMap<Program, ContractGraph>();

/** The contract graph of a program, built on first use and shared by every rule. */
export function contractGraph(program: Program): ContractGraph {
  let graph = graphs.get(program);
  if (!graph) {
    graph = buildGraph(program);
    graphs.set(program, graph);
  }
  return graph;
}

/**
 * A declaration and every instance of it share one syntax node, so the node is
 * the identity that makes "an instance of this template is used" count as a use
 * of the template declaration.
 */
function declarationKey(type: Type): unknown {
  return type.node ?? type;
}

function sortedValues<T extends { name?: string | symbol }>(map: Iterable<T>): T[] {
  return [...map].sort((left, right) => compareText(String(left.name), String(right.name)));
}

function hasOutputProfile(type: { namespace?: Namespace }): boolean {
  for (let namespace = type.namespace; namespace; namespace = namespace.namespace) {
    if (emitDecorator(namespace, OUTPUT_DECORATOR)) return true;
  }
  return false;
}

/** Mirrors what the C# and TypeScript emitters write as a type; see `GENERATED_NAMESPACE_DECORATOR`. */
function hasGeneratedType(declaration: Declaration): boolean {
  if (declaration.kind === "Scalar") {
    return declaration.decorators.some((application) => {
      const name = decoratorFullName(application);
      return name !== undefined && BRAND_DECORATORS.includes(name);
    });
  }
  if (!GENERATED_TYPE_KINDS.includes(declaration.kind)) return false;
  if (emitDecorator(declaration, GENERATED_NAMESPACE_DECORATOR)) return true;
  for (let namespace = declaration.namespace; namespace; namespace = namespace.namespace) {
    if (emitDecorator(namespace, GENERATED_NAMESPACE_DECORATOR)) return true;
  }
  return false;
}

function buildGraph(program: Program): ContractGraph {
  const declarations: Declaration[] = [];
  const roots: Type[] = [];
  const wireRootSet = new Set<Model | Union>();

  const identityScalars = new Set(IDENTITIES.map((identity) => `${COMMON_NAMESPACE}.${identity.scalar}`));

  function addDecoratorRoots(type: Type): boolean {
    let isRoot = false;
    for (const decorator of emitDecorators(type)) {
      if (!Object.hasOwn(ROOT_DECORATORS, decorator.name)) continue;
      isRoot = true;
      roots.push(...typeArguments(decorator.application));
    }
    const applications = "decorators" in type ? (type.decorators as DecoratorApplication[]) : [];
    for (const application of applications) {
      const name = decoratorFullName(application);
      if (name !== undefined && Object.hasOwn(ROOT_LIBRARY_DECORATORS, name)) isRoot = true;
    }
    return isRoot;
  }

  function addDeclaration(type: Declaration): void {
    if (!type.name || !isContractType(program, type)) return;
    declarations.push(type);
    let isRoot = addDecoratorRoots(type);
    // A namespace with an output profile is written, whole, to one generated
    // file that hand-written code consumes. That code is outside the program,
    // so every declaration in such a namespace is a root.
    if (hasOutputProfile(type)) isRoot = true;
    // An identity scalar is named by this library's own policy table.
    if (type.kind === "Scalar" && identityScalars.has(fullName(type))) isRoot = true;
    if (isRoot) roots.push(type);
    if (type.kind === "Model") {
      for (const property of type.properties.values()) {
        if (addDecoratorRoots(property)) roots.push(property);
      }
    }
    // `@@wireRoot(X)` roots X; `@@tool(Input, name, Result, Error)` roots all three.
    for (const decorator of emitDecorators(type)) {
      if (!WIRE_ROOT_DECORATORS.includes(decorator.name)) continue;
      for (const root of [type, ...typeArguments(decorator.application)]) {
        if (root.kind === "Model" || root.kind === "Union") wireRootSet.add(root);
      }
    }
  }

  function addOperation(operation: Operation): void {
    if (isContractType(program, operation)) roots.push(operation);
  }

  function walk(namespace: Namespace): void {
    for (const model of sortedValues(namespace.models.values())) addDeclaration(model);
    for (const union of sortedValues(namespace.unions.values())) addDeclaration(union);
    for (const enumeration of sortedValues(namespace.enums.values())) addDeclaration(enumeration);
    for (const scalar of sortedValues(namespace.scalars.values())) addDeclaration(scalar);
    for (const operation of sortedValues(namespace.operations.values())) addOperation(operation);
    for (const iface of sortedValues<Interface>(namespace.interfaces.values())) {
      // An interface operation has no namespace of its own; its interface decides the scope.
      if (!isContractType(program, iface)) continue;
      for (const operation of sortedValues(iface.operations.values())) roots.push(operation);
    }
    for (const child of sortedValues(namespace.namespaces.values())) walk(child);
  }
  walk(program.getGlobalNamespaceType());
  declarations.sort((left, right) => compareText(fullName(left), fullName(right)));
  const wireRoots = [...wireRootSet].sort((left, right) => compareText(fullName(left), fullName(right)));

  const declared = new Map<unknown, Declaration>();
  for (const declaration of declarations) declared.set(declarationKey(declaration), declaration);

  /** Every type reachable from `start`, as declaration keys. */
  function closure(start: readonly Type[]): Set<unknown> {
    const visited = new Set<Type>();
    const keys = new Set<unknown>();
    const pending = [...start];
    const push = (type: Type | undefined): void => {
      if (type && !visited.has(type)) pending.push(type);
    };
    while (pending.length > 0) {
      const type = pending.pop()!;
      if (visited.has(type)) continue;
      visited.add(type);
      keys.add(declarationKey(type));
      // A type named by any decorator on a used type is used: `@configuredBy(Keys.port)`.
      if ("decorators" in type) {
        for (const application of type.decorators as DecoratorApplication[]) {
          for (const argument of typeArguments(application)) {
            // A namespace argument names an owner, not its members.
            if (argument.kind !== "Namespace") push(argument);
          }
        }
      }
      switch (type.kind) {
        case "Model":
          push(type.baseModel);
          // A used `@discriminator` base brings its derived models: each is a variant it can be on the wire.
          if (getDiscriminator(program, type)) {
            for (const derived of type.derivedModels) push(derived);
          }
          for (const source of type.sourceModels) push(source.model);
          push(type.indexer?.key);
          push(type.indexer?.value);
          for (const property of type.properties.values()) push(property);
          for (const argument of type.templateMapper?.args ?? []) {
            if ((argument as { entityKind?: string }).entityKind === "Type") push(argument as Type);
          }
          break;
        case "ModelProperty":
          push(type.type);
          push(type.sourceProperty);
          // A property reference (`x: Row.x`) uses the model that owns the property.
          push(type.model);
          break;
        case "Union":
          for (const variant of type.variants.values()) push(variant.type);
          break;
        case "UnionVariant":
          push(type.type);
          push(type.union);
          break;
        case "Scalar":
          push(type.baseScalar);
          break;
        case "EnumMember":
          push(type.enum);
          break;
        case "Tuple":
          for (const value of type.values) push(value);
          break;
        case "Operation":
          push(type.parameters);
          push(type.returnType);
          push(type.sourceOperation);
          break;
        default:
          break;
      }
    }
    return keys;
  }

  // Two answers, each computed on first use: with and without the generated types as roots.
  const generated = declarations.filter(hasGeneratedType);
  let reachable: Set<unknown> | undefined;
  let reachableWithoutGenerated: Set<unknown> | undefined;
  const dependencyCache = new Map<Declaration, readonly Declaration[]>();

  return {
    declarations,
    wireRoots,
    hasGeneratedType,
    isReachable(declaration, generatedTypes = true) {
      const set = generatedTypes
        ? (reachable ??= closure([...roots, ...generated]))
        : (reachableWithoutGenerated ??= closure(roots));
      return set.has(declarationKey(declaration));
    },
    dependencies(declaration) {
      let result = dependencyCache.get(declaration);
      if (!result) {
        const own = declarationKey(declaration);
        result = [...closure([declaration])]
          .filter((key) => key !== own && declared.has(key))
          .map((key) => declared.get(key)!)
          .sort((left, right) => compareText(fullName(left), fullName(right)));
        dependencyCache.set(declaration, result);
      }
      return result;
    },
  };
}

// =============================================================================
// Storage tables
// =============================================================================

export interface StorageColumn {
  readonly property: ModelProperty;
  /** The CLR property name the table's index lists use: `projectId` -> `ProjectId`. */
  readonly columnName: string;
  /** Declared position, when the decorator carries an integer one. */
  readonly order: number | undefined;
  readonly primaryKeyOrdinal: number | undefined;
  /** The literal holding `order`, for a code fix. */
  readonly orderNode: Node | undefined;
}

export interface StorageIndex {
  readonly columns: readonly string[];
  readonly unique: boolean;
  /** The index as written, column names joined by a comma. */
  readonly text: string;
}

export interface StorageTable {
  readonly model: Model;
  /** The DuckDB table name. */
  readonly name: string;
  /** Columns in declaration order. */
  readonly columns: readonly StorageColumn[];
  /** Primary-key column names, in key order. */
  readonly primaryKey: readonly string[];
  /** Declared secondary indexes, in declaration order: `indexes` first, then `uniqueIndexes`. */
  readonly indexes: readonly StorageIndex[];
  /** Where an index finding points: the `indexes` text, or the table declaration. */
  readonly indexTarget: DiagnosticTarget;
}

const tables = new WeakMap<Program, readonly StorageTable[]>();

function pascal(name: string): string {
  return name.charAt(0).toUpperCase() + name.slice(1);
}

function integer(value: unknown): number | undefined {
  return typeof value === "number" && Number.isInteger(value) ? value : undefined;
}

/** `"A;B,C"` -> `[[A], [B, C]]`. Empty entries carry no index and are dropped. */
function parseIndexes(value: unknown, unique: boolean): StorageIndex[] {
  if (typeof value !== "string") return [];
  const result: StorageIndex[] = [];
  for (const entry of value.split(";")) {
    const columns = entry.split(",").map((column) => column.trim()).filter((column) => column !== "");
    if (columns.length > 0) result.push({ columns, unique, text: columns.join(",") });
  }
  return result;
}

/** Every `@duckDbTable` model of the contract, sorted by full name. */
export function storageTables(program: Program): readonly StorageTable[] {
  let result = tables.get(program);
  if (!result) {
    result = contractGraph(program).declarations.flatMap((declaration) => {
      if (declaration.kind !== "Model") return [];
      const table = emitDecorator(declaration, TABLE_DECORATOR);
      const options = table && objectArgument(table.application);
      if (!table || !options) return [];
      const columns: StorageColumn[] = [];
      for (const property of declaration.properties.values()) {
        const column = emitDecorator(property, COLUMN_DECORATOR);
        const columnOptions = column && objectArgument(column.application);
        if (!column || !columnOptions) continue;
        columns.push({
          property,
          columnName: pascal(property.name),
          order: integer(columnOptions.order),
          primaryKeyOrdinal: integer(columnOptions.primaryKeyOrdinal),
          orderNode: objectArgumentNode(column.application, "order"),
        });
      }
      const primaryKey = columns
        .filter((column) => column.primaryKeyOrdinal !== undefined)
        .sort((left, right) => left.primaryKeyOrdinal! - right.primaryKeyOrdinal! || compareText(left.columnName, right.columnName))
        .map((column) => column.columnName);
      return [{
        model: declaration,
        name: typeof options.name === "string" ? options.name : declaration.name,
        columns,
        primaryKey,
        indexes: [...parseIndexes(options.indexes, false), ...parseIndexes(options.uniqueIndexes, true)],
        indexTarget: objectArgumentNode(table.application, "indexes") ?? declaration,
      }];
    });
    tables.set(program, result);
  }
  return result;
}

// =============================================================================
// Shapes
// =============================================================================

/** A stable, readable key for a property's type: a full name, or the structure of an anonymous type. */
export function typeKey(type: Type): string {
  return getTypeName(type);
}
