# Internal wire contracts

This assembly is part of the single Nami binary; it is not a published SDK or NuGet package.
The repository's shared build properties select .NET and C# versions. The assembly remains
AOT-compatible and uses generated JSON metadata with reflection serialization disabled.

`contracts/` owns the TypeSpec wire contract. `./eng/build.sh Contracts` generates and commits
the C# files under `Nami/`; `VerifyContracts` in the build gate checks that regeneration changes
nothing. Edit the emitter or TypeSpec input, never the generated files directly.

`ContractJson.cs`, `ContractPattern.cs` and `WireConverters.cs` implement validation and exact wire
conversion beside those generated types. `contract-schemas.json` is embedded as
`nami.contract-schemas.json` and the generated serializer context owns the type registry.

The Unit suite proves wire values and serialization; Integration checks serializer coverage
and revision stability; EndToEnd exercises those contracts through the real NativeAOT host.
