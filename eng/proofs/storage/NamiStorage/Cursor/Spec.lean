import NamiStorage.Cursor.Model
/-
Claims about paging by `next_cursor`, from the contract:
* `contracts/common/types.tsp:186-190` `Cursor`: "Pass the `next_cursor` of one page to receive the next
  page of the same query."
* `contracts/common/telemetry-query.tsp` `Freshness.truncated` and `ToolReadInput.cursor` ("keep all
  other arguments unchanged").
* PRD T4: "`truncated: true` und `next_cursor` setzen fort"; the test name
  `Cursor_PagesCoverAllRowsWithoutDuplicates` (docs/verification.md:53).
Editing this file reopens review.
-/
namespace Nami.Storage.Spec
open Nami.Storage.Cursor

/-- Following `next_cursor` from the first page never skips and never repeats an item: a walk that
ends returns exactly the items, and a walk that is refused returned exactly the items before the
refused page. -/
def CursorNeverSkipsOrRepeats : Prop :=
  ∀ (α : Type) (fits final : Nat → Nat → Bool) (items : List α) (fuel : Nat),
    (∀ got, walk fits final items fuel 0 = .done got → got = items) ∧
    (∀ got at_, walk fits final items fuel 0 = .refused got at_ →
      at_ ≤ items.length ∧ got = items.take at_)

/-- A walk with more cursor calls than items ends: every page before the last moves forward. -/
def CursorWalkEnds : Prop :=
  ∀ (α : Type) (fits final : Nat → Nat → Bool) (items : List α) (fuel : Nat), items.length < fuel →
    (∃ got, walk fits final items fuel 0 = .done got) ∨
    (∃ got at_, walk fits final items fuel 0 = .refused got at_)

/-- When every item fits a page by itself, following the cursors returns every item. -/
def CursorWalkReturnsEveryItemThatFits : Prop :=
  ∀ (α : Type) (fits final : Nat → Nat → Bool) (items : List α) (fuel : Nat), items.length < fuel →
    (∀ o, o < items.length → fits o 1 = true) → (∀ o k, final o k = true) →
    walk fits final items fuel 0 = .done items

/-- A cursor is accepted only for its own snapshot, tool and query, at an offset within the items. -/
def CursorBoundToSnapshotToolAndQuery : Prop :=
  ∀ (c : Cursor) (snapshot tool queryHash : String) (n : Nat), accepts c snapshot tool queryHash n = true →
    c.snapshot = snapshot ∧ c.tool = tool ∧ c.queryHash = queryHash ∧ c.offset ≤ n

end Nami.Storage.Spec
