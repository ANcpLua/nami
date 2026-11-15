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

theorem inv_refuse {s : St} (h : Inv s) (id : Nat) : Inv (refuse s id) :=
  ⟨h.ended_cancelled, h.ids_nodup, h.exit_ended⟩

theorem inv_step {s s' : St} {e : Ev} (h : Inv s) (hs : step s e = some s') : Inv s' := by
  obtain ⟨h1, h2, h3⟩ := h
  cases e <;> simp only [step] at hs
  case frame f =>
    split at hs <;> simp at hs; subst hs
    rename_i hr
    have hr' : s.loop = .reading := by simpa using hr
    have h : Inv s := ⟨h1, h2, h3⟩
    cases f with
    | oversize => exact h
    | garbage => exact h
    | undecodable id => exact inv_refuse h id
    | req id m ok =>
      cases ok
      · exact inv_refuse h id
      · simp only [accept]
        split
        · exact inv_refuse h id
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
      · exact h
      · simp only [accept]
        split
        · constructor
          · intro hl; exact absurd hr' hl
          · exact nodup_remove s id h2
          · intro hx; exact absurd hr' (h3 hx)
        · exact h
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
  case unwindEnter =>
    split at hs <;> simp at hs; subst hs; exact ⟨h1, h2, h3⟩
  case unwind worker =>
    split at hs
    · split at hs <;> simp at hs; subst hs; exact ⟨h1, h2, h3⟩
    · split at hs <;> simp at hs; subst hs; exact ⟨h1, h2, h3⟩
  case exitProcess =>
    split at hs <;> simp at hs; subst hs
    rename_i hc
    have hne : s.loop ≠ .reading := by intro heq; simp [heq] at hc
    constructor
    · intro hl; exact h1 hl
    · exact h2
    · intro _; exact hne

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

