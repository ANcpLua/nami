import Std

namespace Nami.Agent

inductive Phase where
  | idle | diagnosed | planned | applied | verified | refused | rolledBack
  deriving DecidableEq, Repr

inductive Event where
  | diagnose | plan | apply | verify | refuse | rollback
  deriving DecidableEq, Repr

structure Facts where
  revisionMatches : Bool := false
  targetsOwned : Bool := false
  sourcesPreserved : Bool := false
  validationPassed : Bool := false
  rollbackRestored : Bool := false
  deriving Repr

def step (phase : Phase) (event : Event) (facts : Facts) : Option Phase :=
  match phase, event with
  | .idle, .diagnose => some .diagnosed
  | .diagnosed, .plan => some .planned
  | .planned, .apply =>
      if facts.revisionMatches && facts.targetsOwned && facts.sourcesPreserved
      then some .applied else some .refused
  | .applied, .verify =>
      if facts.revisionMatches && facts.sourcesPreserved && facts.validationPassed
      then some .verified else some .refused
  | .applied, .rollback | .refused, .rollback =>
      if facts.rollbackRestored then some .rolledBack else some .refused
  | .planned, .refuse | .diagnosed, .refuse => some .refused
  | _, _ => none

theorem apply_requires_owned_unchanged_sources (s : Phase) (f : Facts)
    (h : step s .apply f = some .applied) :
    f.revisionMatches = true ∧ f.targetsOwned = true ∧ f.sourcesPreserved = true := by
  cases s <;> simp_all [step, Bool.and_eq_true]

theorem verified_requires_successful_validation (s : Phase) (f : Facts)
    (h : step s .verify f = some .verified) :
    s = .applied ∧ f.revisionMatches = true ∧ f.sourcesPreserved = true ∧ f.validationPassed = true := by
  cases s <;> simp_all [step, Bool.and_eq_true]

theorem rollback_requires_restoration (s : Phase) (f : Facts)
    (h : step s .rollback f = some .rolledBack) : f.rollbackRestored = true := by
  cases s <;> simp_all [step]

theorem terminal_cannot_apply (f : Facts) :
    step .verified .apply f = none ∧ step .rolledBack .apply f = none := by
  simp [step]

theorem verified_only_by_validation (s : Phase) (e : Event) (f : Facts)
    (h : step s e f = some .verified) :
    e = .verify ∧ s = .applied ∧ f.revisionMatches = true ∧
      f.sourcesPreserved = true ∧ f.validationPassed = true := by
  cases s <;> cases e <;> simp_all [step, Bool.and_eq_true]
  all_goals split at h <;> simp_all

def parsePhase : String → Option Phase
  | "idle" => some .idle | "diagnosed" => some .diagnosed | "planned" => some .planned
  | "applied" => some .applied | "verified" => some .verified | "refused" => some .refused
  | "rolledBack" => some .rolledBack | _ => none

def parseEvent : String → Option Event
  | "diagnose" => some .diagnose | "plan" => some .plan | "apply" => some .apply
  | "verify" => some .verify | "refuse" => some .refuse | "rollback" => some .rollback | _ => none

def name : Phase → String
  | .idle => "idle" | .diagnosed => "diagnosed" | .planned => "planned"
  | .applied => "applied" | .verified => "verified" | .refused => "refused" | .rolledBack => "rolledBack"

end Nami.Agent

open Nami.Agent

def main (args : List String) : IO UInt32 := do
  match args with
  | ["proof"] =>
      IO.println "checked: apply ownership/revision; verified validation; rollback restoration; terminal immutability"
      return 0
  | [phase, event, revision, owned, preserved, validated, restored] =>
      match parsePhase phase, parseEvent event with
      | some p, some e =>
          let facts : Facts := ⟨revision == "true", owned == "true", preserved == "true", validated == "true", restored == "true"⟩
          match step p e facts with
          | some next => IO.println (name next); return 0
          | none => IO.eprintln "invalid transition"; return 2
      | _, _ => IO.eprintln "unknown phase/event"; return 2
  | _ => IO.eprintln "expected proof or phase event and five boolean facts"; return 2
