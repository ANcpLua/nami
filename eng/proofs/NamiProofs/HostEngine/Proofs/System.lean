import NamiProofs.HostEngine.Spec
import NamiProofs.HostEngine.Proofs.Engine

/-! Proofs for the composed host and engine. -/

namespace NamiProofs.HostEngine.System

open NamiProofs.HostEngine

instance : Inhabited St := ⟨⟨{}, {}, 0, 0⟩⟩

/-- No event of the composition ends the engine's read loop: frames never do
(`Engine.frame_loop`), and the composition has no end of input and no stop signal. -/
theorem step_eng_loop {s s' : St} {e : Ev} (hs : step s e = some s') : s'.eng.loop = s.eng.loop := by
  cases e with
  | host e =>
    cases e <;> simp [step, stepBy] at hs <;> (obtain ⟨_, _, rfl⟩ := hs; rfl)
  | deliver =>
    simp only [step, stepBy] at hs
    split at hs
    · simp only [Option.map_eq_some_iff] at hs
      obtain ⟨g, hg, rfl⟩ := hs
      exact Engine.frame_loop hg
    · simp at hs
  | eng e =>
    cases e <;> simp [step, stepBy] at hs <;>
      (obtain ⟨g, hg, rfl⟩ := hs; exact Engine.step_loop hg (by simp) (by simp) (by simp))
  | deliverUndecodable =>
    simp only [step, stepBy] at hs
    split at hs
    · simp only [Option.map_eq_some_iff] at hs
      obtain ⟨g, hg, rfl⟩ := hs
      exact Engine.frame_loop hg
    · simp only [Option.map_eq_some_iff] at hs
      obtain ⟨g, hg, rfl⟩ := hs
      exact Engine.frame_loop hg
    · simp at hs
  | receive =>
    simp only [step, stepBy] at hs
    split at hs
    · simp only [Option.map_eq_some_iff] at hs
      obtain ⟨_, _, rfl⟩ := hs
      rfl
    · simp at hs

theorem run_eng_loop : ∀ (es : List Ev) {s s' : St}, run s es = some s' → s'.eng.loop = s.eng.loop
  | [], _, _, hr => by simp [run, runBy] at hr; subst hr; rfl
  | e :: es, s, s', hr => by
    simp only [run, runBy] at hr
    cases hs : stepBy Engine.step s e with
    | none => simp [hs] at hr
    | some s1 =>
      simp only [hs, Option.bind_some] at hr
      rw [run_eng_loop es hr, step_eng_loop hs]

/-- No frame a correct host sends ends the engine: its read loop is still reading in every
reachable state. -/
theorem correctHostNeverEndsTheEngine : Claim.CorrectHostNeverEndsTheEngine Engine.step := by
  intro s ⟨es, hr⟩
  rw [run_eng_loop es hr]
  decide

/-! ## The two traces that ended the engine before the slot accounting was fixed -/

/-- 32 tool calls, all written and all read by the engine. -/
def fill : List Ev :=
  List.replicate 32 (.host (.requestTool true)) ++ List.replicate 32 .deliver

/-- Trace A: a caller aborts call 32 at the host's limit and a new call arrives. The host has
dropped 32 from its 32 waiting entries (engine.ts, `abort`) and sends call 33. The engine
removes entry 32 when it reads the cancel (`Accept`), so call 33 finds 31 entries. -/
def cancelAtFullQueue : List Ev :=
  fill ++ [.host (.abort 32), .host (.requestTool true), .deliver, .deliver]

/-- Trace B: the engine removes entry 1 and writes its reply (`Reply`); the host reads the
reply, frees the slot and sends call 33, which the engine reads before the task of call 1
has ended. -/
def replyBeforeRelease : List Ev :=
  fill ++ [.eng (.enter 1), .eng (.respond 1 false), .receive, .host (.requestTool true), .deliver]

/-- What the traces show: the loop, both tables, and whether any call was refused. -/
def slots (s : St) : Engine.Loop × Nat × Nat × Bool :=
  (s.eng.loop, s.host.pending.length, s.eng.pending.length, s.eng.out.any (·.2))

theorem cancelAtFullQueue_accepted :
    (run init cancelAtFullQueue).map slots = some (.reading, 32, 32, false) := by decide

theorem replyBeforeRelease_accepted :
    (run init replyBeforeRelease).map slots = some (.reading, 32, 32, false) := by decide

/-- One call whose content the engine cannot decode is answered with an error for its id. -/
def undecodableCall : List Ev := [.host (.requestTool true), .deliverUndecodable]

theorem undecodableCall_refused :
    (run init undecodableCall).map (fun s => (s.eng.loop, s.eng.out)) = some (.reading, [(1, true)]) := by
  decide

end NamiProofs.HostEngine.System
