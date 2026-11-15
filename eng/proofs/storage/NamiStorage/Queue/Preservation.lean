import NamiStorage.Queue.Invariant
/-
Every event preserves `Inv`.
-/
namespace Nami.Storage.Queue

theorem inv_begin {s s' : State} (hi : Inv s) (k : Kind) (w : Nat)
    (h : step s (.begin k w) = some s') : Inv s' := by
  simp only [step, Option.some.injEq] at h
  subst h
  have hf := hi.fresh s.nextId (Nat.le_refl _)
  have hne : ∀ j, (s.job j).stage ≠ .absent → j ≠ s.nextId :=
    fun j hj => Nat.ne_of_lt (hi.lt_of_stage hj)
  have hdb : ∀ j, j ∈ s.db → j ≠ s.nextId := fun j hj => Nat.ne_of_lt (hi.db_lt hj)
  have hrun : ∀ j n c, s.writer = .running j n c → j ≠ s.nextId :=
    fun j n c hj => Nat.ne_of_lt (hi.run_lt hj)
  constructor <;> dsimp only
  · exact hi.ord1
  · exact hi.ord2
  · exact hi.stopped
  · intro j hj
    have : j ≠ s.nextId := by omega
    rw [upd_other _ _ this]
    exact hi.fresh j (by omega)
  · exact hi.qnodup
  · intro j
    by_cases hj : j = s.nextId
    · subst hj
      rw [upd_same]
      constructor
      · intro hq; have := (hi.qstage _).1 hq; rw [hf] at this; cases this
      · intro hs; cases hd : s.disposed <;> simp [hd] at hs
    · rw [upd_other _ _ hj]; exact hi.qstage j
  · intro j hs
    by_cases hj : j = s.nextId
    · subst hj; rw [upd_same] at hs ⊢; cases hd : s.disposed <;> simp [hd] at hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.qclaim j hs
  · intro j hs
    by_cases hj : j = s.nextId
    · subst hj; rw [upd_same] at hs ⊢; cases hd : s.disposed <;> simp [hd] at hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.qpending j hs
  · intro j n c hw
    rw [upd_other _ _ (hrun j n c hw)]
    exact hi.run j n c hw
  · exact hi.dbnodup
  · intro j hj
    rw [upd_other _ _ (hdb j hj)]
    exact hi.dbjob j hj
  · intro j ho
    by_cases hj : j = s.nextId
    · subst hj; rw [upd_same] at ho; cases hd : s.disposed <;> simp [hd] at ho
    · rw [upd_other _ _ hj] at ho; exact hi.ok j ho
  · intro j hc
    by_cases hj : j = s.nextId
    · subst hj; rw [upd_same] at hc; cases hc
    · rw [upd_other _ _ hj] at hc ⊢; exact hi.cbc j hc
  · intro j hs
    by_cases hj : j = s.nextId
    · subst hj; rw [upd_same] at hs ⊢; cases hd : s.disposed <;> simp [hd] at hs ⊢
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.begun j hs
  · intro j hs
    by_cases hj : j = s.nextId
    · subst hj; rw [upd_same] at hs ⊢; cases hd : s.disposed <;> simp [hd] at hs ⊢
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.refused j hs
  · intro j hs
    by_cases hj : j = s.nextId
    · subst hj; rw [upd_same] at hs ⊢; cases hd : s.disposed <;> simp [hd] at hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.absent j hs
  · intro j hs ho
    by_cases hj : j = s.nextId
    · subst hj; rw [upd_same] at hs; cases hd : s.disposed <;> simp [hd] at hs
    · rw [upd_other _ _ hj] at hs ho; exact hi.taken j hs ho
  · intro j hs hc
    by_cases hj : j = s.nextId
    · subst hj; rw [upd_same] at hs; cases hd : s.disposed <;> simp [hd] at hs
    · rw [upd_other _ _ hj] at hs hc ⊢; exact hi.takenUnclaimed j hs hc
  · intro j ha
    by_cases hj : j = s.nextId
    · subst hj; rw [upd_same] at ha; cases ha
    · rw [upd_other _ _ hj] at ha ⊢; exact hi.armedPending j ha
  · intro j hs
    by_cases hj : j = s.nextId
    · subst hj; rw [upd_same] at hs; cases hd : s.disposed <;> simp [hd] at hs
    · rw [upd_other _ _ hj] at hs; exact hi.qacked j hs
  · intro r hk hc
    by_cases hj : r = s.nextId
    · subst hj; rw [upd_same] at hc; cases hc
    · rw [upd_other _ _ hj] at hk hc; exact hi.readSeen r hk hc
  · intro q x hk hc
    by_cases hj : q = s.nextId
    · subst hj; rw [upd_same] at hc; cases hc
    · rw [upd_other _ _ hj] at hk hc ⊢; exact hi.replaySeen q x hk hc

/-- Facts about a job that has passed `ThrowIfDisposed` but not yet `TryWrite`. -/
theorem Inv.begun_facts {s : State} (hi : Inv s) {j0 : Nat} (hb : (s.job j0).stage = .begun) :
    (s.job j0).claim = .unclaimed ∧ (s.job j0).outcome = .pending ∧ (s.job j0).armed = false ∧
    j0 < s.nextId ∧ j0 ∉ s.queue ∧ j0 ∉ s.db ∧ (∀ n c, s.writer ≠ .running j0 n c) := by
  obtain ⟨hcl, hout, harm⟩ := hi.begun j0 hb
  refine ⟨hcl, hout, harm, hi.lt_of_stage (by rw [hb]; decide), ?_, ?_, ?_⟩
  · intro hq; have := (hi.qstage j0).1 hq; rw [hb] at this; cases this
  · intro hd; have := (hi.dbjob j0 hd).1; rw [hcl] at this; cases this
  · intro n c hw; have := (hi.run j0 n c hw).1; rw [hcl] at this; cases this

theorem inv_refuse {s : State} (hi : Inv s) {j0 : Nat} (hb : (s.job j0).stage = .begun) :
    Inv { s with job := upd s.job j0 { s.job j0 with stage := .refused, outcome := .rejected } } := by
  obtain ⟨hcl, hout, harm, hlt, hnq, hndb, hnrun⟩ := hi.begun_facts hb
  constructor <;> dsimp only
  · exact hi.ord1
  · exact hi.ord2
  · exact hi.stopped
  · intro j hj
    rw [upd_other _ _ (by omega)]
    exact hi.fresh j hj
  · exact hi.qnodup
  · intro j
    by_cases hj : j = j0
    · subst hj; rw [upd_same]; simp [hnq]
    · rw [upd_other _ _ hj]; exact hi.qstage j
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.qclaim j hs
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.qpending j hs
  · intro j n c hw
    have hj : j ≠ j0 := fun e => hnrun n c (e ▸ hw)
    rw [upd_other _ _ hj]
    exact hi.run j n c hw
  · exact hi.dbnodup
  · intro j hj
    have hj' : j ≠ j0 := fun e => hndb (e ▸ hj)
    rw [upd_other _ _ hj']
    exact hi.dbjob j hj
  · intro j ho
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at ho; cases ho
    · rw [upd_other _ _ hj] at ho; exact hi.ok j ho
  · intro j hc
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hc; rw [hcl] at hc; cases hc
    · rw [upd_other _ _ hj] at hc ⊢; exact hi.cbc j hc
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.begun j hs
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same]; exact ⟨hcl, harm, by simp, by simp⟩
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.refused j hs
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.absent j hs
  · intro j hs ho
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ho; exact hi.taken j hs ho
  · intro j hs hc
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs hc ⊢; exact hi.takenUnclaimed j hs hc
  · intro j ha
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at ha; rw [harm] at ha; cases ha
    · rw [upd_other _ _ hj] at ha ⊢; exact hi.armedPending j ha
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs; exact hi.qacked j hs
  · intro r hk hc
    by_cases hj : r = j0
    · subst hj; rw [upd_same] at hc; rw [hcl] at hc; cases hc
    · rw [upd_other _ _ hj] at hk hc; exact hi.readSeen r hk hc
  · intro q x hk hc
    by_cases hj : q = j0
    · subst hj; rw [upd_same] at hc; rw [hcl] at hc; cases hc
    · rw [upd_other _ _ hj] at hk hc ⊢; exact hi.replaySeen q x hk hc

theorem okJobs_sub_db {s : State} (hi : Inv s) : ∀ k ∈ okJobs s, k ∈ s.db := by
  intro k hk
  simp only [okJobs, List.mem_filter, beq_iff_eq] at hk
  exact hi.ok k hk.2

theorem inv_enqueue {s : State} (hi : Inv s) {j0 : Nat} (hb : (s.job j0).stage = .begun)
    (hcomp : s.completed = false) :
    Inv { s with job := upd s.job j0 { s.job j0 with stage := .queued },
                 queue := s.queue ++ [j0],
                 ackedAtEnqueue := upd s.ackedAtEnqueue j0 (okJobs s) } := by
  obtain ⟨hcl, hout, harm, hlt, hnq, hndb, hnrun⟩ := hi.begun_facts hb
  constructor <;> dsimp only
  · exact hi.ord1
  · exact hi.ord2
  · intro hw; have := (hi.stopped hw).1; rw [hcomp] at this; cases this
  · intro j hj
    rw [upd_other _ _ (by omega)]
    exact hi.fresh j hj
  · rw [List.nodup_append]
    refine ⟨hi.qnodup, by simp, ?_⟩
    intro a ha b hb' e
    simp at hb'
    subst hb'; subst e
    exact hnq ha
  · intro j
    by_cases hj : j = j0
    · subst hj; rw [upd_same]; simp
    · rw [upd_other _ _ hj]; simp [hj, hi.qstage j]
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same]; rw [hcl]; decide
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.qclaim j hs
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same]; intro; exact hout
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.qpending j hs
  · intro j n c hw
    have hj : j ≠ j0 := fun e => hnrun n c (e ▸ hw)
    rw [upd_other _ _ hj]
    exact hi.run j n c hw
  · exact hi.dbnodup
  · intro j hj
    have hj' : j ≠ j0 := fun e => hndb (e ▸ hj)
    rw [upd_other _ _ hj']
    exact hi.dbjob j hj
  · intro j ho
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at ho; rw [hout] at ho; cases ho
    · rw [upd_other _ _ hj] at ho; exact hi.ok j ho
  · intro j hc
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hc; rw [hcl] at hc; cases hc
    · rw [upd_other _ _ hj] at hc ⊢; exact hi.cbc j hc
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.begun j hs
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.refused j hs
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.absent j hs
  · intro j hs ho
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ho; exact hi.taken j hs ho
  · intro j hs hc
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs hc ⊢; exact hi.takenUnclaimed j hs hc
  · intro j ha
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at ha; rw [harm] at ha; cases ha
    · rw [upd_other _ _ hj] at ha ⊢; exact hi.armedPending j ha
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same]; exact okJobs_sub_db hi
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.qacked j hs
  · intro r hk hc
    by_cases hj : r = j0
    · subst hj; rw [upd_same] at hc; rw [hcl] at hc; cases hc
    · rw [upd_other _ _ hj] at hk hc ⊢; exact hi.readSeen r hk hc
  · intro q x hk hc
    by_cases hj : q = j0
    · subst hj; rw [upd_same] at hc; rw [hcl] at hc; cases hc
    · rw [upd_other _ _ hj] at hk hc ⊢; exact hi.replaySeen q x hk hc