/-- The tasks whose entries are gone end one by one, then the process exits. -/
theorem drain_ending : ∀ (n : Nat) (s : St), s.endingIdle + s.endingBusy = n → s.pending = [] →
    s.loop ≠ .reading → s.exit = none → ∃ es s', runBy step s es = some s' ∧ s'.exit.isSome = true
  | 0, s, hn, hp, hl, hx => by
    have hi : s.endingIdle = 0 := by omega
    have hb : s.endingBusy = 0 := by omega
    refine ⟨[.exitProcess], { s with exit := some (s.loop != .faulted) }, ?_, by simp⟩
    have hl' : (s.loop != .reading) = true := by simpa using hl
    simp [runBy, step, hl', hp, hx, hi, hb]
  | n + 1, s, hn, hp, hl, hx => by
    by_cases hi : s.endingIdle > 0
    · obtain ⟨es, s', hr, hx'⟩ := drain_ending n { s with endingIdle := s.endingIdle - 1 }
        (by simp only; omega) hp hl hx
      refine ⟨.unwind false :: es, s', ?_, hx'⟩
      simp [runBy, step, hi, hr]
    · have hb : s.endingBusy > 0 := by omega
      obtain ⟨es, s', hr, hx'⟩ := drain_ending n
        { s with endingBusy := s.endingBusy - 1, busy := s.busy - 1 } (by simp only; omega) hp hl hx
      refine ⟨.unwind true :: es, s', ?_, hx'⟩
      simp [runBy, step, hb, hr]

theorem drain_to_exit : ∀ (n : Nat) (s : St), s.pending.length = n → s.loop ≠ .reading →
    s.lifetime = true → (∀ c ∈ s.pending, c.cancelled = true) → (s.pending.map (·.id)).Nodup →
    s.exit = none → ∃ es s', runBy step s es = some s' ∧ s'.exit.isSome = true
  | 0, s, hn, hl, _, _, _, hx =>
    drain_ending _ s rfl (List.eq_nil_of_length_eq_zero hn) hl hx
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
      -- the call ends without a reply: its token is cancelled through `_lifetime`
      cases hph : c.phase with
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

/-! ## A frame never ends the read loop (P14) -/

theorem frame_loop {s s' : St} {f : Frame} (hs : step s (.frame f) = some s') : s'.loop = s.loop := by
  simp only [step] at hs
  split at hs <;> simp at hs; subst hs
  cases f with
  | oversize => rfl
  | garbage => rfl
  | undecodable id => rfl
  | req id m ok =>
    cases ok
    · rfl
    · simp only [accept]; split <;> rfl
  | cancel id ok =>
    cases ok
    · rfl
    · simp only [accept]; split <;> rfl

/-- Only the end of the input and the stop signal change `loop`. -/
theorem step_loop {s s' : St} {e : Ev} (hs : step s e = some s') (h1 : e ≠ .eof) (h2 : e ≠ .eofPartial)
    (h3 : e ≠ .stop) : s'.loop = s.loop := by
  cases e with
  | frame f => exact frame_loop hs
  | eof => exact absurd rfl h1
  | eofPartial => exact absurd rfl h2
  | stop => exact absurd rfl h3
  | enter id =>
    simp only [step] at hs
    split at hs
    · split at hs <;> simp at hs; subst hs; rfl
    · simp at hs
  | respond id isError =>
    simp only [step] at hs
    split at hs
    · split at hs <;> simp at hs; subst hs; rfl
    · simp at hs
  | drop id =>
    simp only [step] at hs
    split at hs
    · split at hs
      · simp at hs; subst hs; rfl
      · split at hs <;> simp at hs; subst hs; rfl
    · simp at hs
  | unwindEnter =>
    simp only [step] at hs
    split at hs <;> simp at hs; subst hs; rfl
  | unwind worker =>
    simp only [step] at hs
    split at hs
    · split at hs <;> simp at hs; subst hs; rfl
    · split at hs <;> simp at hs; subst hs; rfl
  | exitProcess =>
    simp only [step] at hs
    split at hs <;> simp at hs; subst hs; rfl

/-- A frame the engine cannot serve leaves the accepted calls as they were. -/
theorem badFrame_pending {s s' : St} {f : Frame} (hb : Claim.badFrame f = true)
    (hs : step s (.frame f) = some s') : s'.pending = s.pending := by
  simp only [step] at hs
  split at hs <;> simp at hs; subst hs
  cases f with
  | oversize => rfl
  | garbage => rfl
  | undecodable id => rfl
  | req id m ok =>
    cases ok
    · rfl
    · simp [Claim.badFrame] at hb
  | cancel id ok =>
    cases ok
    · rfl
    · simp [Claim.badFrame] at hb

theorem badFrameNotFatal : Claim.BadFrameNotFatal step := by
  intro s _ hl f _ s' hs
  rw [frame_loop hs]; exact hl

theorem badFrameKeepsOtherCalls : Claim.BadFrameKeepsOtherCalls step := by
  intro s _ _ f hb s' hs c hc
  rw [badFrame_pending hb hs]; exact hc

theorem wrongRevisionRefusedBeforeDispatch : Claim.WrongRevisionRefusedBeforeDispatch step := by
  intro s _ hl id m s' hs
  refine ⟨by rw [frame_loop hs]; exact hl, badFrame_pending rfl hs, ?_⟩
  simp only [step] at hs
  split at hs <;> simp at hs; subst hs
  simp [accept, refuse]

theorem duplicateOrFullRefusedForThatId : Claim.DuplicateOrFullRefusedForThatId step := by
  intro s _ hl id m hcond s' hs
  refine ⟨by rw [frame_loop hs]; exact hl, ?_⟩
  simp only [step] at hs
  split at hs <;> simp at hs; subst hs
  have hc : (decide (s.pending.length ≥ maxPending) || (ids s).contains id) = true := by
    rcases hcond with h | h
    · simp only [Bool.or_eq_true, decide_eq_true_eq]; exact Or.inr h
    · simp only [Bool.or_eq_true, decide_eq_true_eq]; exact Or.inl h
  simp only [accept, hc, ite_true, refuse]
  exact fun c hc => hc

theorem onlyEndOfInputEndsTheLoop : Claim.OnlyEndOfInputEndsTheLoop step := by
  intro s _ hl e s' hs hne
  by_cases h1 : e = .eof
  · exact Or.inl h1
  · by_cases h2 : e = .eofPartial
    · exact Or.inr (Or.inl h2)
    · by_cases h3 : e = .stop
      · exact Or.inr (Or.inr h3)
      · exact absurd (by rw [step_loop hs h1 h2 h3]; exact hl) hne

/-! ## States used as witnesses -/

/-- One accepted call, waiting for a worker. -/
def oneCall : St := (run init [.frame (.req 1 .call true)]).get!

theorem oneCall_reachable : Reachable oneCall := ⟨[.frame (.req 1 .call true)], rfl⟩

theorem init_reachable : Reachable init := ⟨[], rfl⟩

end NamiProofs.HostEngine.Engine
