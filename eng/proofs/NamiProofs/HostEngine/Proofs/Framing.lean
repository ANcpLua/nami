import NamiProofs.HostEngine.Framing

/-! Proofs about `Framing`: one read at a time equals one byte at a time. -/

namespace NamiProofs.HostEngine.Framing

theorem splitAtNL_none : ∀ {buf : List Nat}, splitAtNL buf = none → ∀ b ∈ buf, b ≠ NL
  | [], _ => by simp
  | b :: bs, h => by
    unfold splitAtNL at h
    split at h
    · simp at h
    · rename_i hb
      cases hs : splitAtNL bs with
      | none =>
        intro x hx
        simp at hx
        rcases hx with rfl | hx
        · exact hb
        · exact splitAtNL_none hs x hx
      | some p => simp [hs] at h

theorem splitAtNL_some : ∀ {buf f r : List Nat}, splitAtNL buf = some (f, r) →
    buf = f ++ NL :: r ∧ ∀ b ∈ f, b ≠ NL
  | [], _, _, h => by simp [splitAtNL] at h
  | b :: bs, f, r, h => by
    unfold splitAtNL at h
    split at h
    · rename_i hb
      simp at h
      obtain ⟨rfl, rfl⟩ := h
      simp [hb]
    · rename_i hb
      cases hs : splitAtNL bs with
      | none => simp [hs] at h
      | some p =>
        simp [hs] at h
        obtain ⟨rfl, rfl⟩ := h
        obtain ⟨h1, h2⟩ := splitAtNL_some (buf := bs) (f := p.1) (r := p.2) (by simp [hs])
        refine ⟨by simp [h1], ?_⟩
        intro x hx
        simp at hx
        rcases hx with rfl | hx
        · exact hb
        · exact h2 x hx

