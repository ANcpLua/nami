import NamiStorage.Cursor.Spec
/-
Following `next_cursor` from the first page returns every item once, in order, or stops with an
explicit refusal after exactly the items before it.
-/
namespace Nami.Storage.Cursor

theorem keep_ge (f : Nat → Bool) : ∀ c r, c ≤ keep f c r
  | c, 0 => by simp [keep]
  | c, r + 1 => by
      unfold keep
      split
      · exact Nat.le_trans (Nat.le_succ c) (keep_ge f (c + 1) r)
      · exact Nat.le_refl c

theorem keep_le (f : Nat → Bool) : ∀ c r, keep f c r ≤ c + r
  | c, 0 => by simp [keep]
  | c, r + 1 => by
      unfold keep
      split
      · have := keep_le f (c + 1) r; omega
      · omega

theorem keep_pos (f : Nat → Bool) (r : Nat) (h : f 1 = true) (hr : 0 < r) : 0 < keep f 0 r := by
  cases r with
  | zero => omega
  | succ r =>
      unfold keep
      simp only [Nat.zero_add, h, ite_true]
      exact Nat.lt_of_lt_of_le Nat.zero_lt_one (keep_ge f 1 r)

/-- Pages tile the item list from `off`: whatever the walk returns is a contiguous slice. -/
theorem walk_slices {α : Type} (fits final : Nat → Nat → Bool) (items : List α) :
    ∀ fuel off, off ≤ items.length →
      (∀ got, walk fits final items fuel off = .done got → got = items.drop off) ∧
      (∀ got at_, walk fits final items fuel off = .refused got at_ →
        off ≤ at_ ∧ at_ ≤ items.length ∧ got = (items.drop off).take (at_ - off)) := by
  intro fuel
  induction fuel with
  | zero => intro off _; simp [walk]
  | succ fuel ih =>
      intro off hoff
      have hk := keep_le (fits off) 0 (items.length - off)
      have hp : page fits final items.length off = pageOf final items.length off
          (keep (fits off) 0 (items.length - off)) := rfl
      generalize keep (fits off) 0 (items.length - off) = k at hk hp
      unfold walk
      rw [hp]
      unfold pageOf
      by_cases h1 : off < items.length ∧ k = 0
      · rw [ite_eq_left h1]
        refine ⟨fun got h => (by cases h), fun got at_ h => ?_⟩
        cases h; exact ⟨Nat.le_refl _, hoff, by simp⟩
      · rw [ite_eq_right h1]
        by_cases h2 : final off k = false
        · rw [ite_eq_left h2]
          refine ⟨fun got h => (by cases h), fun got at_ h => ?_⟩
          cases h; exact ⟨Nat.le_refl _, hoff, by simp⟩
        · rw [ite_eq_right h2]
          by_cases h3 : off + k < items.length
          · rw [ite_eq_left h3]
            dsimp only
            obtain ⟨ihd, ihr⟩ := ih (off + k) (by omega)
            cases hw : walk fits final items fuel (off + k) with
            | done got' =>
                simp only []
                refine ⟨fun got h => ?_, fun got at_ h => (by cases h)⟩
                cases h
                rw [ihd got' hw, ← List.drop_drop, List.take_append_drop]
            | refused got' at' =>
                simp only []
                refine ⟨fun got h => (by cases h), fun got at_ h => ?_⟩
                cases h
                obtain ⟨g1, g2, g3⟩ := ihr got' at' hw
                refine ⟨by omega, g2, ?_⟩
                have hsplit : at' - off = k + (at' - (off + k)) := by omega
                rw [g3, hsplit, List.take_add, List.drop_drop]
            | outOfFuel =>
                exact ⟨fun got h => (by cases h), fun got at_ h => (by cases h)⟩
          · rw [ite_eq_right h3]
            refine ⟨fun got h => ?_, fun got at_ h => (by cases h)⟩
            cases h
            apply List.take_of_length_le
            rw [List.length_drop]; omega

