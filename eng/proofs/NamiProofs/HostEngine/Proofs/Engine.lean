import NamiProofs.HostEngine.Spec

/-! Proofs and counterexamples for the engine model. -/

namespace NamiProofs.HostEngine.Engine

instance : Inhabited St := ⟨{}⟩

/-- Facts that hold in every reachable state of the engine. -/
structure Inv (s : St) : Prop where
  ended_cancelled : s.loop ≠ .reading → s.lifetime = true ∧ ∀ c ∈ s.pending, c.cancelled = true
  ids_nodup : (s.pending.map (·.id)).Nodup
  exit_ended : s.exit.isSome = true → s.loop ≠ .reading

theorem inv_init : Inv init := by constructor <;> simp [init]

theorem map_id_setPhase (s : St) (id : Nat) (p : Phase) :
    (setPhase s id p).map (·.id) = s.pending.map (·.id) := by
  simp only [setPhase, List.map_map]
  congr 1; funext c; simp only [Function.comp]; split <;> rfl

theorem cancelled_setPhase (s : St) (id : Nat) (p : Phase) :
    (∀ c ∈ s.pending, c.cancelled = true) → ∀ c ∈ setPhase s id p, c.cancelled = true := by
  intro h c hc
  simp only [setPhase, List.mem_map] at hc
  obtain ⟨d, hd, rfl⟩ := hc
  split <;> simp [h d hd]

theorem nodup_remove (s : St) (id : Nat) (h : (s.pending.map (·.id)).Nodup) :
    ((remove s id).map (·.id)).Nodup :=
  List.Nodup.sublist ((List.filter_sublist).map _) h

theorem inv_cancelAll {s : St} (h : (s.pending.map (·.id)).Nodup) (hl : s.loop ≠ .reading) :
    Inv (cancelAll s) := by
  constructor
  · intro _; simp [cancelAll]
  · simp only [cancelAll, List.map_map]
    have : ((fun c : Call => c.id) ∘ fun c => { c with cancelled := true }) = (fun c : Call => c.id) := by
      funext c; rfl
    rw [this]; exact h
  · intro _; exact hl

