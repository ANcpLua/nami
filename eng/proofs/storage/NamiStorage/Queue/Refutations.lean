import NamiStorage.Queue.Exclusive
/-
Counterexamples to what plan P13 asks of the current design, as concrete runs of the model.
Each run is reproduced by a failing test in `tests/Nami.IntegrationTests/StorageLifetimeTests.cs`.
-/
namespace Nami.Storage.Queue

/-- A fresh read holds the writer and one ingest job waits behind it (capacity 2).
Test: `FreshCall_HoldingTheWriter_IngestStillCommits`. -/
def stallTrace : List Event :=
  [.begin .read 1, .tryWrite 0, .arm 0, .dequeue, .begin .ingest 0, .tryWrite 1]

def stallState : State := (run (init 2) stallTrace).getD (init 2)

theorem stallState_run : run (init 2) stallTrace = some stallState := rfl
theorem stallState_reach : Reachable 2 stallState := runBy_reachable _ _ _ .init stallState_run
theorem stallState_writer : stallState.writer = .running 0 1 false := rfl
theorem stallState_read : (stallState.job 0).kind = .read := rfl
theorem stallState_queue : stallState.queue = [1] := rfl
theorem stallState_ingest : (stallState.job 1).kind = .ingest := rfl
theorem stallState_db : stallState.db = [] := rfl

/-- The same with a replay of a stored snapshot holding the writer (capacity 2).
Test: `Replay_HoldingTheWriter_IngestStillCommits`. -/
def replayStallTrace : List Event :=
  [.begin .read 0, .tryWrite 0, .arm 0, .dequeue, .commit, .finish,
   .begin (.replay 0) 1, .tryWrite 1, .arm 1, .dequeue, .begin .ingest 0, .tryWrite 2]

def replayStallState : State := (run (init 2) replayStallTrace).getD (init 2)

theorem replayStallState_run : run (init 2) replayStallTrace = some replayStallState := rfl
theorem replayStallState_reach : Reachable 2 replayStallState :=
  runBy_reachable _ _ _ .init replayStallState_run
theorem replayStallState_writer : replayStallState.writer = .running 1 1 false := rfl
theorem replayStallState_replay : (replayStallState.job 1).kind = .replay 0 := rfl
theorem replayStallState_queue : replayStallState.queue = [2] := rfl
theorem replayStallState_ingest : (replayStallState.job 2).kind = .ingest := rfl
theorem replayStallState_db : replayStallState.db = [0] := rfl

/-- A read that was cancelled before the writer took it still fills the only slot (capacity 1),
so the next ingest call is refused. Test: `CancelledCall_ReleasesItsQueueSlot`. -/
def slotTrace : List Event :=
  [.begin .ingest 1, .tryWrite 0, .arm 0, .dequeue, .begin .read 0, .tryWrite 1, .arm 1, .cancel 1]

def slotState : State := (run (init 1) slotTrace).getD (init 1)

theorem slotState_run : run (init 1) slotTrace = some slotState := rfl
theorem slotState_reach : Reachable 1 slotState := runBy_reachable _ _ _ .init slotState_run
theorem slotState_cancelled : (slotState.job 1).outcome = .canceled := rfl
theorem slotState_queued : 1 ∈ slotState.queue := by decide

