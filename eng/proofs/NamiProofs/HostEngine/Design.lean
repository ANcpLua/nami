/-!
# Which fix keeps the two slot counts together

The bookkeeping of the 32 slots, projected out of `System.lean`: the host's waiting ids
(`host`, engine.ts:22), the frames in the stdin pipe (`pipe`), the engine's `_pending` ids
(`eng`, EngineProtocol.cs:24), and the reply frames in the stdout pipe (`replies`). A host
request is a call frame, an abort a cancel frame (engine.ts:69-86, 76-81); a reply frees the
host's slot when the host reads it (engine.ts:55).

Two engine changes are flags:
* `removeOnCancel`: a cancel frame removes the entry at once (`TryRemove`, then `Cancel`),
  instead of leaving it until the cancelled task has ended (EngineProtocol.cs:70, 173);
* `removeBeforeReply`: the entry is removed before the reply is written, instead of after
  `Respond` returned (EngineProtocol.cs:149 then 173).
A refused frame sets `refused` whether the engine throws (today, line 75) or answers that id
with an error (P14); either way a call that the host's own limit admitted did not run.

Precondition, not modelled here: every frame the host sends decodes. Today it does not
(`System.deliverUndecodable`); the engine fix for that (decode each frame inside a `try` and
answer that id with an error, P14 and the engine fuzzer's branch) is separate from slot
accounting, and its error reply frees the host's slot like any reply.

Proved (`fixed_noRefusal`): with both changes, no frame the host sends is ever refused.
Refuted: today's engine (trace A), each change alone (traces A and B). The call frame carries
a ghost copy of the host's waiting ids when it was sent; ghosts are not program state.
-/

namespace NamiProofs.HostEngine.Design

def limit : Nat := 32

inductive Frame where
  /-- A call; `ghost` holds the host's waiting ids just before it was added. -/
  | call (id : Nat) (ghost : List Nat)
  | cancel (id : Nat)
  deriving DecidableEq, Repr

structure Fix where
  removeOnCancel : Bool
  removeBeforeReply : Bool
  deriving DecidableEq, Repr

structure St where
  seq : Nat := 0
  host : List Nat := []
  pipe : List Frame := []
  eng : List Nat := []
  /-- Entries whose task has ended (reply written or token cancelled) but are still in `eng`. -/
  ending : List Nat := []
  replies : List Nat := []
  refused : Bool := false
  deriving DecidableEq, Repr

inductive Ev where
  /-- A caller enters `Engine.request` and the host has a free slot. -/
  | request
  | abort (id : Nat)
  /-- The engine's read loop takes the next frame. -/
  | deliver
  /-- A running call finishes and writes its reply. -/
  | respond (id : Nat)
  /-- The `finally` of an ended task removes its entry. -/
  | release (id : Nat)
  /-- The host reads the next reply frame. -/
  | receive
  deriving DecidableEq, Repr

def step (fx : Fix) (s : St) : Ev → Option St
  | .request =>
    if s.host.length < limit then
      let id := s.seq + 1
      some { s with seq := id, host := s.host ++ [id], pipe := s.pipe ++ [.call id s.host] }
    else none
  | .abort a =>
    if a ∈ s.host then some { s with host := s.host.erase a, pipe := s.pipe ++ [.cancel a] } else none
  | .deliver =>
    match s.pipe with
    | [] => none
    | .call k _ :: rest =>
      if s.eng.length ≥ limit || s.eng.contains k then some { s with pipe := rest, refused := true }
      else some { s with pipe := rest, eng := k :: s.eng }
    | .cancel a :: rest =>
      if fx.removeOnCancel then some { s with pipe := rest, eng := s.eng.filter (· != a) }
      else if s.eng.contains a then some { s with pipe := rest, ending := s.ending ++ [a] }
      else some { s with pipe := rest }
  | .respond id =>
    if id ∈ s.eng ∧ id ∉ s.ending then
      if fx.removeBeforeReply then some { s with eng := s.eng.filter (· != id), replies := s.replies ++ [id] }
      else some { s with ending := s.ending ++ [id], replies := s.replies ++ [id] }
    else none
  | .release id =>
    if id ∈ s.ending then
      some { s with eng := s.eng.filter (· != id), ending := s.ending.filter (· != id) }
    else none
  | .receive =>
    match s.replies with
    | [] => none
    | r :: rest => some { s with replies := rest, host := s.host.erase r }

def run (fx : Fix) (s : St) : List Ev → Option St
  | [] => some s
  | e :: es => (step fx s e).bind (run fx · es)

def Reachable (fx : Fix) (s : St) : Prop := ∃ es, run fx {} es = some s

/-- No frame a correct host sends is refused. -/
def NoRefusal (fx : Fix) : Prop := ∀ s, Reachable fx s → s.refused = false

def today : Fix := ⟨false, false⟩
def fixed : Fix := ⟨true, true⟩

/-! ## Counterexamples -/

def fill : List Ev := List.replicate 32 .request ++ List.replicate 32 .deliver

/-- Trace A: abort at the limit, then a new call. -/
def traceA : List Ev := fill ++ [.abort 32, .request, .deliver, .deliver]

/-- Trace B: the reply is read before the entry is removed. -/
def traceB : List Ev := fill ++ [.respond 1, .receive, .request, .deliver]

theorem today_refuses : (run today {} traceA).map (·.refused) = some true := by decide
theorem today_refuses_B : (run today {} traceB).map (·.refused) = some true := by decide
theorem removeOnCancel_alone_refuses : (run ⟨true, false⟩ {} traceB).map (·.refused) = some true := by decide
theorem removeBeforeReply_alone_refuses : (run ⟨false, true⟩ {} traceA).map (·.refused) = some true := by decide
theorem fixed_accepts_A : (run fixed {} traceA).map (·.refused) = some false := by decide
theorem fixed_accepts_B : (run fixed {} traceB).map (·.refused) = some false := by decide

theorem today_noRefusal_refuted : ¬ NoRefusal today := by
  intro h
  have := h _ ⟨traceA, rfl⟩
  revert this; decide

theorem removeOnCancel_alone_refuted : ¬ NoRefusal ⟨true, false⟩ := by
  intro h
  have := h _ ⟨traceB, rfl⟩
  revert this; decide

theorem removeBeforeReply_alone_refuted : ¬ NoRefusal ⟨false, true⟩ := by
  intro h
  have := h _ ⟨traceA, rfl⟩
  revert this; decide

/-! ## The fix that holds -/

def callIds : List Frame → List Nat
  | [] => []
  | .call k _ :: rest => k :: callIds rest
  | .cancel _ :: rest => callIds rest

/-- The engine's ids once it has read the whole pipe, starting from `E`. -/
def endSet (E : List Nat) : List Frame → List Nat
  | [] => E
  | .call k _ :: rest => endSet (k :: E) rest
  | .cancel a :: rest => endSet (E.filter (· != a)) rest

/-- Every call frame in the pipe will find the engine's ids inside its ghost, and itself
outside it: the engine's set when the frame arrives is part of the host's set when it was
sent, which had room for it. -/
def pipeOK (E : List Nat) : List Frame → Prop
  | [] => True
  | .call k g :: rest => (∀ x ∈ E, x ∈ g) ∧ k ∉ g ∧ g.length < limit ∧ g.Nodup ∧ pipeOK (k :: E) rest
  | .cancel a :: rest => pipeOK (E.filter (· != a)) rest

theorem pipeOK_mono : ∀ (P : List Frame) {E E' : List Nat}, (∀ x ∈ E', x ∈ E) → pipeOK E P → pipeOK E' P
  | [], _, _, _, _ => trivial
  | .call k g :: rest, E, E', h, ⟨h1, h2, h3, h4, h5⟩ =>
    ⟨fun x hx => h1 x (h x hx), h2, h3, h4,
      pipeOK_mono rest (fun x hx => by
        simp only [List.mem_cons] at hx ⊢
        rcases hx with rfl | hx
        · exact Or.inl rfl
        · exact Or.inr (h x hx)) h5⟩
  | .cancel a :: rest, E, E', h, h5 =>
    pipeOK_mono rest (E := E.filter (· != a)) (fun x hx => by
      simp only [List.mem_filter] at hx ⊢
      exact ⟨h x hx.1, hx.2⟩) h5

theorem endSet_mono : ∀ (P : List Frame) {E E' : List Nat}, (∀ x ∈ E', x ∈ E) →
    ∀ x ∈ endSet E' P, x ∈ endSet E P
  | [], _, _, h => h
  | .call k _ :: rest, E, E', h =>
    endSet_mono rest (E := k :: E) (fun x hx => by
      simp only [List.mem_cons] at hx ⊢
      rcases hx with rfl | hx
      · exact Or.inl rfl
      · exact Or.inr (h x hx))
  | .cancel a :: rest, E, E', h =>
    endSet_mono rest (E := E.filter (· != a)) (fun x hx => by
      simp only [List.mem_filter] at hx ⊢
      exact ⟨h x hx.1, hx.2⟩)

theorem endSet_sub : ∀ (P : List Frame) (E : List Nat), ∀ x ∈ endSet E P, x ∈ E ∨ x ∈ callIds P
  | [], _, _, h => Or.inl h
  | .call k _ :: rest, E, x, h => by
    rcases endSet_sub rest (k :: E) x h with h | h
    · simp only [List.mem_cons] at h
      rcases h with rfl | h
      · exact Or.inr (by simp [callIds])
      · exact Or.inl h
    · exact Or.inr (by simp [callIds, h])
  | .cancel a :: rest, E, x, h => by
    rcases endSet_sub rest (E.filter (· != a)) x h with h | h
    · exact Or.inl (List.mem_filter.mp h).1
    · exact Or.inr (by simpa [callIds] using h)

theorem endSet_append_call : ∀ (P : List Frame) (E : List Nat) (k : Nat) (g : List Nat),
    ∀ x, x ∈ endSet E (P ++ [.call k g]) ↔ x = k ∨ x ∈ endSet E P
  | [], E, k, g, x => by simp [endSet]
  | .call k' _ :: rest, E, k, g, x => endSet_append_call rest (k' :: E) k g x
  | .cancel a :: rest, E, k, g, x => endSet_append_call rest (E.filter (· != a)) k g x