theorem inv_step {s s' : St} {e : Ev} (h : Inv s) (hs : step s e = some s') : Inv s' := by
  obtain ⟨h1, h2, h3⟩ := h
  cases e <;> simp only [step] at hs
  case frame f =>
    split at hs <;> simp at hs; subst hs
    rename_i hr
    have hr' : s.loop = .reading := by simpa using hr
    cases f with
    | oversize => exact inv_cancelAll h2 (by simp)
    | garbage => exact inv_cancelAll h2 (by simp)
    | req id m ok =>
      cases ok
      · exact inv_cancelAll h2 (by simp)
      · simp only [accept]
        split
        · exact inv_cancelAll h2 (by simp)
        · rename_i hc
          constructor
          · intro hl; simp [hr'] at hl
          · simp only [List.map_append, List.map_cons, List.map_nil]
            refine List.nodup_append.mpr ⟨h2, by simp, ?_⟩
            intro a ha b hb
            simp at hb; subst hb
            intro heq; subst heq
            simp [ids] at hc
            obtain ⟨_, hc2⟩ := hc
            simp at ha
            obtain ⟨c, hc', hcid⟩ := ha
            exact hc2 c hc' hcid
          · intro hx; simp [hr'] at hx ⊢; exact h3 hx |>.elim (by simp [hr'])
    | cancel id ok =>
      cases ok
      · exact inv_cancelAll h2 (by simp)
      · constructor
        · intro hl; simp [accept, hr'] at hl
        · simp only [accept, List.map_map]
          have : ((fun c : Call => c.id) ∘ fun c => if c.id == id then { c with cancelled := true } else c)
              = (fun c : Call => c.id) := by
            funext c; simp only [Function.comp]; split <;> rfl
          rw [this]; exact h2
        · intro hx; simp only [accept] at hx; exact absurd hr' (h3 hx)
  case eof =>
    split at hs <;> simp at hs; subst hs; exact inv_cancelAll h2 (by simp)
  case eofPartial =>
    split at hs <;> simp at hs; subst hs; exact inv_cancelAll h2 (by simp)
  case stop =>
    split at hs <;> simp at hs; subst hs
    apply inv_cancelAll (s := { s with loop := if s.loop = .reading then .stopped else s.loop }) h2
    simp only
    split
    · simp
    · rename_i hne; simpa using hne
  case enter id =>
    split at hs
    · split at hs <;> simp at hs; subst hs
      constructor
      · intro hl; obtain ⟨a, b⟩ := h1 hl; exact ⟨a, cancelled_setPhase s id _ b⟩
      · rw [map_id_setPhase]; exact h2
      · exact h3
    · simp at hs
  case respond id isError =>
    split at hs
    · split at hs <;> simp at hs; subst hs
      constructor
      · intro hl; obtain ⟨a, b⟩ := h1 hl; exact ⟨a, cancelled_setPhase s id _ b⟩
      · rw [map_id_setPhase]; exact h2
      · exact h3
    · simp at hs
  case release id =>
    split at hs
    · split at hs <;> simp at hs; subst hs
      constructor
      · intro hl; obtain ⟨a, b⟩ := h1 hl
        exact ⟨a, fun c hc => b c (List.mem_filter.mp hc).1⟩
      · exact nodup_remove s id h2
      · exact h3
    · simp at hs
  case drop id =>
    split at hs
    · split at hs
      · simp at hs; subst hs
        constructor
        · intro hl; obtain ⟨a, b⟩ := h1 hl
          exact ⟨a, fun c hc => b c (List.mem_filter.mp hc).1⟩
        · exact nodup_remove s id h2
        · exact h3
      · split at hs <;> simp at hs; subst hs
        constructor
        · intro hl; obtain ⟨a, b⟩ := h1 hl
          exact ⟨a, fun c hc => b c (List.mem_filter.mp hc).1⟩
        · exact nodup_remove s id h2
        · exact h3
    · simp at hs
  case exitProcess =>
    split at hs <;> simp at hs; subst hs
    rename_i hc
    simp at hc
    constructor
    · intro hl; exact h1 hl
    · exact h2
    · intro _; exact hc.1.1

theorem inv_runBy {s s' : St} : ∀ {es : List Ev}, Inv s → runBy step s es = some s' → Inv s'
  | [], h, hr => by simp [runBy] at hr; subst hr; exact h
  | e :: es, h, hr => by
    simp only [runBy] at hr
    cases hs : step s e with
    | none => simp [hs] at hr
    | some s1 => simp only [hs, Option.bind_some] at hr; exact inv_runBy (inv_step h hs) hr

theorem inv_reachable {s : St} (h : Reachable s) : Inv s := by
  obtain ⟨es, hes⟩ := h; exact inv_runBy inv_init hes

/-! ## Once the read loop has ended, the engine can always reach its exit -/

theorem filter_head (c : Call) (rest : List Call) (h : ((c :: rest).map (·.id)).Nodup) :
    (c :: rest).filter (·.id != c.id) = rest := by
  simp only [List.map_cons, List.nodup_cons, List.mem_map] at h
  simp only [List.filter_cons, bne_self_eq_false, Bool.false_eq_true, ite_false]
  apply List.filter_eq_self.mpr
  intro x hx
  have : x.id ≠ c.id := fun heq => h.1 ⟨x, hx, heq⟩
  simpa using this

theorem drain_to_exit : ∀ (n : Nat) (s : St), s.pending.length = n → s.loop ≠ .reading →
    s.lifetime = true → (∀ c ∈ s.pending, c.cancelled = true) → (s.pending.map (·.id)).Nodup →
    s.exit = none → ∃ es s', runBy step s es = some s' ∧ s'.exit.isSome = true
  | 0, s, hn, hl, _, _, _, hx => by
    have hp : s.pending = [] := List.eq_nil_of_length_eq_zero hn
    refine ⟨[.exitProcess], { s with exit := some (s.loop != .faulted) }, ?_, by simp⟩
    have hl' : (s.loop != .reading) = true := by simpa using hl
    simp [runBy, step, hl', hp, hx]
  | n + 1, s, hn, hl, hlt, hc, hnd, hx => by
    match hp : s.pending, hn with
    | c :: rest, hn =>
      have hcc : c.cancelled = true := hc c (by simp [hp])
      have hfind : findCall s c.id = some c := by simp [findCall, hp]
      have hrem : remove s c.id = rest := by
        simp only [remove, hp]; exact filter_head c rest (hp ▸ hnd)
      have hnd' : (rest.map (·.id)).Nodup := by
        have := hp ▸ hnd; simp only [List.map_cons, List.nodup_cons] at this; exact this.2
      have hc' : ∀ d ∈ rest, d.cancelled = true := fun d hd => hc d (by simp [hp, hd])
      have hlen : rest.length = n := by simpa using hn
      -- the call ends: `release` after a written reply, otherwise `drop` (its token is cancelled)
      cases hph : c.phase with
      | replied =>
        obtain ⟨es, s', hr, hx'⟩ := drain_to_exit n { s with pending := rest, busy := s.busy - 1 }
          hlen hl hlt hc' hnd' hx
        refine ⟨.release c.id :: es, s', ?_, hx'⟩
        simp only [runBy, step, hfind, hph]
        simp [hrem, hr]
      | waiting =>
        obtain ⟨es, s', hr, hx'⟩ := drain_to_exit n { s with pending := rest } hlen hl hlt hc' hnd' hx
        refine ⟨.drop c.id :: es, s', ?_, hx'⟩
        simp only [runBy, step, hfind, hph, hcc]
        simp [hrem, hr]
      | running =>
        obtain ⟨es, s', hr, hx'⟩ := drain_to_exit n { s with pending := rest, busy := s.busy - 1 }
          hlen hl hlt hc' hnd' hx
        refine ⟨.drop c.id :: es, s', ?_, hx'⟩
        simp only [runBy, step, hfind, hph, hcc]
        simp [hrem, hr]

theorem engineCanAlwaysExitAfterTheLoopEnds : Claim.EngineCanAlwaysExitAfterTheLoopEnds step := by
  intro s hr hl
  have h := inv_reachable hr
  cases hx : s.exit with
  | some v => exact ⟨[], s, rfl, by simp [hx]⟩
  | none =>
    obtain ⟨hlt, hc⟩ := h.ended_cancelled hl
    exact drain_to_exit _ s rfl hl hlt hc h.ids_nodup hx

/-! ## Counterexamples -/

/-- One accepted call, waiting for a worker. -/
def oneCall : St := (run init [.frame (.req 1 .call true)]).get!

theorem oneCall_reachable : Reachable oneCall := ⟨[.frame (.req 1 .call true)], rfl⟩

theorem init_reachable : Reachable init := ⟨[], rfl⟩

/-- A garbage frame ends the loop (P14: refused, not fatal). -/
theorem badFrameNotFatal_refuted : ¬ Claim.BadFrameNotFatal step := by
  intro hc
  have := hc init init_reachable rfl .garbage rfl (fault init) rfl
  revert this; decide

/-- A garbage frame cancels the call accepted before it. -/
theorem badFrameKeepsOtherCalls_refuted : ¬ Claim.BadFrameKeepsOtherCalls step := by
  intro hc
  have := hc oneCall oneCall_reachable rfl .garbage rfl (fault oneCall) rfl ⟨1, .call, .waiting, false⟩ (by decide)
  revert this; decide

/-- A wrong revision ends the loop instead of an error reply for that id. -/
theorem wrongRevisionRefusedBeforeDispatch_refuted : ¬ Claim.WrongRevisionRefusedBeforeDispatch step := by
  intro hc
  have := hc init init_reachable rfl 1 .call (fault init) rfl
  revert this; decide

/-- A duplicate id ends the loop. -/
theorem duplicateOrFullRefusedForThatId_refuted : ¬ Claim.DuplicateOrFullRefusedForThatId step := by
  intro hc
  have := hc oneCall oneCall_reachable rfl 1 .call (Or.inl (by decide)) (fault oneCall) rfl
  revert this; decide

/-- Not only end of input ends the loop. -/
theorem onlyEndOfInputEndsTheLoop_refuted : ¬ Claim.OnlyEndOfInputEndsTheLoop step := by
  intro hc
  have := hc init init_reachable rfl (.frame .garbage) (fault init) rfl (by decide)
  revert this; decide

end NamiProofs.HostEngine.Engine
