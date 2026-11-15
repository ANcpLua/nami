/-!
# Framing: newline-delimited frames from reads of any size

Both readers keep the unconsumed bytes of the previous read, append the new read, take
complete frames up to each newline, and keep the rest. They differ in what a frame longer
than the limit does:

* host side, engine.ts (the `data` handler): `Buffer.concat`, `indexOf(10)`, `subarray`; a
  frame or a rest longer than the limit is a fault, and the host fails its engine. `onChunk`
  is that loop for one read (`none` is the fault).
* engine side, EngineProtocol.cs (`ProcessAsync`): `ReadAsync` returns the unconsumed bytes
  plus new ones (`AdvanceTo(buffer.Start, buffer.End)`), `PositionOf('\n')` finds the next
  frame; a frame longer than the limit is dropped, and once the kept rest is longer than the
  limit its bytes are discarded as they arrive, up to the next newline (`skipping`).
  `onChunkSkip` is that loop for one read.

The claims (`Spec.ReadsKeepFrameIdentity`, `Spec.EngineReadsKeepFrameIdentity`) are that the
result does not depend on how the bytes were cut into reads: fragmented and coalesced reads
give the same frames (and for the host the same fault) as one read of the whole stream; and
(`Spec.EngineReaderKeepsAtMostTheLimit`) that the engine's reader never keeps more bytes
than one frame may have.

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

/-! ## The engine's reader: a frame over the limit is skipped, not a fault -/

/-- What the engine's reader has handed on, the bytes it keeps, and whether it is inside a
frame that is longer than the limit (`skipping`). -/
structure SkipReader where
  done : List (List Nat)
  buffer : List Nat
  skipping : Bool
  deriving DecidableEq, Repr

def SkipReader.empty : SkipReader := ⟨[], [], false⟩

/-- The inner loop of `ProcessAsync` over one buffer: the frames within the limit, the kept
rest, and whether the reader is now inside a frame it skips. A frame that ends while
`skipping` is the tail of a skipped frame. -/
def drainSkip (lim : Nat) (skipping : Bool) (buf : List Nat) : List (List Nat) × List Nat × Bool :=
  match h : splitAtNL buf with
  | none => if skipping || buf.length > lim then ([], [], true) else ([], buf, false)
  | some (f, r) =>
    let rest := drainSkip lim false r
    if skipping || f.length > lim then rest else (f :: rest.1, rest.2)
termination_by buf.length
decreasing_by exact splitAtNL_length h

/-- One read of the engine: append it to the kept bytes and drain. -/
def onChunkSkip (lim : Nat) (rd : SkipReader) (data : List Nat) : SkipReader :=
  let p := drainSkip lim rd.skipping (rd.buffer ++ data)
  ⟨rd.done ++ p.1, p.2.1, p.2.2⟩

/-- All reads in order, from an empty reader, with a given read function. -/
def readAllSkipBy (oc : Nat → SkipReader → List Nat → SkipReader) (lim : Nat) (cs : List (List Nat)) :
    SkipReader :=
  cs.foldl (oc lim) SkipReader.empty

/-- The meaning of a stream for the engine, one byte at a time: a frame ends at each
newline; a frame longer than the limit is given up as soon as it is too long, and nothing of
it is handed on or kept. -/
def skipByte (lim : Nat) (rd : SkipReader) (b : Nat) : SkipReader :=
  if b = NL then
    if rd.skipping then ⟨rd.done, [], false⟩ else ⟨rd.done ++ [rd.buffer], [], false⟩
  else if rd.skipping then rd
  else if rd.buffer.length ≥ lim then ⟨rd.done, [], true⟩
  else ⟨rd.done, rd.buffer ++ [b], false⟩

end NamiProofs.HostEngine.Framing
