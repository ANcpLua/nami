import { setTypeSpecNamespace } from "@typespec/compiler";
import { $catalogValue } from "./projections.js";

// The one decorator of lib/main.tsp implemented here; the others are `auto dec` and need no JavaScript.
setTypeSpecNamespace("Nami.Api.Schema.Emit.CSharp", $catalogValue);

export { $lib } from "./lib.js";
export { $catalogValue };
export { $onEmit } from "./emitter.js";
