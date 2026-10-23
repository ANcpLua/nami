import { setTypeSpecNamespace } from "@typespec/compiler";
import { $csharpBrand, $csharpNamespace, $csharpPolymorphic } from "./decorators.js";

setTypeSpecNamespace("Nami.Api.Schema.Emit.CSharp", $csharpNamespace, $csharpBrand, $csharpPolymorphic);

export { $lib } from "./lib.js";
export { $csharpBrand, $csharpNamespace, $csharpPolymorphic } from "./decorators.js";
export { $onEmit } from "./emitter.js";
