/-
Paging by `next_cursor` over one snapshot.

Code (commit 843383e, `src/nami/Tools/ToolDispatcher.cs`):
* `:171-182` a cursor is accepted only for the same snapshot id, tool name and query hash, with an offset
  between 0 and the number of items;
* `:238-252` the list loop: add item `i`, set `truncated` and `next_cursor` for `i + 1`, keep going while
  the envelope fits `max_bytes`; the first item that does not fit is removed and becomes the next cursor;
* `:254-258` an empty page at an offset before the end is refused;
* `:260-261` the serialized page is checked once more and refused when it is too large;
* `:199-226` the same loop over the spans of `traces_get`.

Abstraction: the envelope's size is an arbitrary predicate `fits off count` ("the page of `count` items
from `off`, with its `truncated` flag and `next_cursor`, fits"), and the final serialization check an
arbitrary predicate `final off count`. After an item is removed (`:247-250`) the envelope is exactly the
one the previous iteration measured (same items, `truncated = true`, `next_cursor` for the removed item),
so the loop keeps `count` items where `count` is the last prefix length that fitted. The item list is
the tool's result over one snapshot: T2 makes it the same list on every call with that snapshot id,
which the queue model's `ReplayReturnsCapturedRead` and the test `AnyTool_AfterRestart_SameAnswer` back.
-/
namespace Nami.Storage.Cursor

/-- How many items the loop keeps, having kept `c` and with `r` items left after them. -/
def keep (fits : Nat → Bool) : Nat → Nat → Nat
  | c, 0 => c
  | c, r + 1 => if fits (c + 1) then keep fits (c + 1) r else c

/-- One page: the items returned and the offset of `next_cursor`, or a refusal. -/
inductive Page where
  | page (count : Nat) (next : Option Nat)
  | refused
  deriving DecidableEq, Repr

/-- The page at offset `off` of `n` items once the loop has kept `k` of them. -/
def pageOf (final : Nat → Nat → Bool) (n off k : Nat) : Page :=
  if off < n ∧ k = 0 then .refused
  else if final off k = false then .refused
  else .page k (if off + k < n then some (off + k) else none)

/-- The page the tool returns at offset `off` of `n` items. -/
def page (fits final : Nat → Nat → Bool) (n off : Nat) : Page :=
  pageOf final n off (keep (fits off) 0 (n - off))

/-- What an agent that follows every `next_cursor` from `off` receives. -/
inductive Walk (α : Type) where
  | done (got : List α)
  | refused (got : List α) (at_ : Nat)
  | outOfFuel
  deriving Repr

def walk {α : Type} (fits final : Nat → Nat → Bool) (items : List α) : Nat → Nat → Walk α
  | 0, _ => .outOfFuel
  | fuel + 1, off =>
      match page fits final items.length off with
      | .refused => .refused [] off
      | .page k none => .done ((items.drop off).take k)
      | .page k (some off') =>
          match walk fits final items fuel off' with
          | .done got => .done ((items.drop off).take k ++ got)
          | .refused got at_ => .refused ((items.drop off).take k ++ got) at_
          | .outOfFuel => .outOfFuel

/-- `ToolDispatcher.cs:176-181`: the parts a cursor carries and what the call checks. -/
structure Cursor where
  snapshot : String
  tool : String
  offset : Nat
  queryHash : String
  deriving DecidableEq, Repr

def accepts (c : Cursor) (snapshot tool queryHash : String) (n : Nat) : Bool :=
  c.snapshot == snapshot && c.tool == tool && c.queryHash == queryHash && decide (c.offset ≤ n)

end Nami.Storage.Cursor
