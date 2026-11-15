import NamiProofs.HostEngine.Host

/-! Invariants of the host model and the claims they carry. -/

namespace NamiProofs.HostEngine.Host

/-- Facts that hold in every reachable state of the host. -/
structure Inv (s : St) : Prop where
  stopped_pending : s.stopped = true → s.pending = []
  exited_stopped : s.child = .exited → s.exit = none → s.stopped = true
  shutdown_stopped : s.shutdown = true → s.stopped = true
  shutdown_writes : s.shutdown = true → s.writesClosed = true
  writes_stopped : s.writesClosed = true → s.stopped = true
  hook_child : s.hookArmed = false → s.child = .exited
  exit_orphan : ∀ n, s.exit = some (.code n) → s.child = .exited ∨ s.sigkill = true
  ids_nodup : (s.pending.map (·.id)).Nodup
  ids_le_seq : ∀ p ∈ s.pending, p.id ≤ s.seq

theorem inv_init : Inv init := by
  constructor <;> simp [init]

theorem nodup_filter {l : List Pending} {q : Pending → Bool} (h : (l.map (·.id)).Nodup) :
    ((l.filter q).map (·.id)).Nodup :=
  List.Nodup.sublist ((List.filter_sublist).map _) h

section stopChild
variable (s : St)
@[simp] theorem stopChild_stopped : (stopChild s).stopped = s.stopped := by unfold stopChild; split <;> rfl
@[simp] theorem stopChild_pending : (stopChild s).pending = s.pending := by unfold stopChild; split <;> rfl
@[simp] theorem stopChild_shutdown : (stopChild s).shutdown = s.shutdown := by unfold stopChild; split <;> rfl
@[simp] theorem stopChild_writesClosed : (stopChild s).writesClosed = s.writesClosed := by unfold stopChild; split <;> rfl
@[simp] theorem stopChild_hookArmed : (stopChild s).hookArmed = s.hookArmed := by unfold stopChild; split <;> rfl
@[simp] theorem stopChild_child : (stopChild s).child = s.child := by unfold stopChild; split <;> rfl
@[simp] theorem stopChild_exit : (stopChild s).exit = s.exit := by unfold stopChild; split <;> rfl
@[simp] theorem stopChild_sigkill : (stopChild s).sigkill = s.sigkill := by unfold stopChild; split <;> rfl
@[simp] theorem stopChild_seq : (stopChild s).seq = s.seq := by unfold stopChild; split <;> rfl
@[simp] theorem stopChild_done : (stopChild s).done = s.done := by unfold stopChild; split <;> rfl
@[simp] theorem stopChild_sent : (stopChild s).sent = s.sent := by unfold stopChild; split <;> rfl
@[simp] theorem stopChild_ready : (stopChild s).ready = s.ready := by unfold stopChild; split <;> rfl
@[simp] theorem stopChild_closing : (stopChild s).closing = s.closing := by unfold stopChild; split <;> rfl
@[simp] theorem stopChild_serving : (stopChild s).serving = s.serving := by unfold stopChild; split <;> rfl
@[simp] theorem stopChild_hasExited : (stopChild s).hasExited = s.hasExited := by unfold stopChild; split <;> rfl
@[simp] theorem stopChild_nextTicket : (stopChild s).nextTicket = s.nextTicket := by unfold stopChild; split <;> rfl
@[simp] theorem stopChild_transportDone : (stopChild s).transportDone = s.transportDone := by unfold stopChild; split <;> rfl
@[simp] theorem stopChild_readyFailed : (stopChild s).readyFailed = s.readyFailed := by unfold stopChild; split <;> rfl
end stopChild

theorem close_of_shutdown {s : St} (h : s.shutdown = true) : close s = s := by simp [close, h]

theorem close_of_not_shutdown {s : St} (h : s.shutdown = false) :
    close s = stopChild { rejectPending { s with stopped := true, writesClosed := true } .unavailable with
      shutdown := true } := by simp [close, h]

theorem fail_of_stopped {s : St} {e : Err} (h : s.stopped = true) : fail s e = s := by simp [fail, h]

