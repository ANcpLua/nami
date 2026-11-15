import NamiProofs.HostEngine.Spec

/-! Counterexamples for the composed host and engine. -/

namespace NamiProofs.HostEngine.System

open NamiProofs.HostEngine

/-- 32 tool calls, all written and all read by the engine. -/
def fill : List Ev :=
  List.replicate 32 (.host (.requestTool true)) ++ List.replicate 32 .deliver

/-- Trace A: a caller aborts call 32 at the host's limit and a new call arrives. The host has
dropped 32 from its 32 waiting entries (engine.ts:77) and sends call 33; the engine still
holds 32 until its task unwinds (EngineProtocol.cs:168-175), so call 33 meets
`_pending.Count >= 32` (line 74) and the read loop throws. -/
def cancelAtFullQueue : List Ev :=
  fill ++ [.host (.abort 32), .host (.requestTool true), .deliver, .deliver]

/-- Trace B: the engine writes the reply to call 1 (line 149) before its `finally` removes the
entry (line 173); the host reads the reply, frees the slot (engine.ts:55) and sends call 33,
which the engine reads before that `finally` ran. -/
def replyBeforeRelease : List Ev :=
  fill ++ [.eng (.enter 1), .eng (.respond 1 false), .receive, .host (.requestTool true), .deliver]

theorem cancelAtFullQueue_faults :
    (run init cancelAtFullQueue).map (fun s => (s.eng.loop, s.host.pending.length, s.eng.pending.length)) =
      some (.faulted, 32, 32) := by decide

theorem replyBeforeRelease_faults :
    (run init replyBeforeRelease).map (fun s => (s.eng.loop, s.host.pending.length, s.eng.pending.length)) =
      some (.faulted, 32, 32) := by decide

/-- Trace C: one call whose arguments or trace context the engine cannot decode. -/
def undecodableCall : List Ev := [.host (.requestTool true), .deliverUndecodable]

theorem undecodableCall_faults :
    (run init undecodableCall).map (fun s => s.eng.loop) = some .faulted := by decide

instance : Inhabited St := ⟨⟨{}, {}, 0, 0⟩⟩

theorem correctHostNeverEndsTheEngine_refuted : ¬ Claim.CorrectHostNeverEndsTheEngine := by
  intro hc
  have := hc (run init cancelAtFullQueue).get! ⟨cancelAtFullQueue, by decide⟩
  revert this; decide

end NamiProofs.HostEngine.System
