-- Vendored from the ancplua-lean-proof plugin (skills/lean-proof/scripts), MIT License, Copyright (c) 2026 ANcpLua.
/-
Axiom audit for a Lake project. Reads the compiled environment (build first).
Usage: lake env lean --run axiom_audit.lean <RootModule>... [--spec <Namespace>]
- Every declaration in modules under the given roots may depend only on
  propext, Quot.sound, Classical.choice. Catches sorry (sorryAx),
  native_decide (Lean.ofReduceBool / ofReduceNat) and project axioms.
- With --spec N: every closed `Prop` definition under namespace N must be the
  exact type of at least one theorem (statements are frozen in N; proofs live elsewhere).
This reads what the environment says. Also run `lake env leanchecker <Module>` per module:
that replays declarations through the kernel and catches environment hacking this cannot see.
Exit: 0 clean, 1 violations, 2 usage.
-/
import Lean
open Lean

def allowed : NameSet := NameSet.empty |>.insert ``propext |>.insert ``Quot.sound |>.insert ``Classical.choice

abbrev EnvM := StateM Environment
instance : MonadEnv EnvM where
  getEnv := get
  modifyEnv f := modify f

def axiomsOf (env : Environment) (n : Name) : Array Name :=
  (collectAxioms n : EnvM (Array Name)).run' env

def main (args : List String) : IO UInt32 := do
  let rec split : List String → List String → Option String → List String × Option String
    | [], roots, spec => (roots.reverse, spec)
    | "--spec" :: n :: rest, roots, _ => split rest roots (some n)
    | a :: rest, roots, spec => split rest (a :: roots) spec
  let (roots, spec) := split args [] none
  if roots.isEmpty then
    IO.eprintln "usage: lake env lean --run axiom_audit.lean <RootModule>... [--spec <Namespace>]"
    return 2
  initSearchPath (← findSysroot)
  unsafe Lean.enableInitializersExecution
  let env ← importModules (roots.map fun r => ({ module := r.toName } : Import)).toArray {} (loadExts := true)
  let rootNames := roots.map String.toName
  let inScope (m : Name) : Bool := rootNames.any fun r => r == m || r.isPrefixOf m
  let mut thms := 0
  let mut decls := 0
  let mut bad : Array String := #[]
  let mut theoremTypes : Array Expr := #[]
  let mut specs : Array Name := #[]
  for (n, ci) in env.constants.toList do
    let some idx := env.getModuleIdxFor? n | continue
    let some modName := env.header.moduleNames[idx.toNat]? | continue
    unless inScope modName do continue
    if n.isInternal then continue
    decls := decls + 1
    match ci with
    | .axiomInfo _ => bad := bad.push s!"AXIOM declared in project: {n}"
    | .thmInfo t =>
      thms := thms + 1
      theoremTypes := theoremTypes.push t.type
    | .defnInfo d =>
      if let some ns := spec then
        if ns.toName.isPrefixOf n && d.type.isProp then specs := specs.push n
    | _ => pure ()
    let extra := (axiomsOf env n).filter (fun a => !allowed.contains a)
    unless extra.isEmpty do
      bad := bad.push s!"{n} depends on {extra.toList}"
  let mut unproved : Array Name := #[]
  for s in specs do
    unless theoremTypes.any (·.isConstOf s) do unproved := unproved.push s
  for s in unproved do bad := bad.push s!"SPEC without a theorem of exactly this type: {s}"
  IO.println s!"scope={roots} declarations={decls} theorems={thms} specs={specs.size} violations={bad.size}"
  for b in bad.qsort (· < ·) do IO.println s!"  {b}"
  return if bad.isEmpty then 0 else 1
