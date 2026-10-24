# C# semantic convention integration

Reference `internal/nami.semconv/Nami.SemanticConventions.csproj`.
The namespace is `Nami.SemanticConventions`; `RegistryPolicy`, `AttributeStability`,
`DeprecatedAttributes`, and `AttributeCatalog` expose the resolved policy and metadata.

`NamiAttributes.ProjectId`, `GenAiAttributes.*`, `McpAttributes.*`, `ExceptionAttributes.*`,
`Metrics.McpServerOperationDuration`, and `Spans.*` are generated from exact registry keys.
The generated `CollectorSemanticAttributeCatalog` supplies the temporary stable naming
surface used by the copied ingestion code; its values reference these generated constants.
It is generated code, not a second handwritten policy table.

Release compilation of this library passed with zero warnings and errors before integration.
`AttributeStability.ValueOf` exposes per-enum-value stability (`redis` and `QUERY` are development).
