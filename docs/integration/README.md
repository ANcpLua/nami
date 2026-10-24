# Nami integration decisions

The product requirements are `docs/prd/nami.md`; `docs/checklist.md` owns implementation scope.

- The host is `src/nami/nami.csproj`, assembly `nami`. No arguments serves stateless MCP HTTP;
  `--stdio` serves MCP on stdin/stdout. Both modes expose OTLP and `/health`. This follows the
  checklist's explicit executable behavior; the PRD's shorter local-start paragraph was ambiguous.
- Nami-projected discriminated attribute and metric unions use the PRD's `kind` field and closed
  C# 15 unions. Native OTLP JSON/protobuf encoding and internal persisted storage tags are unchanged.
  Tool results remain direct typed objects, not a new envelope.
- A returned `snapshot_id` identifies an immutable, expiring DuckDB snapshot; a cursor binds to that
  snapshot, tool and argument hash. The schema declares these continuation fields explicitly.
- Self-export uses an ephemeral process-local ingest credential accepted only from loopback and
  scoped to the `nami` project. User credentials retain their configured project and roles.
- The managed Fallout host uses `UseAppHost=false`: its 10.4.0 entry-assembly check otherwise rejects
  the .NET 11 native apphost's command-line path. The application itself remains NativeAOT.
- Registry materialization and upstream-format diagnostics are documented in `1-registry.md`.
- Generated wire contract decisions and compiler proofs are documented in `4-contracts.md`.
- Storage/snapshot and tool behavior are documented in `6-tools.md` and `7-storage.md`.

The build and tests, not unchecked tasks or an older chat status, establish completion.
