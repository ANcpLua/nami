# Workstream 6: read tools

The six tools are implemented in `src/nami/Tools/ReadTools.cs` and dispatched by
`Nami.Tools.ToolDispatcher`. They consume an immutable `ISnapshot` only; no tool mutates telemetry.

Host entry point:

```csharp
var service = new Nami.Tools.NamiToolService(store);
var result = await service.InvokeAsync(name, arguments, verifiedProjectId, cancellationToken);
// result.Content is the validated generated contract JSON; result.IsError selects the MCP error flag.
```

`ToolDispatcher.Run` is the pure entry point for deterministic tests. The snapshot's captured clock
owns relative `since`; callers replaying `snapshot_id` receive the same window after restart.
The optional cursor binds snapshot, tool, query and result offset. It cannot be reused across projects
because snapshot lookup includes the verified project. Unknown `fields` are refused, and all six tools
support projection. The reserved fields `profiles` and `baggage` are refused as `data_unavailable`;
other unknown fields are `invalid_argument` with the allowed field list. Each response is serialized and checked through the TypeSpec-generated `ContractJson`.

The tools return signal freshness, exact scanned and matched counts from the full retained snapshot,
and truncate output by serialized UTF-8 bytes. `traces_get` pages the spans within one trace; other tools
page their collection. A single item too large for the requested budget is refused with a projection hint.
`logs_search` uses ordinal literal matching. Error grouping reads exception log records, exception span
events, and otherwise-unrepresented error status spans.

Validation and tests run in the integration phase after the generated registry and contracts are ready.

## Integration verification

The coordinator built `src/nami/nami.csproj` in Release successfully with zero warnings and zero errors
after integrating the generated contracts and registry (host build 7). The sequential runtime and
conformance test phase follows this implementation pass.
