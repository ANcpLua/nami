import NamiStorage.Queue.Refutations
/-
For every claim in `Spec.Claims`, a deliberately broken model that breaks it, with the run that shows
it. A claim that no broken model can break would be vacuous; these show each one bites. Each broken
model changes one step of `step` the way a plausible code change would.
-/
namespace Nami.Storage.Queue.Broken
open Nami.Storage.Queue
open Nami.Storage.Spec

/-- `TrySetResult` before the transaction commits. -/
def ackBeforeCommit (s : State) : Event → Option State
  | .finish => match s.writer with
      | .running j _ _ => some { s with writer := .idle, job := upd s.job j { s.job j with outcome := .ok } }
      | _ => none
  | e => step s e

def ackTrace : List Event := [.begin .ingest 0, .tryWrite 0, .arm 0, .dequeue, .finish]
def ackState : State := (runBy ackBeforeCommit (init 1) ackTrace).getD (init 1)
theorem ackState_run : runBy ackBeforeCommit (init 1) ackTrace = some ackState := rfl

theorem breaks_noLostAcknowledgedWrite : ¬ Claims.NoLostAcknowledgedWrite ackBeforeCommit := by
  intro h
  have := h 1 ackState (runBy_reachable _ _ _ .init ackState_run) 0 rfl
  revert this; decide

/-- The writer may commit the same job twice (no `committed` guard). -/
def doubleCommit (s : State) : Event → Option State
  | .commit => match s.writer with
      | .running j 0 _ => some { s with writer := .running j 0 true, db := s.db ++ [j] }
      | _ => none
  | e => step s e

def doubleTrace : List Event := [.begin .ingest 0, .tryWrite 0, .arm 0, .dequeue, .commit, .commit]
def doubleState : State := (runBy doubleCommit (init 1) doubleTrace).getD (init 1)
theorem doubleState_run : runBy doubleCommit (init 1) doubleTrace = some doubleState := rfl

theorem breaks_noDuplicatedWrite : ¬ Claims.NoDuplicatedWrite doubleCommit := by
  intro h
  have := h 1 doubleState (runBy_reachable _ _ _ .init doubleState_run)
  revert this; decide

/-- A cancellation observed after the commit reports "cancelled" for a committed job. -/
def abortAfterCommit (s : State) : Event → Option State
  | .abort => match s.writer with
      | .running j _ _ =>
          if tokenCancelled s j then
            some { s with writer := .idle, job := upd s.job j { s.job j with outcome := .canceled } }
          else none
      | _ => none
  | e => step s e

def abortTrace : List Event := [.begin .read 0, .tryWrite 0, .arm 0, .dequeue, .commit, .cancel 0, .abort]
def abortState : State := (runBy abortAfterCommit (init 1) abortTrace).getD (init 1)
theorem abortState_run : runBy abortAfterCommit (init 1) abortTrace = some abortState := rfl

theorem breaks_reportedFailureMeansNotWritten :
    ¬ Claims.ReportedFailureMeansNotWritten abortAfterCommit := by
  intro h
  have := h 1 abortState (runBy_reachable _ _ _ .init abortState_run) 0 (Or.inl rfl)
  revert this; decide

/-- `CancelBeforeClaim` without the compare-exchange: a late cancellation overwrites a result. -/
def cancelOverwrites (s : State) : Event → Option State
  | .cancel j =>
      if (s.job j).stage = .absent then none
      else some { s with job := upd s.job j { s.job j with callerCancelled := true, outcome := .canceled } }
  | e => step s e

def overwriteTrace : List Event := [.begin .ingest 0, .tryWrite 0, .arm 0, .dequeue, .commit, .finish]
def overwriteState : State := (runBy cancelOverwrites (init 1) overwriteTrace).getD (init 1)
theorem overwriteState_run : runBy cancelOverwrites (init 1) overwriteTrace = some overwriteState := rfl

theorem breaks_resultIsFinal : ¬ Claims.ResultIsFinal cancelOverwrites := by
  intro h
  have := h 1 overwriteState (.cancel 0) _ (runBy_reachable _ _ _ .init overwriteState_run) rfl 0
    (by decide)
  revert this; decide

/-- `TryClaim` without the compare-exchange: the writer runs a job its caller already cancelled. -/
def claimWithoutCas (s : State) : Event → Option State
  | .dequeue => match s.writer, s.queue with
      | .idle, j :: rest =>
          some { s with queue := rest, job := upd s.job j { s.job j with stage := .taken },
                        writer := .running j (s.job j).work false }
      | _, _ => none
  | e => step s e

def casTrace : List Event := [.begin .ingest 0, .tryWrite 0, .arm 0, .cancel 0, .dequeue]
def casState : State := (runBy claimWithoutCas (init 1) casTrace).getD (init 1)
theorem casState_run : runBy claimWithoutCas (init 1) casTrace = some casState := rfl