theorem inv_tryWrite {s s' : State} (hi : Inv s) (j0 : Nat)
    (h : step s (.tryWrite j0) = some s') : Inv s' := by
  simp only [step] at h
  split at h
  · cases h
  · rename_i hb
    have hb' : (s.job j0).stage = .begun := by simpa using hb
    split at h
    · cases h; exact inv_refuse hi hb'
    · split at h
      · cases h; exact inv_enqueue hi hb' (by simpa using ‹¬s.completed = true›)
      · split at h
        · cases h
        · cases h; exact inv_refuse hi hb'

/-- The job record `ArmCancellation` leaves behind. -/
def armed (x : Job) : Job :=
  if x.callerCancelled = true then cancelBeforeClaim { x with armed := true } else { x with armed := true }

theorem armed_stage (x : Job) : (armed x).stage = x.stage := by
  unfold armed cancelBeforeClaim; split <;> (try split) <;> rfl

theorem armed_kind (x : Job) : (armed x).kind = x.kind := by
  unfold armed cancelBeforeClaim; split <;> (try split) <;> rfl

theorem armed_claimed (x : Job) : (armed x).claim = .claimed ↔ x.claim = .claimed := by
  unfold armed cancelBeforeClaim; split <;> (try split) <;> simp_all

theorem armed_of_claimed (x : Job) (h : x.claim = .claimed) : armed x = { x with armed := true } := by
  unfold armed cancelBeforeClaim; split <;> (try split) <;> simp_all

theorem armed_outcome (x : Job) :
    (armed x).outcome = x.outcome ∨ (x.outcome = .pending ∧ (armed x).outcome = .canceled) := by
  unfold armed cancelBeforeClaim; split <;> (try split) <;> (try split) <;> simp_all

theorem armed_unclaimed (x : Job) (h : (armed x).claim = .unclaimed) :
    (armed x).outcome = x.outcome ∧ x.claim = .unclaimed := by
  unfold armed cancelBeforeClaim at *; split at h <;> (try split at h) <;> simp_all

theorem armed_cbc_outcome (x : Job) (h : (armed x).claim = .cancelledBeforeClaim)
    (h1 : x.claim = .cancelledBeforeClaim → x.outcome = .canceled)
    (h2 : x.claim = .unclaimed → x.outcome = .pending ∨ x.outcome = .canceled) :
    (armed x).outcome = .canceled := by
  revert h h1 h2
  cases hc : x.claim <;> cases ho : x.outcome <;> cases hcc : x.callerCancelled <;>
    simp [armed, cancelBeforeClaim, hc, ho, hcc]

theorem armed_flags (x : Job) : (armed x).armed = true ∧ (armed x).callerCancelled = x.callerCancelled := by
  unfold armed cancelBeforeClaim; split <;> (try split) <;> simp

theorem armed_released (x : Job) (hcc : x.callerCancelled = true) (hcl : (armed x).claim ≠ .claimed)
    (h1 : x.claim = .cancelledBeforeClaim → x.outcome = .canceled) :
    (armed x).outcome ≠ .pending := by
  revert hcl h1
  cases hc : x.claim <;> cases ho : x.outcome <;>
    simp [armed, cancelBeforeClaim, hc, ho, hcc]

theorem inv_arm {s s' : State} (hi : Inv s) (j0 : Nat)
    (h : step s (.arm j0) = some s') : Inv s' := by
  simp only [step] at h
  split at h
  · rename_i hg
    obtain ⟨hst, harm⟩ := hg
    have hnew : s' = { s with job := upd s.job j0 (armed (s.job j0)) } := by
      cases h; rfl
    subst hnew
    have hlt : j0 < s.nextId := hi.lt_of_stage (by rcases hst with e | e <;> rw [e] <;> decide)
    constructor <;> dsimp only
    · exact hi.ord1
    · exact hi.ord2
    · exact hi.stopped
    · intro j hj
      rw [upd_other _ _ (by omega)]
      exact hi.fresh j hj
    · exact hi.qnodup
    · intro j
      by_cases hj : j = j0
      · subst hj; rw [upd_same, armed_stage]; exact hi.qstage j
      · rw [upd_other _ _ hj]; exact hi.qstage j
    · intro j hs
      by_cases hj : j = j0
      · subst hj; rw [upd_same, armed_stage] at hs; rw [upd_same]
        intro hc; exact hi.qclaim j hs ((armed_claimed _).1 hc)
      · rw [upd_other _ _ hj] at hs ⊢; exact hi.qclaim j hs
    · intro j hs hc
      by_cases hj : j = j0
      · subst hj; rw [upd_same, armed_stage] at hs; rw [upd_same] at hc ⊢
        obtain ⟨ho, hcl⟩ := armed_unclaimed _ hc
        rw [ho]; exact hi.qpending j hs hcl
      · rw [upd_other _ _ hj] at hs hc ⊢; exact hi.qpending j hs hc
    · intro j n c hw
      by_cases hj : j = j0
      · subst hj; rw [upd_same]
        have := hi.run j n c hw
        rw [armed_of_claimed _ this.1]; exact this
      · rw [upd_other _ _ hj]; exact hi.run j n c hw
    · exact hi.dbnodup
    · intro j hj
      by_cases hj' : j = j0
      · subst hj'; rw [upd_same]
        have := hi.dbjob j hj
        rw [armed_of_claimed _ this.1]; exact this
      · rw [upd_other _ _ hj']; exact hi.dbjob j hj
    · intro j ho
      by_cases hj : j = j0
      · subst hj; rw [upd_same] at ho
        rcases armed_outcome (s.job j) with e | ⟨_, e⟩
        · rw [e] at ho; exact hi.ok j ho
        · rw [e] at ho; cases ho
      · rw [upd_other _ _ hj] at ho; exact hi.ok j ho
    · intro j hc
      by_cases hj : j = j0
      · subst hj; rw [upd_same] at hc ⊢
        apply armed_cbc_outcome _ hc (hi.cbc j)
        intro hu
        rcases hst with hq | ht
        · exact Or.inl (hi.qpending j hq hu)
        · exact Or.inr (hi.takenUnclaimed j ht hu)
      · rw [upd_other _ _ hj] at hc ⊢; exact hi.cbc j hc
    · intro j hs
      by_cases hj : j = j0
      · subst hj; rw [upd_same, armed_stage] at hs; rcases hst with e | e <;> rw [e] at hs <;> cases hs
      · rw [upd_other _ _ hj] at hs ⊢; exact hi.begun j hs
    · intro j hs
      by_cases hj : j = j0
      · subst hj; rw [upd_same, armed_stage] at hs; rcases hst with e | e <;> rw [e] at hs <;> cases hs
      · rw [upd_other _ _ hj] at hs ⊢; exact hi.refused j hs
    · intro j hs
      by_cases hj : j = j0
      · subst hj; rw [upd_same, armed_stage] at hs; rcases hst with e | e <;> rw [e] at hs <;> cases hs
      · rw [upd_other _ _ hj] at hs ⊢; exact hi.absent j hs
    · intro j hs ho
      by_cases hj : j = j0
      · subst hj; rw [upd_same, armed_stage] at hs; rw [upd_same] at ho
        rcases armed_outcome (s.job j) with e | ⟨_, e⟩
        · rw [e] at ho; exact hi.taken j hs ho
        · rw [e] at ho; cases ho
      · rw [upd_other _ _ hj] at hs ho; exact hi.taken j hs ho
    · intro j hs hc
      by_cases hj : j = j0
      · subst hj; rw [upd_same, armed_stage] at hs; rw [upd_same] at hc ⊢
        obtain ⟨ho, hcl⟩ := armed_unclaimed _ hc
        rw [ho]; exact hi.takenUnclaimed j hs hcl
      · rw [upd_other _ _ hj] at hs hc ⊢; exact hi.takenUnclaimed j hs hc
    · intro j ha hcc hcl
      by_cases hj : j = j0
      · subst hj; rw [upd_same] at ha hcc hcl ⊢
        rw [(armed_flags _).2] at hcc
        exact armed_released _ hcc hcl (hi.cbc j)
      · rw [upd_other _ _ hj] at ha hcc hcl ⊢; exact hi.armedPending j ha hcc hcl
    · intro j hs
      by_cases hj : j = j0
      · subst hj; rw [upd_same, armed_stage] at hs; exact hi.qacked j hs
      · rw [upd_other _ _ hj] at hs; exact hi.qacked j hs
    · intro r hk hc
      by_cases hj : r = j0
      · subst hj; rw [upd_same, armed_kind] at hk; rw [upd_same] at hc
        exact hi.readSeen r hk ((armed_claimed _).1 hc)
      · rw [upd_other _ _ hj] at hk hc; exact hi.readSeen r hk hc
    · intro q x hk hc
      by_cases hj : q = j0
      · subst hj; rw [upd_same, armed_kind] at hk; rw [upd_same] at hc ⊢
        have hc' := (armed_claimed _).1 hc
        rw [armed_of_claimed _ hc']
        exact hi.replaySeen q x hk hc'
      · rw [upd_other _ _ hj] at hk hc ⊢; exact hi.replaySeen q x hk hc
  · cases h

/-- The job record a cancellation of the caller's token leaves behind. -/
def cancelled (x : Job) : Job :=
  if x.stage = .begun ∧ x.kind = .maint then
    { x with callerCancelled := true, stage := .refused, outcome := .canceled }
  else if x.armed = true then cancelBeforeClaim { x with callerCancelled := true }
  else { x with callerCancelled := true }

theorem cancelled_kind (x : Job) : (cancelled x).kind = x.kind := by
  unfold cancelled cancelBeforeClaim; split <;> (try split) <;> (try split) <;> rfl

theorem cancelled_stage (x : Job) (h : ¬ (x.stage = .begun ∧ x.kind = .maint)) :
    (cancelled x).stage = x.stage := by
  unfold cancelled cancelBeforeClaim; split <;> (try split) <;> (try split) <;> simp_all

theorem cancelled_claimed (x : Job) : (cancelled x).claim = .claimed ↔ x.claim = .claimed := by
  unfold cancelled cancelBeforeClaim; split <;> (try split) <;> (try split) <;> simp_all

theorem cancelled_of_claimed (x : Job) (h : x.claim = .claimed) (hb : x.stage ≠ .begun) :
    cancelled x = { x with callerCancelled := true } := by
  unfold cancelled cancelBeforeClaim; split <;> (try split) <;> (try split) <;> simp_all

theorem cancelled_armed (x : Job) : (cancelled x).armed = x.armed := by
  unfold cancelled cancelBeforeClaim; split <;> (try split) <;> (try split) <;> rfl

theorem cancelled_outcome (x : Job) :
    (cancelled x).outcome = x.outcome ∨ (cancelled x).outcome = .canceled := by
  unfold cancelled cancelBeforeClaim
  cases ho : x.outcome <;> split <;> (try split) <;> (try split) <;> simp_all

theorem cancelled_pending (x : Job) (h : (cancelled x).outcome = .pending) :
    x.outcome = .pending ∧ (cancelled x).stage = x.stage ∧ (cancelled x).claim = x.claim := by
  revert h
  unfold cancelled cancelBeforeClaim
  cases ho : x.outcome <;> cases hc : x.claim <;> split <;> (try split) <;> (try split) <;> simp_all

theorem cancelled_unclaimed (x : Job) (h : (cancelled x).claim = .unclaimed) :
    x.claim = .unclaimed ∧ x.armed = false ∧ (cancelled x).outcome = x.outcome ∨
    x.claim = .unclaimed ∧ x.stage = .begun ∧ x.kind = .maint ∧ (cancelled x).stage = .refused ∧
      (cancelled x).outcome = .canceled := by
  revert h
  unfold cancelled cancelBeforeClaim
  cases hc : x.claim <;> cases ha : x.armed <;> split <;> (try split) <;> (try split) <;> simp_all

theorem cancelled_cbc_outcome (x : Job) (h : (cancelled x).claim = .cancelledBeforeClaim)
    (h1 : x.claim = .cancelledBeforeClaim → x.outcome = .canceled)
    (h2 : x.claim = .unclaimed → x.armed = true → x.outcome = .pending ∨ x.outcome = .canceled) :
    (cancelled x).outcome = .canceled := by
  revert h h1 h2
  unfold cancelled cancelBeforeClaim
  cases hc : x.claim <;> cases ho : x.outcome <;> cases ha : x.armed <;>
    split <;> (try split) <;> (try split) <;> simp_all

theorem cancelled_released (x : Job) (ha : (cancelled x).armed = true) (hcl : (cancelled x).claim ≠ .claimed)
    (h1 : x.claim = .cancelledBeforeClaim → x.outcome = .canceled) :
    (cancelled x).outcome ≠ .pending := by
  revert ha hcl h1
  unfold cancelled cancelBeforeClaim
  cases hc : x.claim <;> cases ho : x.outcome <;> cases ha : x.armed <;>
    split <;> (try split) <;> (try split) <;> simp_all

theorem inv_cancel {s s' : State} (hi : Inv s) (j0 : Nat)
    (h : step s (.cancel j0) = some s') : Inv s' := by
  simp only [step] at h
  split at h
  · cases h
  · rename_i hg
    have hnew : s' = { s with job := upd s.job j0 (cancelled (s.job j0)) } := by
      cases h; rfl
    subst hnew
    have hna : (s.job j0).stage ≠ .absent := fun e => hg (Or.inl e)
    have hlt : j0 < s.nextId := hi.lt_of_stage hna
    -- the cases where the job is begun maintenance waiting for space
    by_cases hm : (s.job j0).stage = .begun ∧ (s.job j0).kind = .maint
    · obtain ⟨hcl, hout, harm⟩ := hi.begun j0 hm.1
      have hnq : j0 ∉ s.queue := fun hq => by have := (hi.qstage j0).1 hq; rw [hm.1] at this; cases this
      have hndb : j0 ∉ s.db := fun hd => by have := (hi.dbjob j0 hd).1; rw [hcl] at this; cases this
      have hnrun : ∀ n c, s.writer ≠ .running j0 n c := fun n c hw => by
        have := (hi.run j0 n c hw).1; rw [hcl] at this; cases this
      have hc : cancelled (s.job j0) =
          { s.job j0 with callerCancelled := true, stage := .refused, outcome := .canceled } := by
        unfold cancelled; simp [hm]
      rw [hc]
      constructor <;> dsimp only
      · exact hi.ord1
      · exact hi.ord2
      · exact hi.stopped
      · intro j hj; rw [upd_other _ _ (by omega)]; exact hi.fresh j hj
      · exact hi.qnodup
      · intro j
        by_cases hj : j = j0
        · subst hj; rw [upd_same]; simp [hnq]
        · rw [upd_other _ _ hj]; exact hi.qstage j
      · intro j hs
        by_cases hj : j = j0
        · subst hj; rw [upd_same] at hs; cases hs
        · rw [upd_other _ _ hj] at hs ⊢; exact hi.qclaim j hs
      · intro j hs
        by_cases hj : j = j0
        · subst hj; rw [upd_same] at hs; cases hs
        · rw [upd_other _ _ hj] at hs ⊢; exact hi.qpending j hs
      · intro j n c hw
        have hj : j ≠ j0 := fun e => hnrun n c (e ▸ hw)
        rw [upd_other _ _ hj]; exact hi.run j n c hw
      · exact hi.dbnodup
      · intro j hj
        have hj' : j ≠ j0 := fun e => hndb (e ▸ hj)
        rw [upd_other _ _ hj']; exact hi.dbjob j hj
      · intro j ho
        by_cases hj : j = j0
        · subst hj; rw [upd_same] at ho; cases ho
        · rw [upd_other _ _ hj] at ho; exact hi.ok j ho
      · intro j hc'
        by_cases hj : j = j0
        · subst hj; rw [upd_same] at hc'; simp [hcl] at hc'
        · rw [upd_other _ _ hj] at hc' ⊢; exact hi.cbc j hc'
      · intro j hs
        by_cases hj : j = j0
        · subst hj; rw [upd_same] at hs; cases hs
        · rw [upd_other _ _ hj] at hs ⊢; exact hi.begun j hs
      · intro j hs
        by_cases hj : j = j0
        · subst hj; rw [upd_same]; simp [hcl, harm]
        · rw [upd_other _ _ hj] at hs ⊢; exact hi.refused j hs
      · intro j hs
        by_cases hj : j = j0
        · subst hj; rw [upd_same] at hs; cases hs
        · rw [upd_other _ _ hj] at hs ⊢; exact hi.absent j hs
      · intro j hs ho
        by_cases hj : j = j0
        · subst hj; rw [upd_same] at hs; cases hs
        · rw [upd_other _ _ hj] at hs ho; exact hi.taken j hs ho
      · intro j hs hc'
        by_cases hj : j = j0
        · subst hj; rw [upd_same] at hs; cases hs
        · rw [upd_other _ _ hj] at hs hc' ⊢; exact hi.takenUnclaimed j hs hc'
      · intro j ha
        by_cases hj : j = j0
        · subst hj; rw [upd_same] at ha; simp [harm] at ha
        · rw [upd_other _ _ hj] at ha ⊢; exact hi.armedPending j ha
      · intro j hs
        by_cases hj : j = j0
        · subst hj; rw [upd_same] at hs; cases hs
        · rw [upd_other _ _ hj] at hs; exact hi.qacked j hs
      · intro r hk hc'
        by_cases hj : r = j0
        · subst hj; rw [upd_same] at hc'; simp [hcl] at hc'
        · rw [upd_other _ _ hj] at hk hc'; exact hi.readSeen r hk hc'
      · intro q x hk hc'
        by_cases hj : q = j0
        · subst hj; rw [upd_same] at hc'; simp [hcl] at hc'
        · rw [upd_other _ _ hj] at hk hc' ⊢; exact hi.replaySeen q x hk hc'
    · have hst := cancelled_stage _ hm
      constructor <;> dsimp only
      · exact hi.ord1
      · exact hi.ord2
      · exact hi.stopped
      · intro j hj; rw [upd_other _ _ (by omega)]; exact hi.fresh j hj
      · exact hi.qnodup
      · intro j
        by_cases hj : j = j0
        · subst hj; rw [upd_same, hst]; exact hi.qstage j
        · rw [upd_other _ _ hj]; exact hi.qstage j
      · intro j hs
        by_cases hj : j = j0
        · subst hj; rw [upd_same, hst] at hs; rw [upd_same]
          intro hc; exact hi.qclaim j hs ((cancelled_claimed _).1 hc)
        · rw [upd_other _ _ hj] at hs ⊢; exact hi.qclaim j hs
      · intro j hs hc
        by_cases hj : j = j0
        · subst hj; rw [upd_same, hst] at hs; rw [upd_same] at hc ⊢
          rcases cancelled_unclaimed _ hc with ⟨hu, _, ho⟩ | ⟨_, hb, _⟩
          · rw [ho]; exact hi.qpending j hs hu
          · rw [hs] at hb; cases hb
        · rw [upd_other _ _ hj] at hs hc ⊢; exact hi.qpending j hs hc
      · intro j n c hw
        by_cases hj : j = j0
        · subst hj; rw [upd_same]
          have := hi.run j n c hw
          rw [cancelled_of_claimed _ this.1 (by rw [this.2.2.1]; decide)]; exact this
        · rw [upd_other _ _ hj]; exact hi.run j n c hw
      · exact hi.dbnodup
      · intro j hj
        by_cases hj' : j = j0
        · subst hj'; rw [upd_same]
          have := hi.dbjob j hj
          rw [cancelled_of_claimed _ this.1 (by rw [this.2.1]; decide)]; exact this
        · rw [upd_other _ _ hj']; exact hi.dbjob j hj
      · intro j ho
        by_cases hj : j = j0
        · subst hj; rw [upd_same] at ho
          rcases cancelled_outcome (s.job j) with e | e
          · rw [e] at ho; exact hi.ok j ho
          · rw [e] at ho; cases ho
        · rw [upd_other _ _ hj] at ho; exact hi.ok j ho
      · intro j hc
        by_cases hj : j = j0
        · subst hj; rw [upd_same] at hc ⊢
          apply cancelled_cbc_outcome _ hc (hi.cbc j)
          intro hu ha
          -- an armed job has been queued
          rcases hs : (s.job j).stage with _ | _ | _ | _ | _
          · rw [hi.absent j hs] at ha; cases ha
          · rw [(hi.begun j hs).2.2] at ha; cases ha
          · exact Or.inl (hi.qpending j hs hu)
          · exact Or.inr (hi.takenUnclaimed j hs hu)
          · rw [(hi.refused j hs).2.1] at ha; cases ha
        · rw [upd_other _ _ hj] at hc ⊢; exact hi.cbc j hc
      · intro j hs
        by_cases hj : j = j0
        · subst hj; rw [upd_same, hst] at hs; rw [upd_same]
          obtain ⟨h1, h2, h3⟩ := hi.begun j hs
          have hplain : cancelled (s.job j) = { s.job j with callerCancelled := true } := by
            unfold cancelled; simp [hm, h3]
          rw [hplain]; exact ⟨h1, h2, h3⟩
        · rw [upd_other _ _ hj] at hs ⊢; exact hi.begun j hs
      · intro j hs
        by_cases hj : j = j0
        · subst hj; rw [upd_same, hst] at hs; rw [upd_same]
          obtain ⟨h1, h2, h3, h4⟩ := hi.refused j hs
          have hplain : cancelled (s.job j) = { s.job j with callerCancelled := true } := by
            unfold cancelled; simp [hm, h2]
          rw [hplain]; exact ⟨h1, h2, h3, h4⟩
        · rw [upd_other _ _ hj] at hs ⊢; exact hi.refused j hs
      · intro j hs
        by_cases hj : j = j0
        · subst hj; rw [upd_same, hst] at hs; exact absurd hs hna
        · rw [upd_other _ _ hj] at hs ⊢; exact hi.absent j hs
      · intro j hs ho
        by_cases hj : j = j0
        · subst hj; rw [upd_same, hst] at hs; rw [upd_same] at ho
          exact hi.taken j hs (cancelled_pending _ ho).1
        · rw [upd_other _ _ hj] at hs ho; exact hi.taken j hs ho
      · intro j hs hc
        by_cases hj : j = j0
        · subst hj; rw [upd_same, hst] at hs; rw [upd_same] at hc ⊢
          rcases cancelled_unclaimed _ hc with ⟨hu, _, ho⟩ | ⟨_, hb, _⟩
          · rw [ho]; exact hi.takenUnclaimed j hs hu
          · rw [hs] at hb; cases hb
        · rw [upd_other _ _ hj] at hs hc ⊢; exact hi.takenUnclaimed j hs hc
      · intro j ha hcc hcl
        by_cases hj : j = j0
        · subst hj; rw [upd_same] at ha hcc hcl ⊢
          exact cancelled_released _ ha hcl (hi.cbc j)
        · rw [upd_other _ _ hj] at ha hcc hcl ⊢; exact hi.armedPending j ha hcc hcl
      · intro j hs
        by_cases hj : j = j0
        · subst hj; rw [upd_same, hst] at hs; exact hi.qacked j hs
        · rw [upd_other _ _ hj] at hs; exact hi.qacked j hs
      · intro r hk hc
        by_cases hj : r = j0
        · subst hj; rw [upd_same, cancelled_kind] at hk; rw [upd_same] at hc
          exact hi.readSeen r hk ((cancelled_claimed _).1 hc)
        · rw [upd_other _ _ hj] at hk hc; exact hi.readSeen r hk hc
      · intro q x hk hc
        by_cases hj : q = j0
        · subst hj; rw [upd_same, cancelled_kind] at hk; rw [upd_same] at hc ⊢
          have hc' := (cancelled_claimed _).1 hc
          have hnb : (s.job q).stage ≠ .begun := by
            intro hb; rw [(hi.begun q hb).1] at hc'; cases hc'
          rw [cancelled_of_claimed _ hc' hnb]
          exact hi.replaySeen q x hk hc'
        · rw [upd_other _ _ hj] at hk hc ⊢; exact hi.replaySeen q x hk hc

/-- Facts about the head of the queue while the writer is idle. -/
theorem Inv.head_facts {s : State} (hi : Inv s) {j0 : Nat} {rest : List Nat} (hq : s.queue = j0 :: rest) :
    (s.job j0).stage = .queued ∧ (s.job j0).claim ≠ .claimed ∧ j0 < s.nextId ∧ j0 ∉ s.db ∧
    j0 ∉ rest ∧ rest.Nodup ∧ (∀ j, j ∈ rest ↔ j ≠ j0 ∧ (s.job j).stage = .queued) := by
  have hmem : j0 ∈ s.queue := by rw [hq]; simp
  have hst := (hi.qstage j0).1 hmem
  have hnd := hi.qnodup
  rw [hq, List.nodup_cons] at hnd
  refine ⟨hst, hi.qclaim j0 hst, hi.queue_lt hmem, fun hd => hi.db_not_queue hd hmem, hnd.1, hnd.2, ?_⟩
  intro j
  constructor
  · intro hj
    refine ⟨fun e => hnd.1 (e ▸ hj), (hi.qstage j).1 (by rw [hq]; exact List.mem_cons_of_mem _ hj)⟩
  · rintro ⟨hne, hjs⟩
    have := (hi.qstage j).2 hjs
    rw [hq, List.mem_cons] at this
    rcases this with e | e
    · exact absurd e hne
    · exact e

theorem Inv.idle_no_taken_pending {s : State} (hi : Inv s) (hw : s.writer = .idle) (j : Nat)
    (hs : (s.job j).stage = .taken) (ho : (s.job j).outcome = .pending) : False := by
  obtain ⟨n, c, h⟩ := hi.taken j hs ho
  rw [hw] at h; cases h

theorem Inv.idle_db_ok {s : State} (hi : Inv s) (hw : s.writer = .idle) (j : Nat) (hj : j ∈ s.db) :
    (s.job j).outcome = .ok := by
  rcases (hi.dbjob j hj).2.2 with e | e
  · exact e
  · rw [hw] at e; cases e

theorem inv_claim_run {s : State} (hi : Inv s) {j0 : Nat} {rest : List Nat} (hw : s.writer = .idle)
    (hq : s.queue = j0 :: rest) (hu : (s.job j0).claim = .unclaimed) (sn : Nat → List Nat)
    (hsn : ∀ i, i ≠ j0 → sn i = s.seen i) (hread : (s.job j0).kind = .read → sn j0 = s.db)
    (hrep : ∀ x, (s.job j0).kind = .replay x → x ∈ s.db ∧ sn j0 = s.seen x) :
    Inv { s with queue := rest, job := upd s.job j0 { s.job j0 with stage := .taken, claim := .claimed },
                 writer := .running j0 (s.job j0).work false, seen := sn } := by
  obtain ⟨hst, _, hlt, hndb, hnr, hrnd, hrest⟩ := hi.head_facts hq
  have hout := hi.qpending j0 hst hu
  constructor <;> dsimp only
  · exact hi.ord1
  · exact hi.ord2
  · intro h; cases h
  · intro j hj; rw [upd_other _ _ (by omega)]; exact hi.fresh j hj
  · exact hrnd
  · intro j
    by_cases hj : j = j0
    · subst hj; rw [upd_same]; simp [hnr]
    · rw [upd_other _ _ hj, hrest j]; simp [hj]
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.qclaim j hs
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.qpending j hs
  · intro j n c h
    cases h
    rw [upd_same]
    exact ⟨rfl, hout, rfl, by simp [hndb], by simp⟩
  · exact hi.dbnodup
  · intro j hj
    have hj' : j ≠ j0 := fun e => hndb (e ▸ hj)
    rw [upd_other _ _ hj']
    exact ⟨(hi.dbjob j hj).1, (hi.dbjob j hj).2.1, Or.inl (hi.idle_db_ok hw j hj)⟩
  · intro j ho
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at ho; rw [hout] at ho; cases ho
    · rw [upd_other _ _ hj] at ho; exact hi.ok j ho
  · intro j hc
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hc; cases hc
    · rw [upd_other _ _ hj] at hc ⊢; exact hi.cbc j hc
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.begun j hs
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.refused j hs
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.absent j hs
  · intro j hs ho
    by_cases hj : j = j0
    · subst hj; exact ⟨_, _, rfl⟩
    · rw [upd_other _ _ hj] at hs ho; exact (hi.idle_no_taken_pending hw j hs ho).elim
  · intro j hs hc
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hc; cases hc
    · rw [upd_other _ _ hj] at hs hc ⊢; exact hi.takenUnclaimed j hs hc
  · intro j ha hcc hcl
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hcl; exact absurd rfl hcl
    · rw [upd_other _ _ hj] at ha hcc hcl ⊢; exact hi.armedPending j ha hcc hcl
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs; exact hi.qacked j hs
  · intro r hk hc
    by_cases hj : r = j0
    · subst hj; rw [upd_same] at hk
      rw [hread hk]
      exact ⟨hi.qacked r hst, List.prefix_refl _⟩
    · rw [upd_other _ _ hj] at hk hc; rw [hsn r hj]; exact hi.readSeen r hk hc
  · intro q x hk hc ho
    by_cases hj : q = j0
    · subst hj; rw [upd_same] at hk ho
      obtain ⟨hx, he⟩ := hrep x hk
      have hxne : x ≠ q := fun e => hndb (e ▸ hx)
      exact ⟨hx, by rw [he, hsn x hxne]⟩
    · rw [upd_other _ _ hj] at hk hc ho
      obtain ⟨hx, he⟩ := hi.replaySeen q x hk hc ho
      have hxne : x ≠ j0 := fun e => hndb (e ▸ hx)
      exact ⟨hx, by rw [hsn q hj, hsn x hxne, he]⟩

theorem inv_claim_fail {s : State} (hi : Inv s) {j0 : Nat} {rest : List Nat} (hw : s.writer = .idle)
    (hq : s.queue = j0 :: rest) (hu : (s.job j0).claim = .unclaimed) (hkr : (s.job j0).kind ≠ .read) :
    Inv { s with queue := rest,
                 job := upd s.job j0 { s.job j0 with stage := .taken, claim := .claimed, outcome := .failed } } := by
  obtain ⟨hst, _, hlt, hndb, hnr, hrnd, hrest⟩ := hi.head_facts hq
  constructor <;> dsimp only
  · exact hi.ord1
  · exact hi.ord2
  · intro h; rw [hw] at h; cases h
  · intro j hj; rw [upd_other _ _ (by omega)]; exact hi.fresh j hj
  · exact hrnd
  · intro j
    by_cases hj : j = j0
    · subst hj; rw [upd_same]; simp [hnr]
    · rw [upd_other _ _ hj, hrest j]; simp [hj]
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.qclaim j hs
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.qpending j hs
  · intro j n c h; rw [hw] at h; cases h
  · exact hi.dbnodup
  · intro j hj
    have hj' : j ≠ j0 := fun e => hndb (e ▸ hj)
    rw [upd_other _ _ hj']
    exact hi.dbjob j hj
  · intro j ho
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at ho; cases ho
    · rw [upd_other _ _ hj] at ho; exact hi.ok j ho
  · intro j hc
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hc; cases hc
    · rw [upd_other _ _ hj] at hc ⊢; exact hi.cbc j hc
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.begun j hs
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.refused j hs
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.absent j hs
  · intro j hs ho
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at ho; cases ho
    · rw [upd_other _ _ hj] at hs ho; exact (hi.idle_no_taken_pending hw j hs ho).elim
  · intro j hs hc
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hc; cases hc
    · rw [upd_other _ _ hj] at hs hc ⊢; exact hi.takenUnclaimed j hs hc
  · intro j ha hcc hcl
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hcl; exact absurd rfl hcl
    · rw [upd_other _ _ hj] at ha hcc hcl ⊢; exact hi.armedPending j ha hcc hcl
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs; exact hi.qacked j hs
  · intro r hk hc
    by_cases hj : r = j0
    · subst hj; rw [upd_same] at hk; exact absurd hk hkr
    · rw [upd_other _ _ hj] at hk hc; exact hi.readSeen r hk hc
  · intro q x hk hc ho
    by_cases hj : q = j0
    · subst hj; rw [upd_same] at ho; exact absurd rfl ho
    · rw [upd_other _ _ hj] at hk hc ho; exact hi.replaySeen q x hk hc ho

theorem inv_skip {s : State} (hi : Inv s) {j0 : Nat} {rest : List Nat} (hw : s.writer = .idle)
    (hq : s.queue = j0 :: rest) (hu : (s.job j0).claim ≠ .unclaimed) :
    Inv { s with queue := rest, job := upd s.job j0 { s.job j0 with stage := .taken } } := by
  obtain ⟨hst, hncl, hlt, hndb, hnr, hrnd, hrest⟩ := hi.head_facts hq
  have hcbc : (s.job j0).claim = .cancelledBeforeClaim := by
    revert hu hncl; cases (s.job j0).claim <;> simp
  have hout := hi.cbc j0 hcbc
  constructor <;> dsimp only
  · exact hi.ord1
  · exact hi.ord2
  · intro h; rw [hw] at h; cases h
  · intro j hj; rw [upd_other _ _ (by omega)]; exact hi.fresh j hj
  · exact hrnd
  · intro j
    by_cases hj : j = j0
    · subst hj; rw [upd_same]; simp [hnr]
    · rw [upd_other _ _ hj, hrest j]; simp [hj]
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.qclaim j hs
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.qpending j hs
  · intro j n c h; rw [hw] at h; cases h
  · exact hi.dbnodup
  · intro j hj
    have hj' : j ≠ j0 := fun e => hndb (e ▸ hj)
    rw [upd_other _ _ hj']
    exact hi.dbjob j hj
  · intro j ho
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at ho; rw [hout] at ho; cases ho
    · rw [upd_other _ _ hj] at ho; exact hi.ok j ho
  · intro j hc
    by_cases hj : j = j0
    · subst hj; rw [upd_same]; exact hout
    · rw [upd_other _ _ hj] at hc ⊢; exact hi.cbc j hc
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.begun j hs
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.refused j hs
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.absent j hs
  · intro j hs ho
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at ho; rw [hout] at ho; cases ho
    · rw [upd_other _ _ hj] at hs ho; exact (hi.idle_no_taken_pending hw j hs ho).elim
  · intro j hs hc
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hc; rw [hcbc] at hc; cases hc
    · rw [upd_other _ _ hj] at hs hc ⊢; exact hi.takenUnclaimed j hs hc
  · intro j ha hcc hcl
    by_cases hj : j = j0
    · subst hj; rw [upd_same]; rw [hout]; decide
    · rw [upd_other _ _ hj] at ha hcc hcl ⊢; exact hi.armedPending j ha hcc hcl
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; cases hs
    · rw [upd_other _ _ hj] at hs; exact hi.qacked j hs
  · intro r hk hc
    by_cases hj : r = j0
    · subst hj; rw [upd_same] at hc; rw [hcbc] at hc; cases hc
    · rw [upd_other _ _ hj] at hk hc; exact hi.readSeen r hk hc
  · intro q x hk hc ho
    by_cases hj : q = j0
    · subst hj; rw [upd_same] at hc; rw [hcbc] at hc; cases hc
    · rw [upd_other _ _ hj] at hk hc ho; exact hi.replaySeen q x hk hc ho

theorem inv_dequeue {s s' : State} (hi : Inv s) (h : step s .dequeue = some s') : Inv s' := by
  simp only [step] at h
  split at h
  · rename_i j0 rest hw hq
    split at h
    · rename_i hu
      split at h
      · rename_i x hk
        split at h
        · rename_i hx
          cases h
          exact inv_claim_run hi hw hq hu _ (fun i hi' => upd_other _ _ hi')
            (by intro e; rw [hk] at e; cases e)
            (by intro y e; rw [hk] at e; cases e; exact ⟨hx, upd_same _ _ _⟩)
        · cases h; exact inv_claim_fail hi hw hq hu (by rw [hk]; intro e; cases e)
      · rename_i hk
        cases h
        exact inv_claim_run hi hw hq hu _ (fun i hi' => upd_other _ _ hi')
          (fun _ => upd_same _ _ _) (by intro y e; rw [hk] at e; cases e)
      · rename_i hk1 hk2
        cases h
        exact inv_claim_run hi hw hq hu s.seen (fun _ _ => rfl)
          (by intro e; exact absurd e hk2) (by intro y e; exact absurd e (hk1 y))
    · rename_i hu
      cases h
      exact inv_skip hi hw hq hu
  · cases h

theorem inv_progress {s s' : State} (hi : Inv s) (h : step s .progress = some s') : Inv s' := by
  simp only [step] at h
  split at h
  · rename_i j0 n hw
    split at h
    · cases h
    · cases h
      have hr := hi.run j0 (n + 1) false hw
      constructor <;> dsimp only
      · exact hi.ord1
      · exact hi.ord2
      · intro e; cases e
      · exact hi.fresh
      · exact hi.qnodup
      · exact hi.qstage
      · exact hi.qclaim
      · exact hi.qpending
      · intro j m c e
        cases e
        exact ⟨hr.1, hr.2.1, hr.2.2.1, hr.2.2.2.1, by simp⟩
      · exact hi.dbnodup
      · intro j hj
        obtain ⟨h1, h2, h3⟩ := hi.dbjob j hj
        refine ⟨h1, h2, Or.inl ?_⟩
        rcases h3 with e | e
        · exact e
        · rw [hw] at e; cases e
      · exact hi.ok
      · exact hi.cbc
      · exact hi.begun
      · exact hi.refused
      · exact hi.absent
      · intro j hs ho
        obtain ⟨m, c, e⟩ := hi.taken j hs ho
        rw [hw] at e; cases e
        exact ⟨n, false, rfl⟩
      · exact hi.takenUnclaimed
      · exact hi.armedPending
      · exact hi.qacked
      · exact hi.readSeen
      · exact hi.replaySeen
  · cases h

theorem inv_commit {s s' : State} (hi : Inv s) (h : step s .commit = some s') : Inv s' := by
  simp only [step] at h
  split at h
  · rename_i j0 hw
    split at h
    · cases h
    · cases h
      have hr := hi.run j0 0 false hw
      have hndb : j0 ∉ s.db := fun hd => by have := hr.2.2.2.1.2 hd; cases this
      constructor <;> dsimp only
      · exact hi.ord1
      · exact hi.ord2
      · intro e; cases e
      · exact hi.fresh
      · exact hi.qnodup
      · exact hi.qstage
      · exact hi.qclaim
      · exact hi.qpending
      · intro j m c e
        cases e
        exact ⟨hr.1, hr.2.1, hr.2.2.1, by simp, by simp⟩
      · rw [List.nodup_append]
        refine ⟨hi.dbnodup, by simp, ?_⟩
        intro a ha b hb e
        simp at hb; subst hb; subst e; exact hndb ha
      · intro j hj
        rw [List.mem_append] at hj
        rcases hj with hj | hj
        · obtain ⟨h1, h2, h3⟩ := hi.dbjob j hj
          refine ⟨h1, h2, Or.inl ?_⟩
          rcases h3 with e | e
          · exact e
          · rw [hw] at e; cases e
        · simp at hj; subst hj
          exact ⟨hr.1, hr.2.2.1, Or.inr rfl⟩
      · intro j ho; exact List.mem_append_left _ (hi.ok j ho)
      · exact hi.cbc
      · exact hi.begun
      · exact hi.refused
      · exact hi.absent
      · intro j hs ho
        obtain ⟨m, c, e⟩ := hi.taken j hs ho
        rw [hw] at e; cases e
        exact ⟨0, true, rfl⟩
      · exact hi.takenUnclaimed
      · exact hi.armedPending
      · intro j hs k hk; exact List.mem_append_left _ (hi.qacked j hs k hk)
      · intro r hk hc
        obtain ⟨h1, h2⟩ := hi.readSeen r hk hc
        exact ⟨h1, h2.trans (List.prefix_append _ _)⟩
      · intro q x hk hc ho
        obtain ⟨h1, h2⟩ := hi.replaySeen q x hk hc ho
        exact ⟨List.mem_append_left _ h1, h2⟩
  · cases h

/-- The writer hands back a job: `TrySetResult`, `TrySetCanceled` or `TrySetException`, then idle. -/
theorem inv_release {s : State} (hi : Inv s) {j0 n : Nat} {c : Bool} (hw : s.writer = .running j0 n c)
    (o : Outcome) (hop : o ≠ .pending) (hiff : o = .ok ↔ j0 ∈ s.db) :
    Inv { s with writer := .idle, job := upd s.job j0 { s.job j0 with outcome := o } } := by
  have hr := hi.run j0 n c hw
  have hlt := hi.run_lt hw
  constructor <;> dsimp only
  · exact hi.ord1
  · exact hi.ord2
  · intro e; cases e
  · intro j hj; rw [upd_other _ _ (by omega)]; exact hi.fresh j hj
  · exact hi.qnodup
  · intro j
    by_cases hj : j = j0
    · subst hj; rw [upd_same]; rw [hi.qstage j]
    · rw [upd_other _ _ hj]; exact hi.qstage j
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; rw [hr.2.2.1] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.qclaim j hs
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; rw [hr.2.2.1] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.qpending j hs
  · intro j m c' e; cases e
  · exact hi.dbnodup
  · intro j hj
    by_cases hj' : j = j0
    · subst hj'; rw [upd_same]
      exact ⟨hr.1, hr.2.2.1, Or.inl (hiff.2 hj)⟩
    · rw [upd_other _ _ hj']
      obtain ⟨h1, h2, h3⟩ := hi.dbjob j hj
      refine ⟨h1, h2, Or.inl ?_⟩
      rcases h3 with e | e
      · exact e
      · rw [hw] at e; cases e; exact absurd rfl hj'
  · intro j ho
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at ho; exact hiff.1 ho
    · rw [upd_other _ _ hj] at ho; exact hi.ok j ho
  · intro j hc
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hc; rw [hr.1] at hc; cases hc
    · rw [upd_other _ _ hj] at hc ⊢; exact hi.cbc j hc
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; rw [hr.2.2.1] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.begun j hs
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; rw [hr.2.2.1] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.refused j hs
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; rw [hr.2.2.1] at hs; cases hs
    · rw [upd_other _ _ hj] at hs ⊢; exact hi.absent j hs
  · intro j hs ho
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at ho; exact absurd ho hop
    · rw [upd_other _ _ hj] at hs ho
      obtain ⟨m, c', e⟩ := hi.taken j hs ho
      rw [hw] at e; cases e; exact absurd rfl hj
  · intro j hs hc
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hc; rw [hr.1] at hc; cases hc
    · rw [upd_other _ _ hj] at hs hc ⊢; exact hi.takenUnclaimed j hs hc
  · intro j ha hcc hcl
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hcl; exact absurd hr.1 hcl
    · rw [upd_other _ _ hj] at ha hcc hcl ⊢; exact hi.armedPending j ha hcc hcl
  · intro j hs
    by_cases hj : j = j0
    · subst hj; rw [upd_same] at hs; rw [hr.2.2.1] at hs; cases hs
    · rw [upd_other _ _ hj] at hs; exact hi.qacked j hs
  · intro r hk hc
    by_cases hj : r = j0
    · subst hj; rw [upd_same] at hk hc; exact hi.readSeen r hk hc
    · rw [upd_other _ _ hj] at hk hc; exact hi.readSeen r hk hc
  · intro q x hk hc ho
    by_cases hj : q = j0
    · subst hj; rw [upd_same] at hk hc
      exact hi.replaySeen q x hk hc (by rw [hr.2.1]; decide)
    · rw [upd_other _ _ hj] at hk hc ho; exact hi.replaySeen q x hk hc ho

theorem inv_finish {s s' : State} (hi : Inv s) (h : step s .finish = some s') : Inv s' := by
  simp only [step] at h
  split at h
  · rename_i j0 hw
    cases h
    have hr := hi.run j0 0 true hw
    exact inv_release hi hw .ok (by decide) ⟨fun _ => hr.2.2.2.1.1 rfl, fun _ => rfl⟩
  · cases h

theorem inv_abort {s s' : State} (hi : Inv s) (h : step s .abort = some s') : Inv s' := by
  simp only [step] at h
  split at h
  · rename_i j0 n hw
    split at h
    · cases h
      have hr := hi.run j0 n false hw
      exact inv_release hi hw .canceled (by decide)
        ⟨fun e => (by cases e), fun hd => (by have := hr.2.2.2.1.2 hd; cases this)⟩
    · cases h
  · cases h

theorem inv_fail {s s' : State} (hi : Inv s) (h : step s .fail = some s') : Inv s' := by
  simp only [step] at h
  split at h
  · rename_i j0 n hw
    cases h
    have hr := hi.run j0 n false hw
    exact inv_release hi hw .failed (by decide)
      ⟨fun e => (by cases e), fun hd => (by have := hr.2.2.2.1.2 hd; cases this)⟩
  · cases h

theorem inv_flags {s : State} (hi : Inv s) (d c x : Bool)
    (h1 : c = true → d = true) (h2 : x = true → c = true)
    (hc : s.completed = true → c = true) :
    Inv { s with disposed := d, completed := c, shutdownCancelled := x } := by
  constructor <;> dsimp only
  · exact h1
  · exact h2
  · intro hw; exact ⟨hc (hi.stopped hw).1, (hi.stopped hw).2⟩
  all_goals first
    | exact hi.fresh | exact hi.qnodup | exact hi.qstage | exact hi.qclaim | exact hi.qpending
    | exact hi.run | exact hi.dbnodup | exact hi.dbjob | exact hi.ok | exact hi.cbc | exact hi.begun
    | exact hi.refused | exact hi.absent | exact hi.taken | exact hi.takenUnclaimed
    | exact hi.armedPending | exact hi.qacked | exact hi.readSeen | exact hi.replaySeen

theorem inv_dispose {s s' : State} (hi : Inv s) (h : step s .dispose = some s') : Inv s' := by
  simp only [step] at h
  split at h
  · cases h
  · cases h
    exact inv_flags hi true s.completed s.shutdownCancelled (fun _ => rfl) hi.ord2 id

theorem inv_complete {s s' : State} (hi : Inv s) (h : step s .complete = some s') : Inv s' := by
  simp only [step] at h
  split at h
  · rename_i hg
    cases h
    exact inv_flags hi s.disposed true s.shutdownCancelled (fun _ => hg.1) (fun _ => rfl) (fun _ => rfl)
  · cases h

theorem inv_shutdownCancel {s s' : State} (hi : Inv s) (h : step s .shutdownCancel = some s') : Inv s' := by
  simp only [step] at h
  split at h
  · rename_i hg
    cases h
    exact inv_flags hi s.disposed s.completed true hi.ord1 (fun _ => hg.1) id
  · cases h

theorem drainOne_outcome (x : Job) : (drainOne x).outcome ≠ .pending := by
  unfold drainOne; split <;> simp_all

theorem drainOne_ok (x : Job) (h : (drainOne x).outcome = .ok) : x.outcome = .ok := by
  revert h; unfold drainOne; split <;> simp_all

theorem inv_exit {s s' : State} (hi : Inv s) (h : step s .exit = some s') : Inv s' := by
  simp only [step] at h
  split at h
  · rename_i hw
    split at h
    · rename_i hg
      cases h
      have hcomp : s.completed = true := by
        rcases hg with ⟨e, _⟩ | e
        · exact e
        · exact hi.ord2 e
      have hj : ∀ i, i ∉ s.queue → (fun i => if i ∈ s.queue then drainOne (s.job i) else s.job i) i = s.job i :=
        fun i hn => by simp [hn]
      have hjq : ∀ i, i ∈ s.queue → (fun i => if i ∈ s.queue then drainOne (s.job i) else s.job i) i =
          drainOne (s.job i) := fun i hm => by simp [hm]
      constructor <;> dsimp only
      · exact hi.ord1
      · exact hi.ord2
      · intro _; exact ⟨hcomp, rfl⟩
      · intro j hge
        have hn : j ∉ s.queue := fun hm => by have := hi.queue_lt hm; omega
        simp only [hn, ite_false]; exact hi.fresh j hge
      · exact List.nodup_nil
      · intro j
        by_cases hm : j ∈ s.queue
        · simp only [hm, ite_true]; simp [drainOne]
        · simp only [hm, ite_false]; simp only [List.not_mem_nil, false_iff]
          intro e; exact hm ((hi.qstage j).2 e)
      · intro j hs
        by_cases hm : j ∈ s.queue
        · simp only [hm, ite_true] at hs; simp [drainOne] at hs
        · simp only [hm, ite_false] at hs; exact absurd ((hi.qstage j).2 hs) hm
      · intro j hs
        by_cases hm : j ∈ s.queue
        · simp only [hm, ite_true] at hs; simp [drainOne] at hs
        · simp only [hm, ite_false] at hs; exact absurd ((hi.qstage j).2 hs) hm
      · intro j n c e; cases e
      · exact hi.dbnodup
      · intro j hjd
        have hn : j ∉ s.queue := hi.db_not_queue hjd
        simp only [hn, ite_false]
        exact ⟨(hi.dbjob j hjd).1, (hi.dbjob j hjd).2.1, Or.inl (hi.idle_db_ok hw j hjd)⟩
      · intro j ho
        by_cases hm : j ∈ s.queue
        · simp only [hm, ite_true] at ho; exact hi.ok j (drainOne_ok _ ho)
        · simp only [hm, ite_false] at ho; exact hi.ok j ho
      · intro j hc
        by_cases hm : j ∈ s.queue
        · simp only [hm, ite_true] at hc ⊢
          have hc' : (s.job j).claim = .cancelledBeforeClaim := by simpa [drainOne] using hc
          have := hi.cbc j hc'
          simp [drainOne, this]
        · simp only [hm, ite_false] at hc ⊢; exact hi.cbc j hc
      · intro j hs
        by_cases hm : j ∈ s.queue
        · simp only [hm, ite_true] at hs; simp [drainOne] at hs
        · simp only [hm, ite_false] at hs ⊢; exact hi.begun j hs
      · intro j hs
        by_cases hm : j ∈ s.queue
        · simp only [hm, ite_true] at hs; simp [drainOne] at hs
        · simp only [hm, ite_false] at hs ⊢; exact hi.refused j hs
      · intro j hs
        by_cases hm : j ∈ s.queue
        · simp only [hm, ite_true] at hs; simp [drainOne] at hs
        · simp only [hm, ite_false] at hs ⊢; exact hi.absent j hs
      · intro j hs ho
        by_cases hm : j ∈ s.queue
        · simp only [hm, ite_true] at ho; exact absurd ho (drainOne_outcome _)
        · simp only [hm, ite_false] at hs ho; exact (hi.idle_no_taken_pending hw j hs ho).elim
      · intro j hs hc
        by_cases hm : j ∈ s.queue
        · simp only [hm, ite_true] at hc ⊢
          have hc' : (s.job j).claim = .unclaimed := by simpa [drainOne] using hc
          have := hi.qpending j ((hi.qstage j).1 hm) hc'
          simp [drainOne, this]
        · simp only [hm, ite_false] at hs hc ⊢; exact hi.takenUnclaimed j hs hc
      · intro j ha hcc hcl
        by_cases hm : j ∈ s.queue
        · simp only [hm, ite_true]; exact drainOne_outcome _
        · simp only [hm, ite_false] at ha hcc hcl ⊢; exact hi.armedPending j ha hcc hcl
      · intro j hs
        by_cases hm : j ∈ s.queue
        · simp only [hm, ite_true] at hs; simp [drainOne] at hs
        · simp only [hm, ite_false] at hs; exact absurd ((hi.qstage j).2 hs) hm
      · intro r hk hc
        by_cases hm : r ∈ s.queue
        · simp only [hm, ite_true] at hk hc
          have hc' : (s.job r).claim = .claimed := by simpa [drainOne] using hc
          exact absurd hc' (hi.qclaim r ((hi.qstage r).1 hm))
        · simp only [hm, ite_false] at hk hc; exact hi.readSeen r hk hc
      · intro q x hk hc ho
        by_cases hm : q ∈ s.queue
        · simp only [hm, ite_true] at hc
          have hc' : (s.job q).claim = .claimed := by simpa [drainOne] using hc
          exact absurd hc' (hi.qclaim q ((hi.qstage q).1 hm))
        · simp only [hm, ite_false] at hk hc ho; exact hi.replaySeen q x hk hc ho
    · cases h
  · cases h

theorem inv_step {s s' : State} (hi : Inv s) {e : Event} (h : step s e = some s') : Inv s' := by
  cases e with
  | begin k w => exact inv_begin hi k w h
  | tryWrite j => exact inv_tryWrite hi j h
  | arm j => exact inv_arm hi j h
  | cancel j => exact inv_cancel hi j h
  | dequeue => exact inv_dequeue hi h
  | progress => exact inv_progress hi h
  | commit => exact inv_commit hi h
  | finish => exact inv_finish hi h
  | abort => exact inv_abort hi h
  | fail => exact inv_fail hi h
  | dispose => exact inv_dispose hi h
  | complete => exact inv_complete hi h
  | shutdownCancel => exact inv_shutdownCancel hi h
  | exit => exact inv_exit hi h

theorem inv_reachable {cap : Nat} {s : State} (h : Reachable cap s) : Inv s := by
  induction h with
  | init => exact inv_init cap
  | step e _ hs ih => exact inv_step ih hs

end Nami.Storage.Queue
