import NamiStorage.Queue.Preservation
import NamiStorage.Queue.Spec
/-
Proofs of the claims in `Spec.lean` for the model of the code.
-/
namespace Nami.Storage
open Queue

theorem noLostAcknowledgedWrite : Spec.NoLostAcknowledgedWrite := by
  intro cap s hs j ho
  exact (inv_reachable hs).ok j ho

theorem noDuplicatedWrite : Spec.NoDuplicatedWrite := by
  intro cap s hs
  exact (inv_reachable hs).dbnodup

theorem reportedFailureMeansNotWritten : Spec.ReportedFailureMeansNotWritten := by
  intro cap s hs j ho hd
  have hi := inv_reachable hs
  obtain ⟨_, _, h3⟩ := hi.dbjob j hd
  rcases h3 with e | e
  · rcases ho with o | o | o <;> rw [e] at o <;> cases o
  · have hp := (hi.run j 0 true e).2.1
    rcases ho with o | o | o <;> rw [hp] at o <;> cases o

theorem cancelled_outcome_stable (x : Job) (h : x.outcome ≠ .pending) (hb : x.stage = .begun → x.outcome = .pending) :
    (cancelled x).outcome = x.outcome := by
  revert h hb
  unfold cancelled cancelBeforeClaim
  cases ho : x.outcome <;> split <;> (try split) <;> (try split) <;> simp_all

theorem armed_outcome_stable (x : Job) (h : x.outcome ≠ .pending) : (armed x).outcome = x.outcome := by
  revert h
  unfold armed cancelBeforeClaim
  cases ho : x.outcome <;> split <;> (try split) <;> simp_all

theorem drainOne_outcome_stable (x : Job) (h : x.outcome ≠ .pending) : (drainOne x).outcome = x.outcome := by
  revert h; unfold drainOne; cases x.outcome <;> simp

