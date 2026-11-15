import NamiProofs.HostEngine.Framing
import NamiProofs.HostEngine.Host
import NamiProofs.HostEngine.Engine
import NamiProofs.HostEngine.System
import NamiProofs.HostEngine.Design

/-!
# Claims, written from the contract

Sources: plan packages P04 ("The host ends when the engine ends; `/health` is honest and
reachable") and P14 ("Private pipe: a bad frame is refused, not fatal") in `nami-plan.md`,
and the contract text of `contracts/models/engine.tsp` ("A tool refusal is an explicit error
value, never a lost reply"; "Fixed bounds for the private protocol, shared by both
implementations").

`Claim.*` states each claim for any step function; `Spec.*` instantiates it for the model of
the code as it is (closed propositions). `Verdict.*` records the result for the code:
the claim itself where it holds, its negation where a counterexample refutes it. Every claim
about the code holds now; the three refuted verdicts that remain are about the slot
accounting of `Design.lean` before the fix and with half of it. Every
`Verdict` is the exact type of a theorem in `Proofs.lean`, which `lean_audit.sh --spec`
checks. Editing this file reopens review.
-/

namespace NamiProofs.HostEngine

open NamiProofs.HostEngine

namespace Claim

/-! ## Host (P04) -/

/-- P04: "waiting calls get the typed, retryable `collector_unavailable` error" — part 1:
once the engine process has ended, no caller of the running host is still waiting. -/
def NothingWaitsOnADeadEngine (stp : Host.St → Host.Ev → Option Host.St) : Prop :=
  ∀ s, Host.ReachableBy stp s → s.child = .exited → s.exit = none → s.pending = []

/-- P04 part 2: when the engine dies, every tool call that waits on it ends with typed
`collector_unavailable` (retryable). -/
def DeadEngineFailsToolCallsUnavailable (stp : Host.St → Host.Ev → Option Host.St) : Prop :=
  ∀ s, Host.ReachableBy stp s → s.exit = none → s.child = .running →
    ∀ p ∈ s.pending, p.via = .tool →
      ∀ s', stp s .childExit = some s' → (⟨p.ticket, .tool, .unavailable⟩ : Host.Done) ∈ s'.done

/-- P04: "the Bun host exits non-zero within seconds" of the engine's death: from any state
after readiness where the engine has died and nobody asked the host to stop, events that
need no client, operator or platform lead to an exit with a non-zero code. -/
def HostExitsNonZeroWhenEngineDies (stp : Host.St → Host.Ev → Option Host.St) : Prop :=
  ∀ s, Host.ReachableBy stp s → s.ready = true → s.child = .exited → s.exit = none →
    s.closing = false →
    ∃ es : List Host.Ev, es.all Host.Ev.internal = true ∧
      ∃ s' n, Host.runBy stp s es = some s' ∧ s'.exit = some (.code n) ∧ n ≠ 0

/-- P04: "no engine process is left behind": whenever the host ends by itself (any exit
code), the engine has ended or has been sent SIGKILL. -/
def NoOrphanWhenHostExits (stp : Host.St → Host.Ev → Option Host.St) : Prop :=
  ∀ s, Host.ReachableBy stp s → ∀ n, s.exit = some (.code n) → s.child = .exited ∨ s.sigkill = true

/-- P04: "`/health` answers 503 in the time between the engine's failure and the host's
exit". -/
def Health503WhileEngineGone (stp : Host.St → Host.Ev → Option Host.St) : Prop :=
  ∀ s, Host.ReachableBy stp s → s.ready = true → s.stopped = true → s.closing = false →
    s.exit = none →
    ∀ s', stp s .requestHealth = some s' →
      (⟨s.nextTicket, .health, .http 503⟩ : Host.Done) ∈ s'.done

/-- P04: "`/health` answers 503 ... whenever the engine reports an error". -/
def Health503OnEngineError (stp : Host.St → Host.Ev → Option Host.St) : Prop :=
  ∀ s, Host.ReachableBy stp s → s.exit = none → s.hasExited = false →
    ∀ p ∈ s.pending, p.via = .health →
      ∀ s', stp s (.reply p.id true) = some s' → (⟨p.ticket, .health, .http 503⟩ : Host.Done) ∈ s'.done

/-- P04 title: "`/health` is honest": every answer to `GET /health` is a health report with
200 or 503 (or the client went away); never an unhandled 500. -/
def HealthAnswersAreReports (stp : Host.St → Host.Ev → Option Host.St) : Prop :=
  ∀ s, Host.ReachableBy stp s → ∀ d ∈ s.done, d.via = .health →
    d.outcome = .http 200 ∨ d.outcome = .http 503 ∨ d.outcome = .cancelled

/-- P14: "A cancel during a call completes the waiting call on the host side": the abort
ends that caller at once, frees its slot, and forwards a cancel frame to the engine. -/
def CancelCompletesWaitingCall (stp : Host.St → Host.Ev → Option Host.St) : Prop :=
  ∀ s, Host.ReachableBy stp s → s.exit = none → ∀ p ∈ s.pending,
    ∃ s', stp s (.abort p.id) = some s' ∧ (⟨p.ticket, p.via, .cancelled⟩ : Host.Done) ∈ s'.done ∧
      (∀ q ∈ s'.pending, q.id ≠ p.id) ∧ s'.sent = s.sent ++ [.cancel p.id]

/-! ## Engine (P14) -/

def badFrame : Engine.Frame → Bool
  | .garbage | .oversize | .undecodable _ | .req _ _ false | .cancel _ false => true
  | _ => false

/-- P14: "A frame that cannot be parsed or is too large is dropped ... A frame with a wrong
contract revision is refused": a bad frame does not end the read loop. -/
def BadFrameNotFatal (stp : Engine.St → Engine.Ev → Option Engine.St) : Prop :=
  ∀ s, Engine.ReachableBy stp s → s.loop = .reading → ∀ f, badFrame f = true →
    ∀ s', stp s (.frame f) = some s' → s'.loop = .reading

/-- P14: "All other running calls finish": a bad frame leaves every other accepted call as
it was (in particular, not cancelled). -/
def BadFrameKeepsOtherCalls (stp : Engine.St → Engine.Ev → Option Engine.St) : Prop :=
  ∀ s, Engine.ReachableBy stp s → s.loop = .reading → ∀ f, badFrame f = true →
    ∀ s', stp s (.frame f) = some s' → ∀ c ∈ s.pending, c ∈ s'.pending

/-- P14: "A frame with a wrong contract revision is refused for that call before dispatch":
the loop keeps reading, nothing is dispatched, and that id gets an error reply. -/
def WrongRevisionRefusedBeforeDispatch (stp : Engine.St → Engine.Ev → Option Engine.St) : Prop :=
  ∀ s, Engine.ReachableBy stp s → s.loop = .reading → ∀ id m s',
    stp s (.frame (.req id m false)) = some s' →
      s'.loop = .reading ∧ s'.pending = s.pending ∧ (id, true) ∈ s'.out

/-- P14: "A duplicate id or a full queue is refused for that id": the loop keeps reading and
the calls already accepted are untouched. -/
def DuplicateOrFullRefusedForThatId (stp : Engine.St → Engine.Ev → Option Engine.St) : Prop :=
  ∀ s, Engine.ReachableBy stp s → s.loop = .reading → ∀ id m,
    ((Engine.ids s).contains id = true ∨ s.pending.length ≥ Engine.maxPending) →
    ∀ s', stp s (.frame (.req id m true)) = some s' →
      s'.loop = .reading ∧ ∀ c ∈ s.pending, c ∈ s'.pending

/-- P14: "Only end of input ends the read loop" (the host's stop signal also may). -/
def OnlyEndOfInputEndsTheLoop (stp : Engine.St → Engine.Ev → Option Engine.St) : Prop :=
  ∀ s, Engine.ReachableBy stp s → s.loop = .reading → ∀ e s', stp s e = some s' →
    s'.loop ≠ .reading → e = .eof ∨ e = .eofPartial ∨ e = .stop

/-- What "no orphan" needs from the engine: once its read loop has ended (end of input,
stop signal or fault), the engine can always reach its exit; every waiting or running call
can end. -/
def EngineCanAlwaysExitAfterTheLoopEnds (stp : Engine.St → Engine.Ev → Option Engine.St) : Prop :=
  ∀ s, Engine.ReachableBy stp s → s.loop ≠ .reading →
    ∃ es s', Engine.runBy stp s es = some s' ∧ s'.exit.isSome = true

end Claim

/-! ## Framing (P14, D3) -/

namespace Claim

/-- P14: `EngineProtocol_FragmentedAndCoalescedReads_KeepRequestIdentity`: both readers
(EngineProtocol.cs:57-89, engine.ts:44-57) give the same frames, and the same fault, for any
cutting of the byte stream into reads as for one read of the whole stream. -/
def ReadsKeepFrameIdentity (onChunk : Nat → Framing.Reader → List Nat → Option Framing.Reader) : Prop :=
  ∀ lim (cs : List (List Nat)),
    Framing.readAllBy onChunk lim cs = Framing.readAllBy onChunk lim [cs.flatten]

end Claim

/-! ## Host and engine together (P14 limits shared by both implementations) -/

namespace Claim

/-- engine.tsp: "Fixed bounds for the private protocol, shared by both implementations":
the host's bound of 32 waiting requests keeps every frame it sends acceptable to the
engine, so no frame a correct host sends ends the engine. -/
def CorrectHostNeverEndsTheEngine (estp : Engine.St → Engine.Ev → Option Engine.St) : Prop :=
  ∀ s, System.ReachableBy estp s → s.eng.loop ≠ .faulted

end Claim

namespace Spec
def NothingWaitsOnADeadEngine : Prop := Claim.NothingWaitsOnADeadEngine Host.step
def DeadEngineFailsToolCallsUnavailable : Prop := Claim.DeadEngineFailsToolCallsUnavailable Host.step
def HostExitsNonZeroWhenEngineDies : Prop := Claim.HostExitsNonZeroWhenEngineDies Host.step
def NoOrphanWhenHostExits : Prop := Claim.NoOrphanWhenHostExits Host.step
def Health503WhileEngineGone : Prop := Claim.Health503WhileEngineGone Host.step
def Health503OnEngineError : Prop := Claim.Health503OnEngineError Host.step
def HealthAnswersAreReports : Prop := Claim.HealthAnswersAreReports Host.step
def CancelCompletesWaitingCall : Prop := Claim.CancelCompletesWaitingCall Host.step
def BadFrameNotFatal : Prop := Claim.BadFrameNotFatal Engine.step
def BadFrameKeepsOtherCalls : Prop := Claim.BadFrameKeepsOtherCalls Engine.step
def WrongRevisionRefusedBeforeDispatch : Prop := Claim.WrongRevisionRefusedBeforeDispatch Engine.step
def DuplicateOrFullRefusedForThatId : Prop := Claim.DuplicateOrFullRefusedForThatId Engine.step
def OnlyEndOfInputEndsTheLoop : Prop := Claim.OnlyEndOfInputEndsTheLoop Engine.step
def EngineCanAlwaysExitAfterTheLoopEnds : Prop := Claim.EngineCanAlwaysExitAfterTheLoopEnds Engine.step
def ReadsKeepFrameIdentity : Prop := Claim.ReadsKeepFrameIdentity Framing.onChunk
def CorrectHostNeverEndsTheEngine : Prop := Claim.CorrectHostNeverEndsTheEngine Engine.step
/-- engine.tsp: shared bounds. The slot accounting as it was at 843383e (`Design.lean`). -/
def SlotsAgreeToday : Prop := Design.NoRefusal Design.today
/-- With only "a cancel frame removes the entry". -/
def SlotsAgreeWithRemoveOnCancelOnly : Prop := Design.NoRefusal ⟨true, false⟩
/-- With only "remove the entry before writing the reply". -/
def SlotsAgreeWithRemoveBeforeReplyOnly : Prop := Design.NoRefusal ⟨false, true⟩
/-- With both changes, which is the engine as it is: `Accept` and `Reply` in EngineProtocol.cs. -/
def SlotsAgreeWithBothChanges : Prop := Design.NoRefusal Design.fixed
end Spec

namespace Verdict
-- Held on the code as it is.
def NothingWaitsOnADeadEngine : Prop := Spec.NothingWaitsOnADeadEngine
def DeadEngineFailsToolCallsUnavailable : Prop := Spec.DeadEngineFailsToolCallsUnavailable
def NoOrphanWhenHostExits : Prop := Spec.NoOrphanWhenHostExits
def Health503OnEngineError : Prop := Spec.Health503OnEngineError
def CancelCompletesWaitingCall : Prop := Spec.CancelCompletesWaitingCall
def EngineCanAlwaysExitAfterTheLoopEnds : Prop := Spec.EngineCanAlwaysExitAfterTheLoopEnds
def ReadsKeepFrameIdentity : Prop := Spec.ReadsKeepFrameIdentity
-- Held since the engine refuses instead of ending (P14) and frees a slot when the host does (A1 = H3).
def BadFrameNotFatal : Prop := Spec.BadFrameNotFatal
def BadFrameKeepsOtherCalls : Prop := Spec.BadFrameKeepsOtherCalls
def WrongRevisionRefusedBeforeDispatch : Prop := Spec.WrongRevisionRefusedBeforeDispatch
def DuplicateOrFullRefusedForThatId : Prop := Spec.DuplicateOrFullRefusedForThatId
def OnlyEndOfInputEndsTheLoop : Prop := Spec.OnlyEndOfInputEndsTheLoop
def CorrectHostNeverEndsTheEngine : Prop := Spec.CorrectHostNeverEndsTheEngine
-- Held since the host ends when its engine has failed and `/health` catches (P04).
def HostExitsNonZeroWhenEngineDies : Prop := Spec.HostExitsNonZeroWhenEngineDies
def Health503WhileEngineGone : Prop := Spec.Health503WhileEngineGone
def HealthAnswersAreReports : Prop := Spec.HealthAnswersAreReports
-- The slot accounting (design model): the code before the fix, each change alone, and the fix.
def SlotsAgreeToday : Prop := ¬ Spec.SlotsAgreeToday
def SlotsAgreeWithRemoveOnCancelOnly : Prop := ¬ Spec.SlotsAgreeWithRemoveOnCancelOnly
def SlotsAgreeWithRemoveBeforeReplyOnly : Prop := ¬ Spec.SlotsAgreeWithRemoveBeforeReplyOnly
def SlotsAgreeWithBothChanges : Prop := Spec.SlotsAgreeWithBothChanges
end Verdict

end NamiProofs.HostEngine
