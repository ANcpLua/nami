import { setTypeSpecNamespace } from "@typespec/compiler";
import { $csharpBrand, $csharpNamespace, $csharpPolymorphic } from "./decorators.js";

setTypeSpecNamespace("Nami.Api.Schema.Emit.CSharp", $csharpNamespace, $csharpBrand, $csharpPolymorphic);

export { $lib } from "./lib.js";
export { $csharpBrand, $csharpNamespace, $csharpPolymorphic } from "./decorators.js";
export { $onEmit } from "./emitter.js";

import { $csharpOutput, $csharpForm, $csharpType, $duckDbTable, $duckDbColumn } from "./outputs.js";
setTypeSpecNamespace("Nami.Api.Schema.Emit.CSharp", $csharpOutput, $csharpForm, $csharpType, $duckDbTable, $duckDbColumn);
export { $csharpOutput, $csharpForm, $csharpType, $duckDbTable, $duckDbColumn };

import { $tool, $wireRoot } from "./tool-metadata.js";
setTypeSpecNamespace("Nami.Api.Schema.Emit.CSharp", $tool, $wireRoot);
export { $tool, $wireRoot };

import { $slotValue } from "./slots.js";
setTypeSpecNamespace("Nami.Api.Schema.Emit.CSharp", $slotValue);
export { $slotValue };

import { $storageProjection, $storageProjectionUsing } from "./projections.js";
setTypeSpecNamespace("Nami.Api.Schema.Emit.CSharp", $storageProjection, $storageProjectionUsing);
export { $storageProjection, $storageProjectionUsing };

import { $environment } from "./outputs.js";
setTypeSpecNamespace("Nami.Api.Schema.Emit.CSharp", $environment);
export { $environment };

import { $attributeProjection } from "./attribute-projection.js";
setTypeSpecNamespace("Nami.Api.Schema.Emit.CSharp", $attributeProjection);
export { $attributeProjection };

import { $csharpEnum } from "./outputs.js";
setTypeSpecNamespace("Nami.Api.Schema.Emit.CSharp", $csharpEnum);
export { $csharpEnum };

import { $storageAttributeProjection } from "./attribute-projection.js";
setTypeSpecNamespace("Nami.Api.Schema.Emit.CSharp", $storageAttributeProjection);
export { $storageAttributeProjection };
