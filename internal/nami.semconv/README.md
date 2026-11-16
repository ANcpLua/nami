# Nami.SemanticConventions

A dependency-free .NET 11 library generated from Nami's pinned Weaver registry.

From the repository root:

```sh
registry/generate.sh
registry/generate.sh --check
dotnet build internal/nami.semconv/Nami.SemanticConventions.csproj -c Release
```

`AttributeCatalog.All` exposes typed metadata; `AttributeCatalog.Find` also recognizes the
expanded keys of a declared template attribute. No generated type decides what is stored: nami stores telemetry as it was sent.
`AttributeStability` distinguishes stable/development/deprecated/unknown keys, and
`DeprecatedAttributes` supplies declared replacement keys. `AttributeStability.ValueOf(key, value)`
reports each enum value's own stability, so development values remain available without being
misrepresented as stable.

Per-namespace constant classes (for example `HttpAttributes`, `GenAiAttributes`, `NamiAttributes`)
include enum-value classes. `Metrics.All` and `Spans.All` expose typed definitions, alongside
named constants such as `Metrics.HttpServerRequestDuration.Name` and `Spans.HttpServer.Kind`.
`Spans` includes refinements such as the four database-specific SQL shapes.

The constant of a deprecated attribute key, the value class of a deprecated enum attribute and the
class of a deprecated metric or span carry `[Obsolete]`. The message names the successor
(`Replaced by android.app.state.`) or, when the registry declares none, repeats its deprecation
note. Code that uses one gets CS0618; the runtime catalog (`AttributeCatalog`, `Metrics.All`,
`Spans.All`) still lists every definition.

The embedded `Nami.SemanticConventions.RegistryInventory.json` resource records the exact source
commits and inventory counts. It is generated with the code, not maintained separately.
