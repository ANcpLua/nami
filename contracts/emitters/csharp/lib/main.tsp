import "../dist/index.js";

using TypeSpec.Reflection;

namespace Nami.Api.Schema.Emit.CSharp;

// `auto dec`: the compiler stores the arguments as a `{ parameterName: value }` record under the decorator's fully
// qualified name; src/decorators.ts reads them by parameter name, so renaming a parameter here is a change there.
// Applied twice on one declaration it warns, which the gate's `--warn-as-error` turns into a failure. An augment
// (`@@name(Target, …)`) is not counted as a second application: the last one written silently wins.
// `extern dec` is kept for a decorator whose JavaScript validates its arguments (`catalogValue`).

auto dec csharpNamespace(
  target: Model | Enum | Namespace,
  ns: valueof string
);
auto dec csharpBrand(target: Scalar);
auto dec csharpPolymorphic(target: Union, discriminator?: valueof string);

model OutputOptions {
  directory: string; `namespace`: string; file: string;
  json?: boolean; context?: string; typescript?: boolean; schema?: boolean; mutable?: boolean; strictJson?: boolean;
}
model TableOptions {
  name: string; indexes?: string; uniqueIndexes?: string; conflict?: "ignore" | "update";
  parameterSets?: string; derived?: boolean; bootstrap?: boolean; appenderEligible?: boolean; arrowEligible?: boolean;
}
model ColumnOptions {
  order: int32; sqlType?: string; primaryKeyOrdinal?: int32;
  defaultSql?: string; excludeFromInsert?: boolean; privateInit?: boolean;
  sequenceName?: string;
  merge?: "replace" | "coalesce" | "min" | "max" | "minPresent" | "longest";
}
auto dec csharpOutput(target: Namespace, options: valueof OutputOptions);
auto dec csharpForm(target: Model, form: valueof string);
auto dec csharpType(target: ModelProperty | Model | Scalar | Union, type: valueof string);
auto dec duckDbTable(target: Model, options: valueof TableOptions);
auto dec duckDbColumn(target: ModelProperty, options: valueof ColumnOptions);

auto dec tool(target: Model, name: valueof string, result: Model, error: Model);
auto dec wireRoot(target: Model | Union);

auto dec slotValue(target: Union);

auto dec storageProjection(target: Model, destination: Model);
auto dec storageProjectionUsing(target: Model | Union | Scalar, method: valueof string);
auto dec modelProjection(target: Model, source: Model, owner: Model | Namespace, additional?: Model);
auto dec projectionSource(target: ModelProperty, source: ModelProperty);
auto dec catalogProjection(target: Model, values: Union);
extern dec catalogValue(target: ModelProperty, symbol: valueof string);

auto dec environment(target: ModelProperty, name: valueof string);
auto dec configuredBy(target: ModelProperty, key: ModelProperty);
auto dec csharpSetter(target: ModelProperty, setter: valueof "set" | "internal set" | "init");
auto dec csharpInternal(target: ModelProperty);

auto dec attributeProjection(target: Union, destination: Union);

auto dec csharpEnum(target: Enum, type: valueof string, flags?: valueof boolean);

auto dec storageAttributeProjection(target: Union, destination: Union);
