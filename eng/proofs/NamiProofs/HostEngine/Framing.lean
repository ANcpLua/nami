/-!
# Framing: newline-delimited frames from reads of any size

Both readers keep the unconsumed bytes of the previous read, append the new read, take
complete frames up to each newline, and keep the rest:

* engine side, EngineProtocol.cs:57-89: `ReadAsync` returns the unconsumed bytes plus new
  ones (`AdvanceTo(buffer.Start, buffer.End)`, line 89), `PositionOf('\n')` finds the next
  frame (59), a frame or a rest longer than the limit is a fault (62-63, 87-88);
* host side, engine.ts:44-57: `Buffer.concat`, `indexOf(10)`, `subarray`, the same two
  limit checks (50, 57).

`onChunk` is that loop for one read. The claim (`Spec.ReadsKeepFrameIdentity`) is that the
result does not depend on how the bytes were cut into reads: fragmented and coalesced reads
give the same frames, and the same fault, as one read of the whole stream.

Abstraction: bytes are `Nat`; decoding a frame is not part of framing (it is in
`Engine.lean`). `PipeReader` is assumed to return all unconsumed bytes followed by new ones,
its documented contract.
-/

namespace NamiProofs.HostEngine.Framing

def NL : Nat := 10

/-- What the readers have handed on, and the bytes they keep for the next read. -/
structure Reader where
  done : List (List Nat)
  buffer : List Nat
  deriving DecidableEq, Repr

def Reader.empty : Reader := ⟨[], []⟩

/-- The first newline and the slices around it (`PositionOf`/`Slice`, `indexOf`/`subarray`). -/
def splitAtNL : List Nat → Option (List Nat × List Nat)
  | [] => none
  | b :: bs => if b = NL then some ([], bs) else (splitAtNL bs).map (fun p => (b :: p.1, p.2))

theorem splitAtNL_length : ∀ {buf : List Nat} {f r : List Nat},
    splitAtNL buf = some (f, r) → r.length < buf.length
  | [], _, _, h => by simp [splitAtNL] at h
  | b :: bs, f, r, h => by
    unfold splitAtNL at h
    split at h
    · simp at h; obtain ⟨_, rfl⟩ := h; simp
    · cases hs : splitAtNL bs with
      | none => simp [hs] at h
      | some p =>
        simp [hs] at h
        obtain ⟨_, rfl⟩ := h
        have := splitAtNL_length (buf := bs) (f := p.1) (r := p.2) (by simp [hs])
        simp; omega

/-- The inner loop over one buffer: complete frames, then the rest; `none` is a fault. -/
def drain (lim : Nat) (buf : List Nat) : Option (List (List Nat) × List Nat) :=
  match h : splitAtNL buf with
  | none => if buf.length > lim then none else some ([], buf)
  | some (f, r) =>
    if f.length > lim then none
    else (drain lim r).map (fun p => (f :: p.1, p.2))
termination_by buf.length
decreasing_by exact splitAtNL_length h

/-- One read: append it to the kept bytes and drain (EngineProtocol.cs:57-89,
engine.ts:45-57). -/
def onChunk (lim : Nat) (rd : Reader) (data : List Nat) : Option Reader :=
  (drain lim (rd.buffer ++ data)).map (fun p => ⟨rd.done ++ p.1, p.2⟩)

/-- All reads in order, from an empty reader, with a given read function. -/
def readAllBy (oc : Nat → Reader → List Nat → Option Reader) (lim : Nat) (cs : List (List Nat)) :
    Option Reader :=
  cs.foldlM (oc lim) Reader.empty

/-- The meaning of a stream, one byte at a time: a frame ends at each newline; a frame
longer than the limit is a fault as soon as it is too long. -/
def byteStep (lim : Nat) (rd : Reader) (b : Nat) : Option Reader :=
  if b = NL then some ⟨rd.done ++ [rd.buffer], []⟩
  else if rd.buffer.length ≥ lim then none
  else some ⟨rd.done, rd.buffer ++ [b]⟩

end NamiProofs.HostEngine.Framing