/-- With more fuel than items left, the walk ends with a result or a refusal, never out of fuel. -/
theorem walk_not_outOfFuel {α : Type} (fits final : Nat → Nat → Bool) (items : List α) :
    ∀ fuel off, items.length - off < fuel →
      (∃ got, walk fits final items fuel off = .done got) ∨
      (∃ got at_, walk fits final items fuel off = .refused got at_) := by
  intro fuel
  induction fuel with
  | zero => intro off h; omega
  | succ fuel ih =>
      intro off hf
      have hp : page fits final items.length off = pageOf final items.length off
          (keep (fits off) 0 (items.length - off)) := rfl
      generalize keep (fits off) 0 (items.length - off) = k at hp
      unfold walk
      rw [hp]
      unfold pageOf
      by_cases h1 : off < items.length ∧ k = 0
      · rw [ite_eq_left h1]; exact Or.inr ⟨_, _, rfl⟩
      · rw [ite_eq_right h1]
        by_cases h2 : final off k = false
        · rw [ite_eq_left h2]; exact Or.inr ⟨_, _, rfl⟩
        · rw [ite_eq_right h2]
          by_cases h3 : off + k < items.length
          · rw [ite_eq_left h3]
            dsimp only
            have hkpos : 0 < k := by
              rcases Nat.eq_zero_or_pos k with h0 | h0
              · exact absurd ⟨by omega, h0⟩ h1
              · exact h0
            rcases ih (off + k) (by omega) with ⟨got', hw⟩ | ⟨got', at', hw⟩
            · rw [hw]; exact Or.inl ⟨_, rfl⟩
            · rw [hw]; exact Or.inr ⟨_, _, rfl⟩
          · rw [ite_eq_right h3]; exact Or.inl ⟨_, rfl⟩

theorem page_not_refused (fits final : Nat → Nat → Bool) (n off : Nat)
    (hfit : ∀ o, o < n → fits o 1 = true) (hfinal : ∀ o k, final o k = true) :
    page fits final n off ≠ .refused := by
  unfold page pageOf
  have hnz : ¬ (off < n ∧ keep (fits off) 0 (n - off) = 0) := by
    rintro ⟨hlt, h0⟩
    have := keep_pos (fits off) (n - off) (hfit off hlt) (by omega)
    omega
  rw [ite_eq_right hnz, ite_eq_right (by rw [hfinal]; decide)]
  intro h; cases h

theorem walk_never_refused {α : Type} (fits final : Nat → Nat → Bool) (items : List α)
    (hfit : ∀ o, o < items.length → fits o 1 = true) (hfinal : ∀ o k, final o k = true) :
    ∀ fuel off got at_, walk fits final items fuel off ≠ .refused got at_ := by
  intro fuel
  induction fuel with
  | zero => intro off got at_ h; cases h
  | succ fuel ih =>
      intro off got at_ h
      unfold walk at h
      split at h
      · exact page_not_refused fits final items.length off hfit hfinal ‹_›
      · cases h
      · split at h
        · cases h
        · rename_i got' at' hw
          exact ih _ got' at' hw
        · cases h

/-- When every item fits on a page by itself and serialization never overshoots, the walk returns
every item. -/
theorem walk_complete {α : Type} (fits final : Nat → Nat → Bool) (items : List α)
    (hfit : ∀ o, o < items.length → fits o 1 = true) (hfinal : ∀ o k, final o k = true)
    (fuel : Nat) (hf : items.length < fuel) :
    walk fits final items fuel 0 = .done items := by
  rcases walk_not_outOfFuel fits final items fuel 0 (by omega) with ⟨got, hw⟩ | ⟨got, at_, hw⟩
  · rw [hw, (walk_slices fits final items fuel 0 (Nat.zero_le _)).1 got hw]; rfl
  · exact absurd hw (walk_never_refused fits final items hfit hfinal fuel 0 got at_)

end Nami.Storage.Cursor

namespace Nami.Storage
open Cursor

theorem cursorNeverSkipsOrRepeats : Spec.CursorNeverSkipsOrRepeats := by
  intro α fits final items fuel
  obtain ⟨hd, hr⟩ := walk_slices fits final items fuel 0 (Nat.zero_le _)
  refine ⟨fun got h => by simpa using hd got h, fun got at_ h => ?_⟩
  obtain ⟨_, h2, h3⟩ := hr got at_ h
  exact ⟨h2, by simpa using h3⟩

theorem cursorWalkEnds : Spec.CursorWalkEnds := by
  intro α fits final items fuel hf
  exact walk_not_outOfFuel fits final items fuel 0 (by omega)

theorem cursorWalkReturnsEveryItemThatFits : Spec.CursorWalkReturnsEveryItemThatFits := by
  intro α fits final items fuel hf hfit hfinal
  exact walk_complete fits final items hfit hfinal fuel hf

theorem cursorBoundToSnapshotToolAndQuery : Spec.CursorBoundToSnapshotToolAndQuery := by
  intro c snapshot tool queryHash n h
  simp only [accepts, Bool.and_eq_true, beq_iff_eq, decide_eq_true_eq] at h
  exact ⟨h.1.1.1, h.1.1.2, h.1.2, h.2⟩

end Nami.Storage
