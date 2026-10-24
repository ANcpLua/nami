import "../dist/index.js";

using TypeSpec.Reflection;

namespace Nami.Api.Schema.Emit.CSharp;

extern dec csharpNamespace(
  target: Model | Enum | Namespace,
  ns: valueof string
);
extern dec csharpBrand(target: Scalar);
extern dec csharpPolymorphic(target: Union, discriminator?: valueof string);