theorem slotState_refuses_ingest :
    ∃ s', run slotState [.begin .ingest 0, .tryWrite 2] = some s' ∧ (s'.job 2).outcome = .rejected :=
  ⟨_, rfl, rfl⟩

/-! A general capacity: one read holding the writer and `cap` queued jobs make the next ingest call
fail, whatever the capacity. -/

theorem enqueue_ingest {cap : Nat} {s : State} (hs : Reachable cap s) (hd : s.disposed = false)
    (hc : s.completed = false) (hlen : s.queue.length < s.cap) :
    ∃ s', Reachable cap s' ∧ s'.writer = s.writer ∧ s'.queue.length = s.queue.length + 1 ∧
      s'.disposed = false ∧ s'.completed = false ∧ s'.cap = s.cap ∧
      (∀ j, j < s.nextId → s'.job j = s.job j) := by
  obtain ⟨s1, h1⟩ : ∃ s1, step s (.begin .ingest 0) = some s1 := ⟨_, rfl⟩
  have hs1 := ReachableBy.step _ hs h1
  have hjob1 : (s1.job s.nextId).stage = .begun ∧ (s1.job s.nextId).kind = .ingest := by
    simp only [step, Option.some.injEq] at h1; subst h1; simp [hd]
  have hrest1 : s1.writer = s.writer ∧ s1.queue = s.queue ∧ s1.disposed = s.disposed ∧
      s1.completed = s.completed ∧ s1.cap = s.cap ∧ (∀ j, j < s.nextId → s1.job j = s.job j) := by
    simp only [step, Option.some.injEq] at h1; subst h1
    refine ⟨rfl, rfl, rfl, rfl, rfl, ?_⟩
    intro j hj; exact upd_other _ _ (Nat.ne_of_lt hj)
  obtain ⟨hw1, hq1, hd1, hc1, hcap1, hj1⟩ := hrest1
  obtain ⟨s2, h2⟩ := Option.isSome_iff_exists.mp
    (show (step s1 (.tryWrite s.nextId)).isSome = true by
      simp [step, hjob1.1, hc1, hc, hq1, hcap1, hlen])
  refine ⟨s2, ReachableBy.step _ hs1 h2, ?_⟩
  simp [step, hjob1.1, hc1, hc, hq1, hcap1, hlen] at h2
  subst h2
  refine ⟨by simp [hw1], by simp, by simp [hd1, hd], by simp, by simp, ?_⟩
  intro j hj
  have hne : j ≠ s.nextId := Nat.ne_of_lt hj
  simp [upd_other _ _ hne, hj1 j hj]

theorem read_holds_writer (cap : Nat) (hcap : 0 < cap) :
    ∃ s, Reachable cap s ∧ s.writer = .running 0 1 false ∧ (s.job 0).kind = .read ∧
      s.queue = [] ∧ s.disposed = false ∧ s.completed = false ∧ s.cap = cap := by
  obtain ⟨s1, h1⟩ : ∃ s1, step (init cap) (.begin .read 1) = some s1 := ⟨_, rfl⟩
  have r1 := ReachableBy.step _ (ReachableBy.init : Reachable cap (init cap)) h1
  obtain ⟨s2, h2⟩ := Option.isSome_iff_exists.mp
    (show (step s1 (.tryWrite 0)).isSome = true by
      simp only [step, init, Option.some.injEq] at h1; subst h1; simp [step, hcap])
  have r2 := ReachableBy.step _ r1 h2
  obtain ⟨s3, h3⟩ := Option.isSome_iff_exists.mp
    (show (step s2 (.arm 0)).isSome = true by
      simp only [step, init, Option.some.injEq] at h1; subst h1
      simp [step, hcap] at h2; subst h2; simp [step])
  have r3 := ReachableBy.step _ r2 h3
  obtain ⟨s4, h4⟩ := Option.isSome_iff_exists.mp
    (show (step s3 .dequeue).isSome = true by
      simp only [step, init, Option.some.injEq] at h1; subst h1
      simp [step, hcap] at h2; subst h2
      simp [step] at h3; subst h3; simp [step])
  have r4 := ReachableBy.step _ r3 h4
  refine ⟨s4, r4, ?_⟩
  simp only [step, init, Option.some.injEq] at h1; subst h1
  simp [step, hcap] at h2; subst h2
  simp [step] at h3; subst h3
  simp [step] at h4; subst h4
  simp

theorem fill_queue (cap : Nat) (hcap : 0 < cap) :
    ∀ m, m ≤ cap → ∃ s, Reachable cap s ∧ s.writer = .running 0 1 false ∧ (s.job 0).kind = .read ∧
      s.queue.length = m ∧ s.disposed = false ∧ s.completed = false ∧ s.cap = cap := by
  intro m
  induction m with
  | zero =>
      intro _
      obtain ⟨s, hs, hw, hk, hq, hd, hc, hcp⟩ := read_holds_writer cap hcap
      exact ⟨s, hs, hw, hk, by rw [hq]; rfl, hd, hc, hcp⟩
  | succ m ih =>
      intro hm
      obtain ⟨s, hs, hw, hk, hq, hd, hc, hcp⟩ := ih (by omega)
      have hi := inv_reachable hs
      have h0 : 0 < s.nextId := hi.run_lt hw
      obtain ⟨s', hs', hw', hq', hd', hc', hcp', hj⟩ := enqueue_ingest hs hd hc (by omega)
      exact ⟨s', hs', hw'.trans hw, by rw [hj 0 h0]; exact hk, by omega, hd', hc', hcp'.trans hcp⟩

end Nami.Storage.Queue

namespace Nami.Storage
open Queue

theorem ingestStallsWhileReadRuns : Spec.IngestStallsWhileReadRuns := by
  intro h
  obtain ⟨t, hst, hk, n', c', hw'⟩ :=
    h 2 stallState stallState_reach 0 1 false 1 stallState_writer (Or.inl stallState_read)
      (by rw [stallState_queue]; simp) stallState_ingest
  have := writerHeldExclusively 2 stallState t stallState_reach hst 0 1 false n' c' stallState_writer hw' 1 hk
  rw [stallState_db] at this
  simp at this

theorem ingestStallsWhileReplayRuns :
    ∃ cap s, Reachable cap s ∧ (∃ q x n c, s.writer = .running q n c ∧ (s.job q).kind = .replay x) ∧
      ∃ k ∈ s.queue, (s.job k).kind = .ingest ∧
        ∀ t, Steps s t → k ∈ t.db → ∀ n' c', ∀ q n c, s.writer = .running q n c → t.writer ≠ .running q n' c' := by
  refine ⟨2, replayStallState, replayStallState_reach, ⟨1, 0, 1, false, replayStallState_writer,
    replayStallState_replay⟩, 2, by rw [replayStallState_queue]; simp, replayStallState_ingest, ?_⟩
  intro t hst hk n' c' q n c hw hw'
  have := writerHeldExclusively 2 replayStallState t replayStallState_reach hst q n c n' c' hw hw' 2 hk
  rw [replayStallState_db] at this
  rw [replayStallState_writer] at hw
  cases hw
  simp at this

theorem ingestRefusedWhileReadRuns : Spec.IngestRefusedWhileReadRuns := by
  intro cap hcap
  obtain ⟨s, hs, hw, hk, hq, hd, hc, hcp⟩ := fill_queue cap hcap cap (Nat.le_refl _)
  have h0 : 0 < s.nextId := (inv_reachable hs).run_lt hw
  obtain ⟨s1, h1⟩ : ∃ s1, step s (.begin .ingest 0) = some s1 := ⟨_, rfl⟩
  obtain ⟨s2, h2⟩ := Option.isSome_iff_exists.mp
    (show (step s1 (.tryWrite s.nextId)).isSome = true by
      simp only [step, Option.some.injEq] at h1; subst h1
      simp [step, hd, hc, hq, hcp])
  refine ⟨s1, ReachableBy.step _ hs h1, ⟨0, 1, false, ?_, ?_⟩, s.nextId, s2, ?_, h2, ?_⟩
  · simp only [step, Option.some.injEq] at h1; subst h1; exact hw
  · simp only [step, Option.some.injEq] at h1; subst h1
    dsimp only; rw [upd_other _ _ (Nat.ne_of_lt h0)]; exact hk
  · simp only [step, Option.some.injEq] at h1; subst h1; simp
  · simp only [step, Option.some.injEq] at h1; subst h1
    simp [step, hd, hc, hq, hcp] at h2; subst h2; simp

theorem cancelledCallKeepsItsSlot : Spec.CancelledCallKeepsItsSlot := by
  intro h
  exact h 1 slotState slotState_reach 1 slotState_cancelled slotState_queued

end Nami.Storage