theorem endSet_append_cancel : ∀ (P : List Frame) (E : List Nat) (a : Nat),
    ∀ x, x ∈ endSet E (P ++ [.cancel a]) ↔ x ∈ endSet E P ∧ x ≠ a
  | [], E, a, x => by simp [endSet]
  | .call k _ :: rest, E, a, x => endSet_append_cancel rest (k :: E) a x
  | .cancel b :: rest, E, a, x => endSet_append_cancel rest (E.filter (· != b)) a x

theorem callIds_append_call (P : List Frame) (k : Nat) (g : List Nat) :
    callIds (P ++ [.call k g]) = callIds P ++ [k] := by
  induction P with
  | nil => rfl
  | cons f rest ih => cases f <;> simp [callIds, ih]

theorem callIds_append_cancel (P : List Frame) (a : Nat) : callIds (P ++ [.cancel a]) = callIds P := by
  induction P with
  | nil => rfl
  | cons f rest ih => cases f <;> simp [callIds, ih]

theorem pipeOK_append_call : ∀ (P : List Frame) (E : List Nat) (k : Nat) (g : List Nat),
    pipeOK E P → (∀ x ∈ endSet E P, x ∈ g) → k ∉ g → g.length < limit → g.Nodup →
    pipeOK E (P ++ [.call k g])
  | [], _, _, _, _, hl, hk, hn, hd => ⟨hl, hk, hn, hd, trivial⟩
  | .call k' _ :: rest, E, k, g, ⟨h1, h2, h3, h4, h5⟩, hl, hk, hn, hd =>
    ⟨h1, h2, h3, h4, pipeOK_append_call rest (k' :: E) k g h5 hl hk hn hd⟩
  | .cancel a :: rest, E, k, g, h5, hl, hk, hn, hd =>
    pipeOK_append_call rest (E.filter (· != a)) k g h5 hl hk hn hd

theorem pipeOK_append_cancel : ∀ (P : List Frame) (E : List Nat) (a : Nat),
    pipeOK E P → pipeOK E (P ++ [.cancel a])
  | [], _, _, _ => trivial
  | .call k _ :: rest, E, a, ⟨h1, h2, h3, h4, h5⟩ =>
    ⟨h1, h2, h3, h4, pipeOK_append_cancel rest (k :: E) a h5⟩
  | .cancel b :: rest, E, a, h5 => pipeOK_append_cancel rest (E.filter (· != b)) a h5

structure Inv (s : St) : Prop where
  ok : pipeOK s.eng s.pipe
  live : ∀ x ∈ endSet s.eng s.pipe, x ∈ s.host
  replies_out : ∀ r ∈ s.replies, r ∉ s.eng ∧ r ∉ callIds s.pipe
  eng_pipe : ∀ x ∈ s.eng, x ∉ callIds s.pipe
  fresh_host : ∀ x ∈ s.host, x ≤ s.seq
  fresh_eng : ∀ x ∈ s.eng, x ≤ s.seq
  fresh_pipe : ∀ x ∈ callIds s.pipe, x ≤ s.seq
  fresh_replies : ∀ x ∈ s.replies, x ≤ s.seq
  host_nodup : s.host.Nodup
  eng_nodup : s.eng.Nodup
  pipe_nodup : (callIds s.pipe).Nodup
  ending_nil : s.ending = []
  not_refused : s.refused = false

theorem inv_init : Inv {} := by
  constructor <;> simp [pipeOK, endSet, callIds]

theorem inv_step {s s' : St} {e : Ev} (h : Inv s) (hs : step fixed s e = some s') : Inv s' := by
  cases e with
  | request =>
    simp only [step] at hs
    split at hs
    · rename_i hlen
      simp only [Option.some.injEq] at hs
      subst hs
      have hfresh : s.seq + 1 ∉ s.host := fun hm => by have := h.fresh_host _ hm; omega
      constructor <;> (try dsimp only)
      · exact pipeOK_append_call s.pipe s.eng _ _ h.ok h.live hfresh hlen h.host_nodup
      · intro x hx
        rcases (endSet_append_call s.pipe s.eng _ _ x).mp hx with rfl | hx
        · simp
        · simp [h.live x hx]
      · intro r hr
        refine ⟨h.replies_out r hr |>.1, ?_⟩
        rw [callIds_append_call]
        simp only [List.mem_append, List.mem_singleton, not_or]
        exact ⟨h.replies_out r hr |>.2, by have := h.fresh_replies r hr; omega⟩
      · intro x hx
        rw [callIds_append_call]
        simp only [List.mem_append, List.mem_singleton, not_or]
        exact ⟨h.eng_pipe x hx, by have := h.fresh_eng x hx; omega⟩
      · intro x hx
        simp only [List.mem_append, List.mem_singleton] at hx
        rcases hx with hx | rfl
        · have := h.fresh_host x hx; omega
        · omega
      · intro x hx; have := h.fresh_eng x hx; omega
      · intro x hx
        rw [callIds_append_call] at hx
        simp only [List.mem_append, List.mem_singleton] at hx
        rcases hx with hx | rfl
        · have := h.fresh_pipe x hx; omega
        · omega
      · intro x hx; have := h.fresh_replies x hx; omega
      · exact List.nodup_append.mpr ⟨h.host_nodup, by simp,
          fun a ha b hb => by simp at hb; subst hb; exact fun he => hfresh (he ▸ ha)⟩
      · exact h.eng_nodup
      · rw [callIds_append_call]
        exact List.nodup_append.mpr ⟨h.pipe_nodup, by simp,
          fun a ha b hb => by simp at hb; subst hb; intro he; subst he; have := h.fresh_pipe _ ha; omega⟩
      · exact h.ending_nil
      · exact h.not_refused
    · simp at hs
  | abort a =>
    simp only [step] at hs
    split at hs
    · simp only [Option.some.injEq] at hs
      subst hs
      constructor <;> (try dsimp only)
      · exact pipeOK_append_cancel s.pipe s.eng a h.ok
      · intro x hx
        obtain ⟨hx, hne⟩ := (endSet_append_cancel s.pipe s.eng a x).mp hx
        exact (List.mem_erase_of_ne hne).mpr (h.live x hx)
      · intro r hr; rw [callIds_append_cancel]; exact h.replies_out r hr
      · intro x hx; rw [callIds_append_cancel]; exact h.eng_pipe x hx
      · intro x hx; exact h.fresh_host x (List.mem_of_mem_erase hx)
      · exact h.fresh_eng
      · intro x hx; rw [callIds_append_cancel] at hx; exact h.fresh_pipe x hx
      · exact h.fresh_replies
      · exact h.host_nodup.erase a
      · exact h.eng_nodup
      · rw [callIds_append_cancel]; exact h.pipe_nodup
      · exact h.ending_nil
      · exact h.not_refused
    · simp at hs
  | deliver =>
    simp only [step] at hs
    split at hs
    · simp at hs
    · rename_i k g rest hp
      have hok := h.ok
      rw [hp] at hok
      obtain ⟨h1, h2, h3, h4, h5⟩ := hok
      have hlen : s.eng.length ≤ g.length := List.Nodup.length_le_of_subset h.eng_nodup h1
      have hk : k ∉ s.eng := fun hm => h2 (h1 k hm)
      have hcond : (s.eng.length ≥ limit || s.eng.contains k) = false := by
        simp only [Bool.or_eq_false_iff, decide_eq_false_iff_not, Nat.not_le]
        exact ⟨by omega, by simpa using hk⟩
      rw [hcond] at hs
      simp only [Bool.false_eq_true, ite_false, Option.some.injEq] at hs
      subst hs
      have hpipe := h.pipe_nodup
      rw [hp] at hpipe
      simp only [callIds, List.nodup_cons] at hpipe
      constructor <;> (try dsimp only)
      · exact h5
      · intro x hx; have := h.live; rw [hp] at this; exact this x hx
      · intro r hr
        obtain ⟨hr1, hr2⟩ := h.replies_out r hr
        rw [hp] at hr2
        simp only [callIds, List.mem_cons, not_or] at hr2
        simp only [List.mem_cons, not_or]
        exact ⟨⟨hr2.1, hr1⟩, hr2.2⟩
      · intro x hx
        simp only [List.mem_cons] at hx
        rcases hx with rfl | hx
        · exact hpipe.1
        · have := h.eng_pipe x hx; rw [hp] at this; simp [callIds] at this; exact this.2
      · exact h.fresh_host
      · intro x hx
        simp only [List.mem_cons] at hx
        rcases hx with rfl | hx
        · have := h.fresh_pipe x; rw [hp] at this; exact this (by simp [callIds])
        · exact h.fresh_eng x hx
      · intro x hx; have := h.fresh_pipe x; rw [hp] at this; exact this (by simp [callIds, hx])
      · exact h.fresh_replies
      · exact h.host_nodup
      · exact List.nodup_cons.mpr ⟨hk, h.eng_nodup⟩
      · exact hpipe.2
      · exact h.ending_nil
      · exact h.not_refused
    · rename_i a rest hp
      simp only [fixed, ite_true, Option.some.injEq] at hs
      subst hs
      have hok := h.ok; rw [hp] at hok
      constructor <;> (try dsimp only)
      · exact hok
      · intro x hx; have := h.live; rw [hp] at this; exact this x hx
      · intro r hr
        obtain ⟨hr1, hr2⟩ := h.replies_out r hr
        rw [hp] at hr2
        exact ⟨fun hm => hr1 (List.mem_filter.mp hm).1, by simpa [callIds] using hr2⟩
      · intro x hx
        have := h.eng_pipe x (List.mem_filter.mp hx).1
        rw [hp] at this; simpa [callIds] using this
      · exact h.fresh_host
      · intro x hx; exact h.fresh_eng x (List.mem_filter.mp hx).1
      · intro x hx; have := h.fresh_pipe x; rw [hp] at this; exact this (by simpa [callIds] using hx)
      · exact h.fresh_replies
      · exact h.host_nodup
      · exact h.eng_nodup.filter _
      · have := h.pipe_nodup; rw [hp] at this; simpa [callIds] using this
      · exact h.ending_nil
      · exact h.not_refused
  | respond id =>
    simp only [step] at hs
    split at hs
    · rename_i hc
      simp only [fixed, ite_true, Option.some.injEq] at hs
      subst hs
      constructor <;> (try dsimp only)
      · exact pipeOK_mono s.pipe (fun x hx => (List.mem_filter.mp hx).1) h.ok
      · intro x hx; exact h.live x (endSet_mono s.pipe (fun y hy => (List.mem_filter.mp hy).1) x hx)
      · intro r hr
        simp only [List.mem_append, List.mem_singleton] at hr
        rcases hr with hr | rfl
        · exact ⟨fun hm => (h.replies_out r hr).1 (List.mem_filter.mp hm).1, (h.replies_out r hr).2⟩
        · exact ⟨by simp [List.mem_filter], h.eng_pipe _ hc.1⟩
      · intro x hx; exact h.eng_pipe x (List.mem_filter.mp hx).1
      · exact h.fresh_host
      · intro x hx; exact h.fresh_eng x (List.mem_filter.mp hx).1
      · exact h.fresh_pipe
      · intro x hx
        simp only [List.mem_append, List.mem_singleton] at hx
        rcases hx with hx | rfl
        · exact h.fresh_replies x hx
        · exact h.fresh_eng _ hc.1
      · exact h.host_nodup
      · exact h.eng_nodup.filter _
      · exact h.pipe_nodup
      · exact h.ending_nil
      · exact h.not_refused
    · simp at hs
  | release id =>
    simp only [step, h.ending_nil] at hs
    simp at hs
  | receive =>
    simp only [step] at hs
    split at hs
    · simp at hs
    · rename_i r rest hr
      simp only [Option.some.injEq] at hs
      subst hs
      have hro := h.replies_out r (by simp [hr])
      constructor <;> (try dsimp only)
      · exact h.ok
      · intro x hx
        have hne : x ≠ r := by
          intro he; subst he
          rcases endSet_sub s.pipe s.eng x hx with h' | h'
          · exact hro.1 h'
          · exact hro.2 h'
        exact (List.mem_erase_of_ne hne).mpr (h.live x hx)
      · intro r' hr'; exact h.replies_out r' (by simp [hr, hr'])
      · exact h.eng_pipe
      · intro x hx; exact h.fresh_host x (List.mem_of_mem_erase hx)
      · exact h.fresh_eng
      · exact h.fresh_pipe
      · intro x hx; exact h.fresh_replies x (by simp [hr, hx])
      · exact h.host_nodup.erase r
      · exact h.eng_nodup
      · exact h.pipe_nodup
      · exact h.ending_nil
      · exact h.not_refused

theorem inv_run : ∀ (es : List Ev) {s s' : St}, Inv s → run fixed s es = some s' → Inv s'
  | [], _, _, h, hr => by simp [run] at hr; subst hr; exact h
  | e :: es, s, s', h, hr => by
    simp only [run] at hr
    cases hs : step fixed s e with
    | none => simp [hs] at hr
    | some s1 => simp only [hs, Option.bind_some] at hr; exact inv_run es (inv_step h hs) hr

/-- With both changes, no frame a correct host sends is ever refused. -/
theorem fixed_noRefusal : NoRefusal fixed := by
  intro s ⟨es, hr⟩
  exact (inv_run es inv_init hr).not_refused

end NamiProofs.HostEngine.Design
