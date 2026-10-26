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
  json?: boolean; context?: string; typescript?: boolean; schema?: boolean;
}
model TableOptions {
  name: string; indexes?: string; uniqueIndexes?: string; onConflict?: string;
  parameterSets?: string; derived?: boolean; appenderEligible?: boolean; arrowEligible?: boolean;
}
model ColumnOptions {
  order: int32; sqlType?: string; primaryKeyOrdinal?: int32;
  defaultSql?: string; excludeFromInsert?: boolean; privateInit?: boolean;
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

extern dec environment(target: ModelProperty, name: valueof string);

extern dec attributeProjection(target: Union, destination: Union);

extern dec csharpEnum(target: Enum, type: valueof string);

extern dec storageAttributeProjection(target: Union, destination: Union);