/-- What `fail` leaves behind when the engine was not stopped yet. -/
theorem fail_fields {s : St} {e : Err} (hst : s.stopped = false) (hsd : s.shutdown = false) :
    (fail s e).stopped = true ∧ (fail s e).pending = [] ∧ (fail s e).writesClosed = true ∧
    (fail s e).shutdown = true ∧ (fail s e).child = s.child ∧ (fail s e).seq = s.seq ∧
    (fail s e).hookArmed = s.hookArmed ∧
    (fail s e).done = s.done ++ s.pending.map (fun p => ⟨p.ticket, p.via, outcomeOf p.via e⟩) ∧
    (s.ready = true → (fail s e).exit = s.exit ∧ (fail s e).sigkill = s.sigkill) ∧
    (s.ready = false → (fail s e).exit = some (.code 1) ∧
      (fail s e).sigkill = (s.sigkill || (s.hookArmed && s.child == .running))) := by
  unfold fail
  simp only [hst, Bool.false_eq_true, ite_false]
  rw [close_of_not_shutdown (by simp [rejectPending, hsd])]
  by_cases hr : s.ready = true
  · simp [hr, rejectPending]
  · have hr' : s.ready = false := by simpa using hr
    simp [hr', rejectPending, exitWith]

theorem inv_congr {s s' : St} (h : Inv s)
    (e1 : s'.stopped = s.stopped) (e2 : s'.pending = s.pending) (e3 : s'.child = s.child)
    (e4 : s'.exit = s.exit) (e5 : s'.shutdown = s.shutdown) (e6 : s'.writesClosed = s.writesClosed)
    (e7 : s'.hookArmed = s.hookArmed) (e8 : s'.sigkill = s.sigkill) (e9 : s'.seq = s.seq) : Inv s' := by
  obtain ⟨h1, h2, h3, h4, h5, h6, h7, h8, h9⟩ := h
  constructor <;> simp_all

theorem inv_fail {s : St} (e : Err) (h : Inv s) : Inv (fail s e) := by
  by_cases hst : s.stopped = true
  · rw [fail_of_stopped hst]; exact h
  · have hst' : s.stopped = false := by simpa using hst
    have hsd : s.shutdown = false := by
      cases hq : s.shutdown
      · rfl
      · exact absurd (h.shutdown_stopped hq) hst
    obtain ⟨f1, f2, f3, f4, f5, f6, f7, _, f9, f10⟩ := fail_fields (e := e) hst' hsd
    constructor
    · intro _; exact f2
    · intro _ _; exact f1
    · intro _; exact f1
    · intro _; exact f3
    · intro _; exact f1
    · intro hk; rw [f7] at hk; rw [f5]; exact h.hook_child hk
    · intro n hn
      rw [f5]
      cases hr : s.ready
      · obtain ⟨g1, g2⟩ := f10 hr
        rw [g2]
        cases hc : s.child
        · cases hk : s.hookArmed
          · exact absurd (h.hook_child hk) (by simp [hc])
          · right; simp
        · exact Or.inl rfl
      · obtain ⟨g1, g2⟩ := f9 hr
        rw [g1] at hn; rw [g2]
        exact h.exit_orphan n hn
    · rw [f2]; simp
    · rw [f2]; simp

theorem inv_close {s : St} (h : Inv s) : Inv (close s) := by
  cases hsd : s.shutdown
  · rw [close_of_not_shutdown hsd]
    constructor <;> simp [rejectPending]
    · exact h.hook_child
    · exact h.exit_orphan
  · rw [close_of_shutdown hsd]; exact h

theorem inv_exitWith {s : St} (n : Nat) (h : Inv s) : Inv (exitWith s n) := by
  constructor <;> simp only [exitWith]
  · exact h.stopped_pending
  · intro _ hx; simp at hx
  · exact h.shutdown_stopped
  · exact h.shutdown_writes
  · exact h.writes_stopped
  · exact h.hook_child
  · intro m _
    cases hc : s.child
    · cases hk : s.hookArmed
      · exact absurd (h.hook_child hk) (by simp [hc])
      · right; simp
    · exact Or.inl rfl
  · exact h.ids_nodup
  · exact h.ids_le_seq

theorem inv_request {s : St} (v : Via) (fits : Bool) (h : Inv s) : Inv (request s v fits) := by
  unfold request
  simp only
  split
  · exact inv_congr h rfl rfl rfl rfl rfl rfl rfl rfl rfl
  · split
    · exact inv_congr h rfl rfl rfl rfl rfl rfl rfl rfl rfl
    · split
      · obtain ⟨h1, h2, h3, h4, h5, h6, h7, h8, h9⟩ := h
        constructor <;> simp_all
        intro p hp; exact Nat.le_succ_of_le (h9 p hp)
      · rename_i hst _ _
        obtain ⟨h1, h2, h3, h4, h5, h6, h7, h8, h9⟩ := h
        constructor
        · intro hs'; simp at hs'; simp_all
        · intro hc hx; exact h2 hc hx
        · exact h3
        · exact h4
        · exact h5
        · exact h6
        · exact h7
        · simp only [List.map_append, List.map_cons, List.map_nil]
          refine List.nodup_append.mpr ⟨h8, by simp, ?_⟩
          intro a ha b hb
          simp at hb; subst hb
          simp at ha
          obtain ⟨p, hp, rfl⟩ := ha
          have := h9 p hp; omega
        · intro p hp
          simp at hp
          rcases hp with hp | rfl
          · have := h9 p hp; simp; omega
          · simp

theorem inv_remove {s : St} (id : Nat) (h : Inv s) (d : List Done) (sent : List Frame) :
    Inv { s with pending := removePending s id, done := d, sent := sent } := by
  obtain ⟨h1, h2, h3, h4, h5, h6, h7, h8, h9⟩ := h
  constructor <;> simp only [removePending]
  · intro hs; simp [h1 hs]
  · exact h2
  · exact h3
  · exact h4
  · exact h5
  · exact h6
  · exact h7
  · exact nodup_filter h8
  · intro p hp; exact h9 p (List.mem_filter.mp hp).1

theorem inv_step {s s' : St} {e : Ev} (h : Inv s) (hs : step s e = some s') : Inv s' := by
  cases e <;> simp only [step] at hs
  case readyFrame =>
    split at hs <;> simp at hs; subst hs
    exact inv_congr h rfl rfl rfl rfl rfl rfl rfl rfl rfl
  case requestTool fits =>
    split at hs <;> simp at hs; subst hs; exact inv_request _ _ h
  case requestHealth =>
    split at hs <;> simp at hs; subst hs; exact inv_request _ _ h
  case reply id isError =>
    split at hs
    · split at hs <;> simp at hs <;> subst hs
      · exact inv_remove id h _ _
      · exact h
    · simp at hs
  case badFrame typed =>
    split at hs <;> simp at hs; subst hs; exact inv_fail _ h
  case abort id =>
    split at hs
    · split at hs <;> simp at hs; subst hs; exact inv_remove id h _ _
    · simp at hs
  case stdinError =>
    split at hs <;> simp at hs; subst hs; exact inv_fail _ h
  case startupTimeout =>
    split at hs <;> simp at hs; subst hs; exact inv_fail _ h
  case childExit =>
    split at hs
    · simp at hs; subst hs
      split
      · rename_i hx
        by_cases hst : s.stopped = true
        · rw [fail_of_stopped (by simpa using hst)]
          obtain ⟨h1, h2, h3, h4, h5, h6, h7, h8, h9⟩ := h
          constructor <;> simp_all
        · have hst' : s.stopped = false := by simpa using hst
          have hsd : s.shutdown = false := by
            cases hq : s.shutdown
            · rfl
            · exact absurd (h.shutdown_stopped hq) hst
          obtain ⟨f1, f2, f3, f4, f5, f6, f7, _, _, _⟩ :=
            fail_fields (s := { s with child := .exited }) (e := .plain) hst' hsd
          constructor
          · intro _; exact f2
          · intro _ _; exact f1
          · intro _; exact f1
          · intro _; exact f3
          · intro _; exact f1
          · intro _; exact f5
          · intro _ _; exact Or.inl f5
          · rw [f2]; simp
          · rw [f2]; simp
      · obtain ⟨h1, h2, h3, h4, h5, h6, h7, h8, h9⟩ := h
        constructor <;> simp_all
    · simp at hs
  case childClose =>
    split at hs <;> simp at hs; subst hs
    rename_i hc
    obtain ⟨h1, h2, h3, h4, h5, h6, h7, h8, h9⟩ := h
    simp at hc
    constructor <;> simp_all
  case gracefulFires =>
    split at hs <;> simp at hs; subst hs
    exact inv_congr h rfl rfl rfl rfl rfl rfl rfl rfl rfl
  case forcedFires =>
    split at hs <;> simp at hs; subst hs
    obtain ⟨h1, h2, h3, h4, h5, h6, h7, h8, h9⟩ := h
    constructor <;> simp_all
  case hostSignal =>
    split at hs
    · split at hs <;> simp at hs <;> subst hs
      · obtain ⟨h1, h2, h3, h4, h5, h6, h7, h8, h9⟩ := h
        constructor <;> simp_all
      · exact inv_congr h rfl rfl rfl rfl rfl rfl rfl rfl rfl
    · simp at hs
  case transportStopped =>
    split at hs <;> simp at hs; subst hs
    exact inv_close (inv_congr h rfl rfl rfl rfl rfl rfl rfl rfl rfl)
  case loopEmpty =>
    split at hs <;> simp at hs; subst hs; exact inv_exitWith _ h
  case externalKill =>
    split at hs <;> simp at hs; subst hs
    obtain ⟨h1, h2, h3, h4, h5, h6, h7, h8, h9⟩ := h
    constructor <;> simp_all

theorem inv_runBy {s s' : St} : ∀ {es : List Ev}, Inv s → runBy step s es = some s' → Inv s'
  | [], h, hr => by simp [runBy] at hr; subst hr; exact h
  | e :: es, h, hr => by
    simp only [runBy] at hr
    cases hs : step s e with
    | none => simp [hs] at hr
    | some s1 =>
      simp only [hs, Option.bind_some] at hr
      exact inv_runBy (inv_step h hs) hr

theorem inv_reachable {s : St} (h : Reachable s) : Inv s := by
  obtain ⟨es, hes⟩ := h
  exact inv_runBy inv_init hes

/-! ## Projections that `fail` and `close` keep -/

section keep
variable (s : St) (e : Err) (n : Nat)
@[simp] theorem close_serving : (close s).serving = s.serving := by unfold close; split <;> simp [rejectPending]
@[simp] theorem close_ready : (close s).ready = s.ready := by unfold close; split <;> simp [rejectPending]
@[simp] theorem close_closing : (close s).closing = s.closing := by unfold close; split <;> simp [rejectPending]
@[simp] theorem close_exit : (close s).exit = s.exit := by unfold close; split <;> simp [rejectPending]
@[simp] theorem exitWith_serving : (exitWith s n).serving = s.serving := rfl
@[simp] theorem exitWith_ready : (exitWith s n).ready = s.ready := rfl
@[simp] theorem exitWith_closing : (exitWith s n).closing = s.closing := rfl

theorem fail_keep : (fail s e).serving = s.serving ∧ (fail s e).ready = s.ready ∧
    (fail s e).closing = s.closing ∧ (s.ready = true → (fail s e).exit = s.exit) := by
  unfold fail
  split
  · simp
  · by_cases hr : s.ready = true
    · simp [hr, rejectPending]
    · have hr' : s.ready = false := by simpa using hr
      simp [hr', rejectPending]
end keep

/-! ## Claims that hold -/

theorem find_unique : ∀ {l : List Pending} {p : Pending}, p ∈ l → (l.map (·.id)).Nodup →
    l.find? (·.id == p.id) = some p
  | [], _, h, _ => by simp at h
  | q :: l, p, h, hn => by
    simp only [List.map_cons, List.nodup_cons] at hn
    simp only [List.mem_cons] at h
    rcases h with rfl | h
    · simp
    · have hne : q.id ≠ p.id := by
        intro heq; apply hn.1; rw [heq]; exact List.mem_map_of_mem h
      simp [hne, find_unique h hn.2]

theorem nothingWaitsOnADeadEngine (s : St) (hr : Reachable s) (hc : s.child = .exited)
    (hx : s.exit = none) : s.pending = [] :=
  let h := inv_reachable hr
  h.stopped_pending (h.exited_stopped hc hx)

theorem deadEngineFailsToolCallsUnavailable (s : St) (hr : Reachable s) (hx : s.exit = none)
    (hc : s.child = .running) (p : Pending) (hp : p ∈ s.pending) (hv : p.via = .tool) (s' : St)
    (hs : step s .childExit = some s') : (⟨p.ticket, .tool, .unavailable⟩ : Done) ∈ s'.done := by
  have h := inv_reachable hr
  have key : step s .childExit = some (fail { s with child := .exited } .plain) := by
    have : ({ s with child := .exited } : St).exit.isNone = true := by simp [hx]
    simp only [step, hc]
    simp only [this, ite_true]
    rfl
  rw [key] at hs
  cases hs
  have hst : s.stopped = false := by
    cases hq : s.stopped
    · rfl
    · rw [h.stopped_pending hq] at hp; simp at hp
  have hsd : s.shutdown = false := by
    cases hq : s.shutdown
    · rfl
    · rw [h.shutdown_stopped hq] at hst; simp at hst
  obtain ⟨_, _, _, _, _, _, _, hd, _, _⟩ :=
    fail_fields (s := { s with child := .exited }) (e := .plain) hst hsd
  rw [hd]
  simp only [List.mem_append, List.mem_map]
  right
  exact ⟨p, hp, by simp [hv, outcomeOf]⟩

theorem noOrphanWhenHostExits (s : St) (hr : Reachable s) (n : Nat) (hx : s.exit = some (.code n)) :
    s.child = .exited ∨ s.sigkill = true :=
  (inv_reachable hr).exit_orphan n hx

theorem health503OnEngineError (s : St) (hr : Reachable s) (hx : s.exit = none)
    (he : s.hasExited = false) (p : Pending) (hp : p ∈ s.pending) (hv : p.via = .health) (s' : St)
    (hs : step s (.reply p.id true) = some s') : (⟨p.ticket, .health, .http 503⟩ : Done) ∈ s'.done := by
  have h := inv_reachable hr
  have hf : findPending s p.id = some p := find_unique hp h.ids_nodup
  simp only [step, hx, he, Option.isNone_none, Bool.not_false, Bool.and_self, ite_true, hf,
    Option.some.injEq] at hs
  subst hs
  simp [hv, replyOutcome]

theorem cancelCompletesWaitingCall (s : St) (hr : Reachable s) (hx : s.exit = none) (p : Pending)
    (hp : p ∈ s.pending) :
    ∃ s', step s (.abort p.id) = some s' ∧ (⟨p.ticket, p.via, .cancelled⟩ : Done) ∈ s'.done ∧
      (∀ q ∈ s'.pending, q.id ≠ p.id) ∧ s'.sent = s.sent ++ [.cancel p.id] := by
  have h := inv_reachable hr
  have hf : findPending s p.id = some p := find_unique hp h.ids_nodup
  have hw : s.writesClosed = false := by
    cases hq : s.writesClosed
    · rfl
    · rw [h.stopped_pending (h.writes_stopped hq)] at hp; simp at hp
  let d : Done := ⟨p.ticket, p.via, .cancelled⟩
  let s' : St := { s with pending := removePending s p.id, done := s.done ++ [d], sent := s.sent ++ [.cancel p.id] }
  refine ⟨s', ?_, ?_, ?_, ?_⟩
  · simp [step, hx, hf, hw, s', d]
  · simp [s', d]
  · intro q hq
    simp only [s', removePending, List.mem_filter, bne_iff_ne, ne_eq] at hq
    exact hq.2
  · rfl

end NamiProofs.HostEngine.Host