theorem breaks_cancelledBeforeClaimNeverRuns : ¬ Claims.CancelledBeforeClaimNeverRuns claimWithoutCas := by
  intro h
  have := (h 1 casState (runBy_reachable _ _ _ .init casState_run) 0 rfl).2 0 false
  exact this rfl

/-- `ArmCancellation` that ignores a token cancelled before it registered. -/
def armMissesCancel (s : State) : Event → Option State
  | .arm j =>
      let x := s.job j
      if (x.stage = .queued ∨ x.stage = .taken) ∧ x.armed = false then
        some { s with job := upd s.job j { x with armed := true } }
      else none
  | e => step s e

def armTrace : List Event := [.begin .read 0, .tryWrite 0, .cancel 0, .arm 0]
def armState : State := (runBy armMissesCancel (init 1) armTrace).getD (init 1)
theorem armState_run : runBy armMissesCancel (init 1) armTrace = some armState := rfl

theorem breaks_cancelledCallerReleasedAtOnce : ¬ Claims.CancelledCallerReleasedAtOnce armMissesCancel := by
  intro h
  have := h 1 armState (runBy_reachable _ _ _ .init armState_run) 0 rfl rfl (by decide)
  exact this rfl

/-- Work that does not check the token between units. -/
def progressIgnoresToken (s : State) : Event → Option State
  | .progress => match s.writer with
      | .running j (n + 1) false => some { s with writer := .running j n false }
      | _ => none
  | e => step s e

def progressTrace : List Event := [.begin .read 2, .tryWrite 0, .arm 0, .dequeue, .cancel 0]
def progressState : State := (runBy progressIgnoresToken (init 1) progressTrace).getD (init 1)
theorem progressState_run : runBy progressIgnoresToken (init 1) progressTrace = some progressState := rfl

theorem breaks_cancelledJobReleasesWriter : ¬ Claims.CancelledJobReleasesWriter progressIgnoresToken := by
  intro h
  have := (h 1 progressState (runBy_reachable _ _ _ .init progressState_run) 0 2 rfl rfl).1 .progress _ rfl rfl
  revert this; decide

/-- A job with no way to fail and no way to make progress. -/
def stuck (s : State) : Event → Option State
  | .fail | .progress => none
  | e => step s e

def stuckTrace : List Event := [.begin .ingest 1, .tryWrite 0, .arm 0, .dequeue]
def stuckState : State := (runBy stuck (init 1) stuckTrace).getD (init 1)
theorem stuckState_run : runBy stuck (init 1) stuckTrace = some stuckState := rfl

theorem stuckState_no_writer_step : ∀ e, e.isWriter = true → stuck stuckState e = none := by
  intro e he
  cases e <;> first | rfl | (simp [Event.isWriter] at he)

theorem breaks_writerNeverStuck : ¬ Claims.WriterNeverStuck stuck := by
  intro h
  obtain ⟨e, s', he, hs⟩ := h 1 stuckState (runBy_reachable _ _ _ .init stuckState_run)
    (Or.inl (by decide)) (by decide)
  rw [stuckState_no_writer_step e he] at hs
  cases hs

/-- The writer's `finally` without its drain loop. -/
def exitWithoutDrain (s : State) : Event → Option State
  | .exit => match s.writer with
      | .idle =>
          if (s.completed ∧ s.queue = []) ∨ s.shutdownCancelled then
            some { s with writer := .stopped, queue := [] }
          else none
      | _ => none
  | e => step s e

def drainTrace : List Event := [.begin .ingest 0, .tryWrite 0, .dispose, .complete, .shutdownCancel, .exit]
def drainState : State := (runBy exitWithoutDrain (init 1) drainTrace).getD (init 1)
theorem drainState_run : runBy exitWithoutDrain (init 1) drainTrace = some drainState := rfl

theorem breaks_shutdownLeavesNoPendingQueuedJob :
    ¬ Claims.ShutdownLeavesNoPendingQueuedJob exitWithoutDrain := by
  intro h
  have := h 1 drainState (runBy_reachable _ _ _ .init drainState_run) rfl 0 (Or.inl rfl)
  exact this rfl

/-- `TryWrite` on a channel shutdown has already completed. -/
def acceptAfterComplete (s : State) : Event → Option State
  | .tryWrite j =>
      let x := s.job j
      if x.stage ≠ .begun then none
      else if s.queue.length < s.cap then
        some { s with job := upd s.job j { x with stage := .queued }, queue := s.queue ++ [j] }
      else none
  | e => step s e

def acceptTrace : List Event := [.begin .ingest 0, .dispose, .complete]
def acceptState : State := (runBy acceptAfterComplete (init 1) acceptTrace).getD (init 1)
theorem acceptState_run : runBy acceptAfterComplete (init 1) acceptTrace = some acceptState := rfl

theorem breaks_noJobAcceptedAfterComplete : ¬ Claims.NoJobAcceptedAfterComplete acceptAfterComplete := by
  intro h
  have := (h 1 acceptState 0 _ (runBy_reachable _ _ _ .init acceptState_run) rfl rfl).1
  revert this; decide

