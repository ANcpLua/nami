import NamiStorage.Cursor.Proofs
/-
Deliberately broken cursor walks: the claims of `Cursor/Spec.lean` tell them apart from `walk`.
-/
namespace Nami.Storage.Cursor.Broken
open Nami.Storage.Cursor

/-- A walk whose `next_cursor` points one item too far (`EncodeCursor(..., i + 1, ...)` after removing
item `i`, instead of `i`). -/
def walkSkipping {α : Type} (fits final : Nat → Nat → Bool) (items : List α) : Nat → Nat → Walk α
  | 0, _ => .outOfFuel
  | fuel + 1, off =>
      match page fits final items.length off with
      | .refused => .refused [] off
      | .page k none => .done ((items.drop off).take k)
      | .page k (some off') =>
          match walkSkipping fits final items fuel (off' + 1) with
          | .done got => .done ((items.drop off).take k ++ got)
          | .refused got at_ => .refused ((items.drop off).take k ++ got) at_
          | .outOfFuel => .outOfFuel

/-- A walk whose `next_cursor` points one item back. -/
def walkRepeating {α : Type} (fits final : Nat → Nat → Bool) (items : List α) : Nat → Nat → Walk α
  | 0, _ => .outOfFuel
  | fuel + 1, off =>
      match page fits final items.length off with
      | .refused => .refused [] off
      | .page k none => .done ((items.drop off).take k)
      | .page k (some off') =>
          match walkRepeating fits final items fuel (off' - 1) with
          | .done got => .done ((items.drop off).take k ++ got)
          | .refused got at_ => .refused ((items.drop off).take k ++ got) at_
          | .outOfFuel => .outOfFuel

/-- Pages of one item each. -/
def oneEach : Nat → Nat → Bool := fun _ count => decide (count ≤ 1)
/-- Pages of two items each. -/
def twoEach : Nat → Nat → Bool := fun _ count => decide (count ≤ 2)
def always : Nat → Nat → Bool := fun _ _ => true

theorem walk_three : walk oneEach always [1, 2, 3] 4 0 = .done [1, 2, 3] := rfl
theorem skipping_skips : walkSkipping oneEach always [1, 2, 3] 4 0 = .done [1, 3] := rfl
theorem repeating_repeats : walkRepeating twoEach always [1, 2, 3] 4 0 = .done [1, 2, 2, 3] := rfl

/-- The claim of `Spec.CursorNeverSkipsOrRepeats`, stated for any walk function. -/
def NeverSkipsOrRepeats (w : (Nat → Nat → Bool) → (Nat → Nat → Bool) → List Nat → Nat → Nat → Walk Nat) : Prop :=
  ∀ (fits final : Nat → Nat → Bool) (items : List Nat) (fuel : Nat) got,
    w fits final items fuel 0 = .done got → got = items

theorem walk_satisfies : NeverSkipsOrRepeats walk := by
  intro fits final items fuel got h
  exact (Nami.Storage.cursorNeverSkipsOrRepeats Nat fits final items fuel).1 got h

theorem breaks_skipping : ¬ NeverSkipsOrRepeats walkSkipping := by
  intro h
  have := h oneEach always [1, 2, 3] 4 [1, 3] skipping_skips
  revert this; decide

theorem breaks_repeating : ¬ NeverSkipsOrRepeats walkRepeating := by
  intro h
  have := h twoEach always [1, 2, 3] 4 [1, 2, 2, 3] repeating_repeats
  revert this; decide

end Nami.Storage.Cursor.Broken