theorem outcome_stable {s s' : State} (hi : Inv s) {e : Event} (h : step s e = some s') (j : Nat)
    (hj : (s.job j).outcome ≠ .pending) : (s'.job j).outcome = (s.job j).outcome := by
  -- a job whose result is out is neither new, nor begun, nor running, nor pending in the queue
  have hnot_new : j ≠ s.nextId := by
    intro e; subst e; rw [hi.fresh _ (Nat.le_refl _)] at hj; exact hj rfl
  have hnot_begun : (s.job j).stage ≠ .begun := fun hb => hj (hi.begun j hb).2.1
  have hnot_run : ∀ n c, s.writer ≠ .running j n c := fun n c hw => hj (hi.run j n c hw).2.1
  cases e with
  | begin k w =>
      simp only [step, Option.some.injEq] at h; subst h
      dsimp only; rw [upd_other _ _ hnot_new]
  | tryWrite j0 =>
      simp only [step] at h
      have hne : j ≠ j0 → True := fun _ => trivial
      split at h
      · cases h
      · rename_i hb
        have hb' : (s.job j0).stage = .begun := by simpa using hb
        have hj0 : j ≠ j0 := fun e => hnot_begun (e ▸ hb')
        split at h
        · cases h; dsimp only; rw [upd_other _ _ hj0]
        · split at h
          · cases h; dsimp only; rw [upd_other _ _ hj0]
          · split at h
            · cases h
            · cases h; dsimp only; rw [upd_other _ _ hj0]
  | arm j0 =>
      simp only [step] at h
      split at h
      · have hnew : s' = { s with job := upd s.job j0 (armed (s.job j0)) } := by cases h; rfl
        subst hnew; dsimp only
        by_cases hj0 : j = j0
        · subst hj0; rw [upd_same]; exact armed_outcome_stable _ hj
        · rw [upd_other _ _ hj0]
      · cases h
  | cancel j0 =>
      simp only [step] at h
      split at h
      · cases h
      · have hnew : s' = { s with job := upd s.job j0 (cancelled (s.job j0)) } := by cases h; rfl
        subst hnew; dsimp only
        by_cases hj0 : j = j0
        · subst hj0; rw [upd_same]; exact cancelled_outcome_stable _ hj (fun hb => (hi.begun j hb).2.1)
        · rw [upd_other _ _ hj0]
  | dequeue =>
      simp only [step] at h
      split at h
      · rename_i j0 rest hw hq
        have hst : (s.job j0).stage = .queued := (hi.qstage j0).1 (by rw [hq]; simp)
        split at h
        · rename_i hu
          have hp := hi.qpending j0 hst hu
          have hj0 : j ≠ j0 := fun e => hj (e ▸ hp)
          split at h
          · split at h
            · cases h; dsimp only; rw [upd_other _ _ hj0]
            · cases h; dsimp only; rw [upd_other _ _ hj0]
          · cases h; dsimp only; rw [upd_other _ _ hj0]
          · cases h; dsimp only; rw [upd_other _ _ hj0]
        · cases h; dsimp only
          by_cases hj0 : j = j0
          · subst hj0; rw [upd_same]
          · rw [upd_other _ _ hj0]
      · cases h
  | progress =>
      simp only [step] at h
      split at h
      · split at h
        · cases h
        · cases h; rfl
      · cases h
  | commit =>
      simp only [step] at h
      split at h
      · split at h
        · cases h
        · cases h; rfl
      · cases h
  | finish =>
      simp only [step] at h
      split at h
      · rename_i j0 hw
        have hj0 : j ≠ j0 := fun e => hnot_run 0 true (e ▸ hw)
        cases h; dsimp only; rw [upd_other _ _ hj0]
      · cases h
  | abort =>
      simp only [step] at h
      split at h
      · rename_i j0 n hw
        have hj0 : j ≠ j0 := fun e => hnot_run n false (e ▸ hw)
        split at h
        · cases h; dsimp only; rw [upd_other _ _ hj0]
        · cases h
      · cases h
  | fail =>
      simp only [step] at h
      split at h
      · rename_i j0 n hw
        have hj0 : j ≠ j0 := fun e => hnot_run n false (e ▸ hw)
        cases h; dsimp only; rw [upd_other _ _ hj0]
      · cases h
  | dispose =>
      simp only [step] at h
      split at h
      · cases h
      · cases h; rfl
  | complete =>
      simp only [step] at h
      split at h
      · cases h; rfl
      · cases h
  | shutdownCancel =>
      simp only [step] at h
      split at h
      · cases h; rfl
      · cases h
  | exit =>
      simp only [step] at h
      split at h
      · split at h
        · cases h; dsimp only
          by_cases hm : j ∈ s.queue
          · simp only [hm, ite_true]; exact drainOne_outcome_stable _ hj
          · simp only [hm, ite_false]
        · cases h
      · cases h

theorem resultIsFinal : Spec.ResultIsFinal := by
  intro cap s e s' hs h j hj
  exact outcome_stable (inv_reachable hs) h j hj

theorem cancelledBeforeClaimNeverRuns : Spec.CancelledBeforeClaimNeverRuns := by
  intro cap s hs j hc
  have hi := inv_reachable hs
  refine ⟨fun hd => ?_, fun n c hw => ?_⟩
  · have := (hi.dbjob j hd).1; rw [hc] at this; cases this
  · have := (hi.run j n c hw).1; rw [hc] at this; cases this

theorem cancelledCallerReleasedAtOnce : Spec.CancelledCallerReleasedAtOnce := by
  intro cap s hs j ha hcc hcl
  exact (inv_reachable hs).armedPending j ha hcc hcl

theorem cancelledJobReleasesWriter : Spec.CancelledJobReleasesWriter := by
  intro cap s hs j n hw ht
  refine ⟨?_, ?_⟩
  · intro e s' he h
    cases e <;> simp [Event.isWriter] at he
    · simp [step, hw] at h
    · cases n with
      | zero => simp [step, hw] at h
      | succ n => simp [step, hw, ht] at h
    · cases n with
      | zero => simp [step, hw, ht] at h
      | succ n => simp [step, hw] at h
    · simp [step, hw] at h
    · simp [step, hw, ht] at h; subst h; rfl
    · simp [step, hw] at h; subst h; rfl
    · simp [step, hw] at h
  · exact ⟨.abort, { s with writer := .idle, job := upd s.job j { s.job j with outcome := .canceled } },
      rfl, by simp [step, hw, ht]⟩

theorem writerNeverStuck : Spec.WriterNeverStuck := by
  intro cap s hs hwork hstop
  have hi := inv_reachable hs
  cases hw : s.writer with
  | idle =>
      rcases hwork with h | h
      · exact absurd hw h
      · cases hq : s.queue with
        | nil => exact absurd hq h
        | cons j0 rest =>
            cases hstep : step s .dequeue with
            | some s' => exact ⟨.dequeue, s', rfl, hstep⟩
            | none =>
                exfalso
                simp only [step, hw, hq] at hstep
                split at hstep <;> (try split at hstep) <;> (try split at hstep) <;> simp at hstep
  | running j n c =>
      cases c with
      | false =>
          exact ⟨.fail, { s with writer := .idle, job := upd s.job j { s.job j with outcome := .failed } },
            rfl, by simp [step, hw]⟩
      | true =>
          have hn := (hi.run j n true hw).2.2.2.2 rfl
          subst hn
          exact ⟨.finish, { s with writer := .idle, job := upd s.job j { s.job j with outcome := .ok } },
            rfl, by simp [step, hw]⟩
  | stopped => exact absurd hw hstop

theorem shutdownLeavesNoPendingQueuedJob : Spec.ShutdownLeavesNoPendingQueuedJob := by
  intro cap s hs hw j hst ho
  have hi := inv_reachable hs
  rcases hst with hq | ht
  · have := (hi.qstage j).2 hq
    rw [(hi.stopped hw).2] at this; cases this
  · obtain ⟨n, c, e⟩ := hi.taken j ht ho
    rw [hw] at e; cases e

theorem noJobAcceptedAfterComplete : Spec.NoJobAcceptedAfterComplete := by
  intro cap s j s' hs hc h
  simp only [step, hc] at h
  split at h
  · cases h
  · cases h; exact ⟨by simp, rfl⟩

theorem readSeesEveryEarlierAcknowledgedWrite : Spec.ReadSeesEveryEarlierAcknowledgedWrite := by
  intro cap s hs r hk hc
  exact ((inv_reachable hs).readSeen r hk hc).1

theorem readIsConsistentCut : Spec.ReadIsConsistentCut := by
  intro cap s hs r hk hc
  exact ((inv_reachable hs).readSeen r hk hc).2

theorem replayReturnsCapturedRead : Spec.ReplayReturnsCapturedRead := by
  intro cap s hs q r hk ho
  have hi := inv_reachable hs
  have hc := (hi.dbjob q (hi.ok q ho)).1
  exact hi.replaySeen q r hk hc (by rw [ho]; decide)

end Nami.Storage
