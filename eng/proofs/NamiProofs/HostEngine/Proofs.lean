import NamiProofs.HostEngine.Spec
import NamiProofs.HostEngine.Proofs.Framing
import NamiProofs.HostEngine.Proofs.Host
import NamiProofs.HostEngine.Proofs.Engine
import NamiProofs.HostEngine.Proofs.System
import NamiProofs.HostEngine.Design

/-!
# Verdicts

Each `Verdict.*` from `Spec.lean`, proved. A held claim is proved for every reachable state
of the model; a refuted claim is proved false by a reachable counterexample (the traces are
named in the cited theorems). The claims that were refuted on the code at 843383e are false
for the broken models of `Mutants.lean`, which are that code.
-/

namespace NamiProofs.HostEngine.Verdict

open NamiProofs.HostEngine

theorem nothingWaitsOnADeadEngine : Verdict.NothingWaitsOnADeadEngine :=
  fun s hr hc hx => Host.nothingWaitsOnADeadEngine s hr hc hx

theorem deadEngineFailsToolCallsUnavailable : Verdict.DeadEngineFailsToolCallsUnavailable :=
  fun s hr hx hc p hp hv s' hs => Host.deadEngineFailsToolCallsUnavailable s hr hx hc p hp hv s' hs

theorem noOrphanWhenHostExits : Verdict.NoOrphanWhenHostExits :=
  fun s hr n hx => Host.noOrphanWhenHostExits s hr n hx

theorem health503OnEngineError : Verdict.Health503OnEngineError :=
  fun s hr hx he p hp hv s' hs => Host.health503OnEngineError s hr hx he p hp hv s' hs

theorem cancelCompletesWaitingCall : Verdict.CancelCompletesWaitingCall :=
  fun s hr hx p hp => Host.cancelCompletesWaitingCall s hr hx p hp

theorem engineCanAlwaysExitAfterTheLoopEnds : Verdict.EngineCanAlwaysExitAfterTheLoopEnds :=
  Engine.engineCanAlwaysExitAfterTheLoopEnds

theorem readsKeepFrameIdentity : Verdict.ReadsKeepFrameIdentity :=
  Framing.readAll_chunking_invariant

theorem hostExitsNonZeroWhenEngineDies : Verdict.HostExitsNonZeroWhenEngineDies :=
  Host.hostExitsNonZeroWhenEngineDies

theorem health503WhileEngineGone : Verdict.Health503WhileEngineGone := Host.health503WhileEngineGone

theorem healthAnswersAreReports : Verdict.HealthAnswersAreReports := Host.healthAnswersAreReports

theorem badFrameNotFatal : Verdict.BadFrameNotFatal := Engine.badFrameNotFatal

theorem badFrameKeepsOtherCalls : Verdict.BadFrameKeepsOtherCalls := Engine.badFrameKeepsOtherCalls

theorem wrongRevisionRefusedBeforeDispatch : Verdict.WrongRevisionRefusedBeforeDispatch :=
  Engine.wrongRevisionRefusedBeforeDispatch

theorem duplicateOrFullRefusedForThatId : Verdict.DuplicateOrFullRefusedForThatId :=
  Engine.duplicateOrFullRefusedForThatId

theorem onlyEndOfInputEndsTheLoop : Verdict.OnlyEndOfInputEndsTheLoop := Engine.onlyEndOfInputEndsTheLoop

theorem correctHostNeverEndsTheEngine : Verdict.CorrectHostNeverEndsTheEngine :=
  System.correctHostNeverEndsTheEngine

theorem slotsAgreeToday : Verdict.SlotsAgreeToday := Design.today_noRefusal_refuted
theorem slotsAgreeWithRemoveOnCancelOnly : Verdict.SlotsAgreeWithRemoveOnCancelOnly :=
  Design.removeOnCancel_alone_refuted
theorem slotsAgreeWithRemoveBeforeReplyOnly : Verdict.SlotsAgreeWithRemoveBeforeReplyOnly :=
  Design.removeBeforeReply_alone_refuted
theorem slotsAgreeWithBothChanges : Verdict.SlotsAgreeWithBothChanges := Design.fixed_noRefusal

end NamiProofs.HostEngine.Verdict

/-! The held claims also as theorems of exactly their `Spec` type, so that
`lean_audit.sh NamiProofs --spec NamiProofs.HostEngine.Spec` lists exactly the refuted
claims as unproved: the three variants of the slot accounting that do not hold. -/

namespace NamiProofs.HostEngine.Spec
theorem nothingWaitsOnADeadEngine_holds : Spec.NothingWaitsOnADeadEngine := Verdict.nothingWaitsOnADeadEngine
theorem deadEngineFailsToolCallsUnavailable_holds : Spec.DeadEngineFailsToolCallsUnavailable :=
  Verdict.deadEngineFailsToolCallsUnavailable
theorem noOrphanWhenHostExits_holds : Spec.NoOrphanWhenHostExits := Verdict.noOrphanWhenHostExits
theorem health503OnEngineError_holds : Spec.Health503OnEngineError := Verdict.health503OnEngineError
theorem cancelCompletesWaitingCall_holds : Spec.CancelCompletesWaitingCall := Verdict.cancelCompletesWaitingCall
theorem engineCanAlwaysExitAfterTheLoopEnds_holds : Spec.EngineCanAlwaysExitAfterTheLoopEnds :=
  Verdict.engineCanAlwaysExitAfterTheLoopEnds
theorem readsKeepFrameIdentity_holds : Spec.ReadsKeepFrameIdentity := Verdict.readsKeepFrameIdentity
theorem slotsAgreeWithBothChanges_holds : Spec.SlotsAgreeWithBothChanges := Verdict.slotsAgreeWithBothChanges
theorem hostExitsNonZeroWhenEngineDies_holds : Spec.HostExitsNonZeroWhenEngineDies :=
  Verdict.hostExitsNonZeroWhenEngineDies
theorem health503WhileEngineGone_holds : Spec.Health503WhileEngineGone := Verdict.health503WhileEngineGone
theorem healthAnswersAreReports_holds : Spec.HealthAnswersAreReports := Verdict.healthAnswersAreReports
theorem badFrameNotFatal_holds : Spec.BadFrameNotFatal := Verdict.badFrameNotFatal
theorem badFrameKeepsOtherCalls_holds : Spec.BadFrameKeepsOtherCalls := Verdict.badFrameKeepsOtherCalls
theorem wrongRevisionRefusedBeforeDispatch_holds : Spec.WrongRevisionRefusedBeforeDispatch :=
  Verdict.wrongRevisionRefusedBeforeDispatch
theorem duplicateOrFullRefusedForThatId_holds : Spec.DuplicateOrFullRefusedForThatId :=
  Verdict.duplicateOrFullRefusedForThatId
theorem onlyEndOfInputEndsTheLoop_holds : Spec.OnlyEndOfInputEndsTheLoop := Verdict.onlyEndOfInputEndsTheLoop
theorem correctHostNeverEndsTheEngine_holds : Spec.CorrectHostNeverEndsTheEngine :=
  Verdict.correctHostNeverEndsTheEngine
end NamiProofs.HostEngine.Spec
