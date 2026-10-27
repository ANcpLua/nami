import "../dist/index.js";

using TypeSpec.Reflection;

namespace Nami.Api.Schema.Emit.CSharp;

extern dec csharpNamespace(
  target: Model | Enum | Namespace,
  ns: valueof string
);
extern dec csharpBrand(target: Scalar);
extern dec csharpPolymorphic(target: Union, discriminator?: valueof string);

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
extern dec csharpOutput(target: Namespace, options: valueof OutputOptions);
extern dec csharpForm(target: Model, form: valueof string);
extern dec csharpType(target: ModelProperty | Model | Scalar | Union, type: valueof string);
extern dec duckDbTable(target: Model, options: valueof TableOptions);
extern dec duckDbColumn(target: ModelProperty, options: valueof ColumnOptions);

extern dec tool(target: Model, name: valueof string, result: Model, error: Model);
extern dec wireRoot(target: Model | Union);

extern dec slotValue(target: Union);

extern dec storageProjection(target: Model, destination: Model);
extern dec storageProjectionUsing(target: Model | Union | Scalar, method: valueof string);
extern dec modelProjection(target: Model, source: Model, owner: Model | Namespace, additional?: Model);
extern dec projectionSource(target: ModelProperty, source: ModelProperty);
extern dec catalogProjection(target: Model, values: Union);
extern dec catalogValue(target: ModelProperty, symbol: valueof string);

extern dec environment(target: ModelProperty, name: valueof string);
extern dec configuredBy(target: ModelProperty, key: ModelProperty);
extern dec csharpSetter(target: ModelProperty, setter: valueof "set" | "internal set" | "init");
extern dec csharpInternal(target: ModelProperty);

extern dec attributeProjection(target: Union, destination: Union);

extern dec csharpEnum(target: Enum, type: valueof string, flags?: valueof boolean);

extern dec storageAttributeProjection(target: Union, destination: Union);