/-- A read served from a stale view: it misses writes acknowledged before it was queued. -/
def staleRead (s : State) : Event → Option State
  | .dequeue => (step s .dequeue).map fun s' => match s.queue with
      | j :: _ => if (s.job j).kind = .read then { s' with seen := upd s'.seen j [] } else s'
      | [] => s'
  | e => step s e

def staleTrace : List Event :=
  [.begin .ingest 0, .tryWrite 0, .arm 0, .dequeue, .commit, .finish,
   .begin .read 0, .tryWrite 1, .arm 1, .dequeue]
def staleState : State := (runBy staleRead (init 1) staleTrace).getD (init 1)
theorem staleState_run : runBy staleRead (init 1) staleTrace = some staleState := rfl

theorem breaks_readSeesEveryEarlierAcknowledgedWrite :
    ¬ Claims.ReadSeesEveryEarlierAcknowledgedWrite staleRead := by
  intro h
  have := h 1 staleState (runBy_reachable _ _ _ .init staleState_run) 1 rfl rfl 0 (by decide)
  revert this; decide

/-- A read assembled from tables read at different moments: it misses an earlier commit. -/
def nonCutRead (s : State) : Event → Option State
  | .dequeue => (step s .dequeue).map fun s' => match s.queue with
      | j :: _ => if (s.job j).kind = .read then { s' with seen := upd s'.seen j (s.db.drop 1) } else s'
      | [] => s'
  | e => step s e

def cutTrace : List Event :=
  [.begin .ingest 0, .tryWrite 0, .arm 0, .dequeue, .commit, .finish,
   .begin .ingest 0, .tryWrite 1, .arm 1, .dequeue, .commit, .finish,
   .begin .read 0, .tryWrite 2, .arm 2, .dequeue]
def cutState : State := (runBy nonCutRead (init 1) cutTrace).getD (init 1)
theorem cutState_run : runBy nonCutRead (init 1) cutTrace = some cutState := rfl

theorem breaks_readIsConsistentCut : ¬ Claims.ReadIsConsistentCut nonCutRead := by
  intro h
  have := h 1 cutState (runBy_reachable _ _ _ .init cutState_run) 2 rfl rfl
  rw [← List.isPrefixOf_iff_prefix] at this
  revert this; decide

/-- A replay that reads the live tables again instead of the stored copy. -/
def replayRereads (s : State) : Event → Option State
  | .dequeue => (step s .dequeue).map fun s' => match s.queue with
      | j :: _ => match (s.job j).kind with
          | .replay _ => { s' with seen := upd s'.seen j s.db }
          | _ => s'
      | [] => s'
  | e => step s e

def rereadTrace : List Event :=
  [.begin .read 0, .tryWrite 0, .arm 0, .dequeue, .commit, .finish,
   .begin .ingest 0, .tryWrite 1, .arm 1, .dequeue, .commit, .finish,
   .begin (.replay 0) 0, .tryWrite 2, .arm 2, .dequeue, .commit, .finish]
def rereadState : State := (runBy replayRereads (init 1) rereadTrace).getD (init 1)
theorem rereadState_run : runBy replayRereads (init 1) rereadTrace = some rereadState := rfl

theorem breaks_replayReturnsCapturedRead : ¬ Claims.ReplayReturnsCapturedRead replayRereads := by
  intro h
  have := (h 1 rereadState (runBy_reachable _ _ _ .init rereadState_run) 2 0 rfl rfl).2
  revert this; decide

/-- A second connection commits the next queued job while the writer still holds the first. -/
def concurrentCommit (s : State) : Event → Option State
  | .commit => (step s .commit).map fun s' => match s.queue with
      | k :: _ => { s' with db := s'.db ++ [k] }
      | [] => s'
  | e => step s e

def concurrentTrace : List Event :=
  [.begin .read 0, .tryWrite 0, .arm 0, .dequeue, .begin .ingest 0, .tryWrite 1]
def concurrentState : State := (runBy concurrentCommit (init 2) concurrentTrace).getD (init 2)
theorem concurrentState_run : runBy concurrentCommit (init 2) concurrentTrace = some concurrentState := rfl
def concurrentAfter : State := (concurrentCommit concurrentState .commit).getD concurrentState
theorem concurrentAfter_step : concurrentCommit concurrentState .commit = some concurrentAfter := rfl

theorem breaks_writerHeldExclusively : ¬ Claims.WriterHeldExclusively concurrentCommit := by
  intro h
  have := h 2 concurrentState concurrentAfter
    (runBy_reachable _ _ _ .init concurrentState_run)
    (StepsBy.tail .commit (StepsBy.refl _) concurrentAfter_step) 0 0 false 0 true rfl rfl 1 (by decide)
  revert this; decide

end Nami.Storage.Queue.Broken
