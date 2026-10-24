# Nami agent control library

Private TypeScript library for one CLI-driven diagnose → plan → apply → validate workflow.
TypeSpec owns diagnostic identities and offered text fixes. `eng/agent-policy/Policy.lean`
owns transition decisions; the CLI executes that exact definition with the pinned Lean 4.34.1.
No product MCP tools, telemetry schemas or internal HTTP service are added.

Use from the repository root:

```sh
mise agent -- prepare
mise agent -- proof
mise agent -- diagnose
mise agent -- plan
mise agent -- apply PLAN_ID
mise agent -- codefixes
mise agent -- type-info Nami.Api.Contracts.Common.ToolReadInput.maxBytes
```

For the supported native TypeSpec fix:

```sh
mise agent -- tsp-plan contracts/main.tsp @ancplua/typespec-nami-lint/wire-name-snake-case nami-add-wire-name
mise agent -- apply PLAN_ID
```

`plan` does not change source or remove files. `apply` requires its exact ID, unchanged inputs
and unchanged policy. Artifact ownership comes from the same `diagnostics.py` policy used by
ordinary cleanup; arbitrary paths are never executable fixes. The initial native TypeSpec adapter
handles a single `.tsp` file in `contracts/` and only the named wire-name fix. It adds the declared
JSON name; it does not rename language-facing properties or suppress the diagnostic.

Codefix previews use TypeSpec's public `resolveCodeFix`/`applyCodeFix` APIs with an in-memory host.
Application uses `applyCodeFix` with a guarded write host, then recompiles without emitting.
This verifies the source repair only. Run the normal Contracts/build gate before claiming
that generated contracts and the whole product are current.
Library accessors return diagnostics instead of reporting them repeatedly. The optional
`$provideTypeInfo` export reads the type graph lazily and is enabled by the library's own
`tspconfig.yaml`; the package includes that file and its TypeSpec entry point.

Plans, validation results and temporary backups live in the external current-state directory.
`mise diagnostics` discovers them. Successful validation removes backups. An unsuccessful
validation or effect triggers conditional restoration; an externally modified file is never
silently overwritten. If restoration cannot be verified, the result stays refused and the
current backup is retained. Interrupted processes may require recovery; no crash-durability or
filesystem atomicity theorem is claimed.

## Proof boundary

Lean proves the finite transition function's guard implications: applying requires ownership,
matching revision and preserved protected sources; entering verified requires successful
validation; rollback requires verified restoration; terminal states cannot apply again.
There is no `sorry` or added axiom in the policy. The executable transition function is the one
that was checked, not a parallel TypeScript switch statement.

The proof assumes that the host supplies truthful filesystem/validator facts. It does not prove
Node, TypeSpec, filesystem behavior, the correctness of arbitrary source changes, or all of Nami.
Real integration tests exercise stale/tampered plans, retained sources, conditional rollback,
native TypeSpec edits, suppression rejection and read-only type information.