/-- A run of bytes without a newline only grows the kept frame, or faults once it is longer
than the limit. -/
theorem foldlM_byteStep_segment (lim : Nat) : ∀ (xs : List Nat) (D : List (List Nat)) (p : List Nat),
    p.length ≤ lim → (∀ b ∈ xs, b ≠ NL) →
    xs.foldlM (byteStep lim) ⟨D, p⟩ =
      if p.length + xs.length ≤ lim then some ⟨D, p ++ xs⟩ else none
  | [], D, p, hp, _ => by simp [hp]
  | x :: xs, D, p, hp, h => by
    have hx : x ≠ NL := h x (by simp)
    have hxs : ∀ b ∈ xs, b ≠ NL := fun b hb => h b (by simp [hb])
    simp only [List.foldlM_cons, byteStep, hx, ite_false]
    by_cases hl : p.length ≥ lim
    · simp only [hl, ite_true]
      simp
      omega
    · simp only [hl, ite_false]
      have hp' : (p ++ [x]).length ≤ lim := by simp; omega
      show (List.foldlM (byteStep lim) ⟨D, p ++ [x]⟩ xs) = _
      rw [foldlM_byteStep_segment lim xs D (p ++ [x]) hp' hxs]
      simp only [List.length_append, List.length_cons, List.length_nil, List.append_assoc,
        List.singleton_append]
      have : p.length + 1 + xs.length = p.length + (xs.length + 1) := by omega
      rw [this]

/-- Frames handed on earlier do not change what later bytes do. -/
theorem foldlM_byteStep_prefix (lim : Nat) : ∀ (xs : List Nat) (D E : List (List Nat)) (p : List Nat),
    xs.foldlM (byteStep lim) ⟨D ++ E, p⟩ =
      (xs.foldlM (byteStep lim) ⟨E, p⟩).map (fun rd => ⟨D ++ rd.done, rd.buffer⟩)
  | [], D, E, p => by simp
  | x :: xs, D, E, p => by
    simp only [List.foldlM_cons, byteStep]
    by_cases hx : x = NL
    · simp only [hx, ite_true]
      show List.foldlM (byteStep lim) ⟨D ++ E ++ [p], []⟩ xs = _
      rw [List.append_assoc, foldlM_byteStep_prefix lim xs D (E ++ [p]) []]
      rfl
    · simp only [hx, ite_false]
      by_cases hl : p.length ≥ lim
      · simp [hl]
      · simp only [hl, ite_false]
        exact foldlM_byteStep_prefix lim xs D E (p ++ [x])

/-- The inner loop over one buffer is the byte-at-a-time meaning of that buffer. -/
theorem drain_eq_bytes (lim : Nat) : ∀ (n : Nat) (buf : List Nat), buf.length < n →
    drain lim buf = (buf.foldlM (byteStep lim) Reader.empty).map (fun rd => (rd.done, rd.buffer))
  | 0, _, h => by simp at h
  | n + 1, buf, hn => by
    rw [drain]
    split
    · rename_i hs
      have hno := splitAtNL_none hs
      rw [show Reader.empty = ⟨[], []⟩ from rfl,
        foldlM_byteStep_segment lim buf [] [] (by simp) hno]
      by_cases hl : buf.length > lim
      · have : ¬ buf.length ≤ lim := by omega
        simp [hl, this]
      · have : buf.length ≤ lim := by omega
        simp [hl, this]
    · rename_i f r hs
      obtain ⟨hbuf, hf⟩ := splitAtNL_some hs
      have hr : r.length < n := by
        have := splitAtNL_length hs; omega
      rw [hbuf, List.foldlM_append, show Reader.empty = ⟨[], []⟩ from rfl,
        foldlM_byteStep_segment lim f [] [] (by simp) hf]
      by_cases hl : f.length > lim
      · have : ¬ f.length ≤ lim := by omega
        simp [hl, this]
      · have : f.length ≤ lim := by omega
        simp only [hl, ite_false]
        show Option.map (fun p => (f :: p.fst, p.snd)) (drain lim r) =
          Option.map (fun rd => (rd.done, rd.buffer))
            ((if ([] : List Nat).length + f.length ≤ lim then some (⟨[], [] ++ f⟩ : Reader) else none).bind
              (fun rd => (byteStep lim rd NL).bind (fun rd => List.foldlM (byteStep lim) rd r)))
        have h0 : ([] : List Nat).length + f.length ≤ lim := by simp; omega
        simp only [h0, ite_true, Option.bind_some, byteStep, List.nil_append]
        rw [drain_eq_bytes lim n r hr]
        have := foldlM_byteStep_prefix lim r [f] [] []
        simp only [List.append_nil] at this
        rw [this]
        simp [Option.map_map, Function.comp_def, Reader.empty]

/-- The bytes a reader keeps: no newline, and within the limit. -/
def Good (lim : Nat) (rd : Reader) : Prop := (∀ b ∈ rd.buffer, b ≠ NL) ∧ rd.buffer.length ≤ lim

theorem byteStep_good {lim : Nat} {rd rd' : Reader} {b : Nat} :
    Good lim rd → byteStep lim rd b = some rd' → Good lim rd' := by
  intro ⟨h1, h2⟩ h
  unfold byteStep at h
  by_cases hb : b = NL
  · simp [hb] at h; subst h; simp [Good]
  · simp only [hb, ite_false] at h
    by_cases hl : rd.buffer.length ≥ lim
    · simp [hl] at h
    · simp only [hl, ite_false, Option.some.injEq] at h
      subst h
      refine ⟨?_, by simp; omega⟩
      intro x hx
      simp at hx
      rcases hx with hx | rfl
      · exact h1 x hx
      · exact hb

theorem foldlM_byteStep_good (lim : Nat) : ∀ (xs : List Nat) {rd rd' : Reader},
    Good lim rd → xs.foldlM (byteStep lim) rd = some rd' → Good lim rd'
  | [], rd, rd', hg, h => by simp at h; subst h; exact hg
  | x :: xs, rd, rd', hg, h => by
    simp only [List.foldlM_cons] at h
    cases hs : byteStep lim rd x with
    | none => simp [hs] at h
    | some r1 =>
      simp only [hs] at h
      exact foldlM_byteStep_good lim xs (byteStep_good hg hs) h

/-- One read of the code equals feeding its bytes one at a time. -/
theorem onChunk_eq_bytes (lim : Nat) (rd : Reader) (data : List Nat) (hg : Good lim rd) :
    onChunk lim rd data = data.foldlM (byteStep lim) rd := by
  unfold onChunk
  rw [drain_eq_bytes lim ((rd.buffer ++ data).length + 1) _ (by omega)]
  have hpre := foldlM_byteStep_prefix lim (rd.buffer ++ data) rd.done [] []
  simp only [List.append_nil] at hpre
  rw [Option.map_map]
  have : (fun p : List (List Nat) × List Nat => (⟨rd.done ++ p.1, p.2⟩ : Reader)) ∘
      (fun r : Reader => (r.done, r.buffer)) = (fun r : Reader => ⟨rd.done ++ r.done, r.buffer⟩) := by
    funext r; rfl
  rw [this, show Reader.empty = ⟨[], []⟩ from rfl, ← hpre, List.foldlM_append,
    foldlM_byteStep_segment lim rd.buffer rd.done [] (by simp) hg.1]
  simp [hg.2]

theorem foldlM_onChunk_eq_flatten (lim : Nat) : ∀ (cs : List (List Nat)) (rd : Reader), Good lim rd →
    cs.foldlM (onChunk lim) rd = cs.flatten.foldlM (byteStep lim) rd
  | [], rd, _ => by simp
  | c :: cs, rd, hg => by
    simp only [List.foldlM_cons, List.flatten_cons, List.foldlM_append, onChunk_eq_bytes lim rd c hg]
    cases hs : c.foldlM (byteStep lim) rd with
    | none => rfl
    | some r1 =>
      exact foldlM_onChunk_eq_flatten lim cs r1 (foldlM_byteStep_good lim c hg hs)

/-- Fragmented and coalesced reads give the same frames, and the same fault, as one read. -/
theorem readAll_chunking_invariant (lim : Nat) (cs : List (List Nat)) :
    readAllBy onChunk lim cs = readAllBy onChunk lim [cs.flatten] := by
  have hg : Good lim Reader.empty := ⟨by simp [Reader.empty], by simp [Reader.empty]⟩
  unfold readAllBy
  rw [foldlM_onChunk_eq_flatten lim cs _ hg, foldlM_onChunk_eq_flatten lim [cs.flatten] _ hg]
  simp

/-! ## The engine's reader -/

/-- While skipping, bytes without a newline change nothing. -/
theorem foldl_skipByte_skipping (lim : Nat) : ∀ (xs : List Nat) (D : List (List Nat)),
    (∀ b ∈ xs, b ≠ NL) → xs.foldl (skipByte lim) ⟨D, [], true⟩ = ⟨D, [], true⟩
  | [], _, _ => rfl
  | x :: xs, D, h => by
    have hx : x ≠ NL := h x (by simp)
    simp only [List.foldl_cons, skipByte, hx, ite_false, ite_true]
    exact foldl_skipByte_skipping lim xs D (fun b hb => h b (by simp [hb]))

/-- A run of bytes without a newline grows the kept frame, or starts skipping once it is
longer than the limit. -/
theorem foldl_skipByte_segment (lim : Nat) : ∀ (xs : List Nat) (D : List (List Nat)) (p : List Nat),
    p.length ≤ lim → (∀ b ∈ xs, b ≠ NL) →
    xs.foldl (skipByte lim) ⟨D, p, false⟩ =
      if p.length + xs.length ≤ lim then ⟨D, p ++ xs, false⟩ else ⟨D, [], true⟩
  | [], D, p, hp, _ => by simp [hp]
  | x :: xs, D, p, hp, h => by
    have hx : x ≠ NL := h x (by simp)
    have hxs : ∀ b ∈ xs, b ≠ NL := fun b hb => h b (by simp [hb])
    simp only [List.foldl_cons, skipByte, hx, ite_false, Bool.false_eq_true]
    by_cases hl : p.length ≥ lim
    · simp only [hl, ite_true]
      rw [foldl_skipByte_skipping lim xs D hxs]
      have : ¬ p.length + (xs.length + 1) ≤ lim := by omega
      simp only [List.length_cons, this, ite_false]
    · simp only [hl, ite_false]
      have hp' : (p ++ [x]).length ≤ lim := by simp; omega
      rw [foldl_skipByte_segment lim xs D (p ++ [x]) hp' hxs]
      simp only [List.length_append, List.length_cons, List.length_nil, List.append_assoc,
        List.singleton_append]
      have : p.length + (0 + 1) + xs.length = p.length + (xs.length + 1) := by omega
      rw [this]

/-- Frames handed on earlier do not change what later bytes do. -/
theorem foldl_skipByte_prefix (lim : Nat) : ∀ (xs : List Nat) (D E : List (List Nat)) (p : List Nat)
    (sk : Bool), xs.foldl (skipByte lim) ⟨D ++ E, p, sk⟩ =
      ⟨D ++ (xs.foldl (skipByte lim) ⟨E, p, sk⟩).done, (xs.foldl (skipByte lim) ⟨E, p, sk⟩).buffer,
        (xs.foldl (skipByte lim) ⟨E, p, sk⟩).skipping⟩
  | [], D, E, p, sk => rfl
  | x :: xs, D, E, p, sk => by
    simp only [List.foldl_cons, skipByte]
    by_cases hx : x = NL
    · simp only [hx, ite_true]
      cases sk
      · simp only [Bool.false_eq_true, ite_false]
        rw [List.append_assoc]
        exact foldl_skipByte_prefix lim xs D (E ++ [p]) [] false
      · simp only [ite_true]
        exact foldl_skipByte_prefix lim xs D E [] false
    · simp only [hx, ite_false]
      cases sk
      · simp only [Bool.false_eq_true, ite_false]
        by_cases hl : p.length ≥ lim
        · simp only [hl, ite_true]
          exact foldl_skipByte_prefix lim xs D E [] true
        · simp only [hl, ite_false]
          exact foldl_skipByte_prefix lim xs D E (p ++ [x]) false
      · simp only [ite_true]
        exact foldl_skipByte_prefix lim xs D E p true

/-- The engine's inner loop over one buffer is the byte-at-a-time meaning of that buffer. -/
theorem drainSkip_eq_bytes (lim : Nat) : ∀ (n : Nat) (buf : List Nat) (sk : Bool), buf.length < n →
    drainSkip lim sk buf =
      ((buf.foldl (skipByte lim) ⟨[], [], sk⟩).done, (buf.foldl (skipByte lim) ⟨[], [], sk⟩).buffer,
        (buf.foldl (skipByte lim) ⟨[], [], sk⟩).skipping)
  | 0, _, _, h => by simp at h
  | n + 1, buf, sk, hn => by
    rw [drainSkip]
    split
    · rename_i hs
      have hno := splitAtNL_none hs
      cases sk
      · rw [foldl_skipByte_segment lim buf [] [] (by simp) hno]
        by_cases hl : buf.length > lim
        · have : ¬ buf.length ≤ lim := by omega
          simp [hl, this]
        · have : buf.length ≤ lim := by omega
          simp [hl, this]
      · rw [foldl_skipByte_skipping lim buf [] hno]
        simp
    · rename_i f r hs
      obtain ⟨hbuf, hf⟩ := splitAtNL_some hs
      have hr : r.length < n := by
        have := splitAtNL_length hs; omega
      have ih := drainSkip_eq_bytes lim n r false hr
      rw [hbuf, List.foldl_append, List.foldl_cons]
      cases sk
      · rw [foldl_skipByte_segment lim f [] [] (by simp) hf]
        by_cases hl : f.length > lim
        · have : ¬ ([] : List Nat).length + f.length ≤ lim := by simp; omega
          simp only [hl, this, Bool.false_or, decide_true, ite_true, ite_false, skipByte]
          exact ih
        · have h0 : ([] : List Nat).length + f.length ≤ lim := by simp; omega
          simp only [hl, h0, Bool.false_or, decide_false, Bool.false_eq_true, ite_true, ite_false, skipByte,
            List.nil_append]
          rw [ih]
          have := foldl_skipByte_prefix lim r [f] [] [] false
          simp only [List.append_nil] at this
          rw [this]
          rfl
      · rw [foldl_skipByte_skipping lim f [] hf]
        simp only [Bool.true_or, ite_true, skipByte]
        exact ih

/-- The bytes the engine's reader keeps: no newline, within the limit, and none while it
skips a frame. -/
def GoodSkip (lim : Nat) (rd : SkipReader) : Prop :=
  (∀ b ∈ rd.buffer, b ≠ NL) ∧ rd.buffer.length ≤ lim ∧ (rd.skipping = true → rd.buffer = [])

theorem skipByte_good {lim : Nat} {rd : SkipReader} (b : Nat) (hg : GoodSkip lim rd) :
    GoodSkip lim (skipByte lim rd b) := by
  obtain ⟨h1, h2, h3⟩ := hg
  unfold skipByte
  by_cases hb : b = NL
  · simp only [hb, ite_true]
    split <;> simp [GoodSkip]
  · simp only [hb, ite_false]
    by_cases hsk : rd.skipping = true
    · simp only [hsk, ite_true]; exact ⟨h1, h2, h3⟩
    · simp only [hsk, Bool.false_eq_true, ite_false]
      by_cases hl : rd.buffer.length ≥ lim
      · simp [hl, GoodSkip]
      · simp only [hl, ite_false]
        refine ⟨?_, by simp; omega, by simp⟩
        intro x hx
        simp at hx
        rcases hx with hx | rfl
        · exact h1 x hx
        · exact hb

theorem foldl_skipByte_good (lim : Nat) : ∀ (xs : List Nat) {rd : SkipReader},
    GoodSkip lim rd → GoodSkip lim (xs.foldl (skipByte lim) rd)
  | [], _, hg => hg
  | x :: xs, _, hg => foldl_skipByte_good lim xs (skipByte_good x hg)

/-- One read of the engine equals feeding its bytes one at a time. -/
theorem onChunkSkip_eq_bytes (lim : Nat) (rd : SkipReader) (data : List Nat) (hg : GoodSkip lim rd) :
    onChunkSkip lim rd data = data.foldl (skipByte lim) rd := by
  obtain ⟨h1, h2, h3⟩ := hg
  have hkept : rd.buffer.foldl (skipByte lim) ⟨[], [], rd.skipping⟩ = ⟨[], rd.buffer, rd.skipping⟩ := by
    cases hsk : rd.skipping
    · rw [foldl_skipByte_segment lim rd.buffer [] [] (by simp) h1]
      simp [h2]
    · rw [h3 hsk]; rfl
  unfold onChunkSkip
  simp only
  rw [drainSkip_eq_bytes lim ((rd.buffer ++ data).length + 1) _ _ (by omega), List.foldl_append, hkept]
  have hpre := foldl_skipByte_prefix lim data rd.done [] rd.buffer rd.skipping
  simp only [List.append_nil] at hpre
  exact hpre.symm

theorem foldl_onChunkSkip_eq_flatten (lim : Nat) : ∀ (cs : List (List Nat)) (rd : SkipReader),
    GoodSkip lim rd → cs.foldl (onChunkSkip lim) rd = cs.flatten.foldl (skipByte lim) rd
  | [], _, _ => rfl
  | c :: cs, rd, hg => by
    simp only [List.foldl_cons, List.flatten_cons, List.foldl_append, onChunkSkip_eq_bytes lim rd c hg]
    exact foldl_onChunkSkip_eq_flatten lim cs _ (foldl_skipByte_good lim c hg)

theorem goodSkip_empty (lim : Nat) : GoodSkip lim SkipReader.empty :=
  ⟨by simp [SkipReader.empty], by simp [SkipReader.empty], by simp [SkipReader.empty]⟩

/-- For the engine too, fragmented and coalesced reads give the same frames as one read. -/
theorem readAllSkip_chunking_invariant (lim : Nat) (cs : List (List Nat)) :
    readAllSkipBy onChunkSkip lim cs = readAllSkipBy onChunkSkip lim [cs.flatten] := by
  unfold readAllSkipBy
  rw [foldl_onChunkSkip_eq_flatten lim cs _ (goodSkip_empty lim),
    foldl_onChunkSkip_eq_flatten lim [cs.flatten] _ (goodSkip_empty lim)]
  simp

/-- The engine's reader never keeps more than the limit: a frame it skips is not buffered. -/
theorem readAllSkip_keeps_at_most_limit (lim : Nat) (cs : List (List Nat)) :
    (readAllSkipBy onChunkSkip lim cs).buffer.length ≤ lim := by
  unfold readAllSkipBy
  rw [foldl_onChunkSkip_eq_flatten lim cs _ (goodSkip_empty lim)]
  exact (foldl_skipByte_good lim cs.flatten (goodSkip_empty lim)).2.1

end NamiProofs.HostEngine.Framing
