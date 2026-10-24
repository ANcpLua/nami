# Nami.SemanticConventions

A dependency-free .NET 11 library generated from Nami's pinned Weaver registry.

From the repository root:

```sh
registry/generate.sh
registry/generate.sh --check
dotnet build internal/nami.semconv/Nami.SemanticConventions.csproj -c Release
```

`AttributeCatalog.All` exposes typed metadata; `AttributeCatalog.Find` also recognizes the
expanded keys of a declared template attribute. `RegistryPolicy` owns persistence decisions,
`AttributeStability` distinguishes stable/development/deprecated/unknown keys, and
`DeprecatedAttributes` supplies declared replacement keys. `AttributeStability.ValueOf(key, value)`
reports each enum value's own stability, so development values remain available without being
misrepresented as stable.

Per-namespace constant classes (for example `HttpAttributes`, `GenAiAttributes`, `NamiAttributes`)
include enum-value classes. `Metrics.All` and `Spans.All` expose typed definitions, alongside
named constants such as `Metrics.HttpServerRequestDuration.Name` and `Spans.HttpServer.Kind`.
`Spans` includes refinements such as the four database-specific SQL shapes.

The embedded `Nami.SemanticConventions.RegistryInventory.json` resource records the exact source
commits and inventory counts. It is generated with the code, not maintained separately.
