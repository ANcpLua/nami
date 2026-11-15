import NamiStorage.Queue.Model
/-
The inductive invariant of the write-queue model. Every reachable state satisfies `Inv`.
-/
namespace Nami.Storage.Queue

@[simp] theorem upd_same {α : Type} (f : Nat → α) (j : Nat) (x : α) : upd f j x j = x := by
  simp [upd]

@[simp] theorem upd_other {α : Type} (f : Nat → α) {i j : Nat} (x : α) (h : i ≠ j) : upd f j x i = f i := by
  simp [upd, h]

theorem upd_apply {α : Type} (f : Nat → α) (j : Nat) (x : α) (i : Nat) :
    upd f j x i = if i = j then x else f i := rfl

structure Inv (s : State) : Prop where
  ord1 : s.completed = true → s.disposed = true
  ord2 : s.shutdownCancelled = true → s.completed = true
  stopped : s.writer = .stopped → s.completed = true ∧ s.queue = []
  fresh : ∀ j, s.nextId ≤ j → s.job j = {}
  qnodup : s.queue.Nodup
  qstage : ∀ j, j ∈ s.queue ↔ (s.job j).stage = .queued
  qclaim : ∀ j, (s.job j).stage = .queued → (s.job j).claim ≠ .claimed
  qpending : ∀ j, (s.job j).stage = .queued → (s.job j).claim = .unclaimed → (s.job j).outcome = .pending
  run : ∀ j n c, s.writer = .running j n c →
    (s.job j).claim = .claimed ∧ (s.job j).outcome = .pending ∧ (s.job j).stage = .taken ∧
    (c = true ↔ j ∈ s.db) ∧ (c = true → n = 0)
  dbnodup : s.db.Nodup
  dbjob : ∀ j ∈ s.db, (s.job j).claim = .claimed ∧ (s.job j).stage = .taken ∧
    ((s.job j).outcome = .ok ∨ s.writer = .running j 0 true)
  ok : ∀ j, (s.job j).outcome = .ok → j ∈ s.db
  cbc : ∀ j, (s.job j).claim = .cancelledBeforeClaim → (s.job j).outcome = .canceled
  begun : ∀ j, (s.job j).stage = .begun →
    (s.job j).claim = .unclaimed ∧ (s.job j).outcome = .pending ∧ (s.job j).armed = false
  refused : ∀ j, (s.job j).stage = .refused →
    (s.job j).claim = .unclaimed ∧ (s.job j).armed = false ∧
    (s.job j).outcome ≠ .pending ∧ (s.job j).outcome ≠ .ok
  absent : ∀ j, (s.job j).stage = .absent → s.job j = {}
  taken : ∀ j, (s.job j).stage = .taken → (s.job j).outcome = .pending → ∃ n c, s.writer = .running j n c
  takenUnclaimed : ∀ j, (s.job j).stage = .taken → (s.job j).claim = .unclaimed → (s.job j).outcome = .canceled
  armedPending : ∀ j, (s.job j).armed = true → (s.job j).callerCancelled = true →
    (s.job j).claim ≠ .claimed → (s.job j).outcome ≠ .pending
  qacked : ∀ j, (s.job j).stage = .queued → ∀ k ∈ s.ackedAtEnqueue j, k ∈ s.db
  readSeen : ∀ r, (s.job r).kind = .read → (s.job r).claim = .claimed →
    (∀ k ∈ s.ackedAtEnqueue r, k ∈ s.seen r) ∧ s.seen r <+: s.db
  replaySeen : ∀ q x, (s.job q).kind = .replay x → (s.job q).claim = .claimed →
    (s.job q).outcome ≠ .failed → x ∈ s.db ∧ s.seen q = s.seen x

theorem inv_init (cap : Nat) : Inv (init cap) where
  ord1 := by simp [init]
  ord2 := by simp [init]
  stopped := by simp [init]
  fresh := by simp [init]
  qnodup := by simp [init]
  qstage := by simp [init]
  qclaim := by simp [init]
  qpending := by simp [init]
  run := by simp [init]
  dbnodup := by simp [init]
  dbjob := by simp [init]
  ok := by simp [init]
  cbc := by simp [init]
  begun := by simp [init]
  refused := by simp [init]
  absent := by simp [init]
  taken := by simp [init]
  takenUnclaimed := by simp [init]
  armedPending := by simp [init]
  qacked := by simp [init]
  readSeen := by simp [init]
  replaySeen := by simp [init]

/-- A job id at or beyond `nextId` has never been used. -/
theorem Inv.lt_of_stage {s : State} (hi : Inv s) {j : Nat} (h : (s.job j).stage ≠ .absent) :
    j < s.nextId := by
  rcases Nat.lt_or_ge j s.nextId with hlt | hge
  · exact hlt
  · exact absurd (by rw [hi.fresh j hge]) h

theorem Inv.db_lt {s : State} (hi : Inv s) {j : Nat} (h : j ∈ s.db) : j < s.nextId := by
  apply hi.lt_of_stage
  rw [(hi.dbjob j h).2.1]
  decide

theorem Inv.run_lt {s : State} (hi : Inv s) {j n c} (h : s.writer = .running j n c) : j < s.nextId := by
  apply hi.lt_of_stage
  rw [(hi.run j n c h).2.2.1]
  decide

theorem Inv.queue_lt {s : State} (hi : Inv s) {j : Nat} (h : j ∈ s.queue) : j < s.nextId := by
  apply hi.lt_of_stage
  rw [(hi.qstage j).1 h]
  decide

theorem Inv.db_not_queue {s : State} (hi : Inv s) {j : Nat} (h : j ∈ s.db) : j ∉ s.queue := by
  intro hq
  have := hi.qclaim j ((hi.qstage j).1 hq)
  exact this (hi.dbjob j h).1

end Nami.Storage.Queue
