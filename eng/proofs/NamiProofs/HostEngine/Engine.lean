/-!
# The .NET engine: the private JSONL protocol

One machine: `src/nami/Hosting/EngineProtocol.cs` (class `EngineProtocol` with its
`PendingCall`) and the process around it in `src/nami/Program.cs`. Line numbers refer to
commit 843383e.

## What one step is
* `frame f`: one pass of the inner loop (EngineProtocol.cs:59-85) over one complete frame
  taken from the read buffer. The loop has no `await` between frames, so all frames of one
  read are handled back to back; the model lets other events run between any two frames,
  which includes every interleaving the code allows.
* `enter`, `respond`, `release`, `drop`: the parts of one `Execute` task (106-176) that are
  separated by awaits: acquiring a worker (113), writing the reply (178-199), and the
  `finally` that removes the entry from `_pending` (168-175). Writing the reply and removing
  the entry are separate steps because `_pending.TryRemove` runs only after `Respond`
  returned.

## Abstractions
* Bytes and framing are in `Framing.lean`. Here a frame is already classified: a decodable
  request or cancel with a matching or wrong revision, `garbage` (JSON or schema failure,
  201-208) or `oversize` (62-63, 87-88).
* A tool's work is abstract: a running call may finish with a reply (normal, refusal or
  internal error, all one `Respond`) or, once its token is cancelled, end without one.
* `exit = some true` is a clean exit (code 0); `some false` is an exception leaving
  `RunAsync` (Program.cs:21) and the process ending with an unhandled exception.
* Startup (`Respond("ready", ...)`, line 54) happened before the initial state.

## Not modelled
Write failures on stdout (a host that is gone), the DuckDB queues behind the tools, the
Kestrel listeners that `Program.cs` also starts, and time.
-/

namespace NamiProofs.HostEngine.Engine

/-- `EngineLimits.MaxPendingDefault` (engine.tsp:31). -/
def maxPending : Nat := 32
/-- `EngineLimits.WorkersDefault` (engine.tsp:32). -/
def workers : Nat := 2

inductive Method where
  | call
  | health
  deriving DecidableEq, Repr

/-- A complete frame as the read loop classifies it. -/
inductive Frame where
  /-- A request that decodes; `revOk` is `request.Revision == ContractRevision.Value` (66). -/
  | req (id : Nat) (m : Method) (revOk : Bool)
  | cancel (id : Nat) (revOk : Bool)
  /-- `JsonDocument.Parse` or `ContractJson.Validate` fails (201-208). -/
  | garbage
  /-- Longer than `MaxFrameBytesDefault` (62-63, 87-88). -/
  | oversize
  deriving DecidableEq, Repr

inductive Phase where
  /-- Waiting for a worker (`_execution.WaitAsync`, 113). -/
  | waiting
  /-- Holding a worker. -/
  | running
  /-- Reply written; the `finally` (168-175) has not removed the entry yet. -/
  | replied
  deriving DecidableEq, Repr

/-- One `_pending` entry (24) with its `PendingCall` (228-259). -/
structure Call where
  id : Nat
  m : Method
  phase : Phase
  /-- The entry's token is cancelled: by a cancel frame (70) or through `_lifetime` (231). -/
  cancelled : Bool
  deriving DecidableEq, Repr

/-- Why the read loop is no longer reading. -/
inductive Loop where
  | reading
  /-- End of input with an empty buffer (90-93). -/
  | eof
  /-- `ApplicationStopping` cancelled `_lifetime` (22, 96-98). -/
  | stopped
  /-- An exception left the loop (62-92). -/
  | faulted
  deriving DecidableEq, Repr

structure St where
  loop : Loop := .reading
  /-- `_lifetime` cancelled (22, 101). -/
  lifetime : Bool := false
  pending : List Call := []
  /-- Workers taken from `_execution` (21). -/
  busy : Nat := 0
  /-- Reply frames written after `ready`, oldest first: (id, is_error). -/
  out : List (Nat × Bool) := []
  /-- `some true`: the process ended with code 0; `some false`: with an unhandled exception. -/
  exit : Option Bool := none
  deriving DecidableEq, Repr

/-- After `Respond("ready", ...)` (54). -/
def init : St := {}

inductive Ev where
  | frame (f : Frame)
  /-- `read.IsCompleted` with an empty remainder (90-93). -/
  | eof
  /-- `read.IsCompleted` with part of a frame left (92). -/
  | eofPartial
  /-- `ApplicationStopping`, for example SIGTERM (Program.cs:21). -/
  | stop
  | enter (id : Nat)
  | respond (id : Nat) (isError : Bool)
  | release (id : Nat)
  | drop (id : Nat)
  /-- `Task.WhenAll(_tasks)` finished and the process ends (102, Program.cs:23-26). -/
  | exitProcess
  deriving DecidableEq, Repr

def ids (s : St) : List Nat := s.pending.map (·.id)

def findCall (s : St) (id : Nat) : Option Call := s.pending.find? (·.id == id)

/-- Cancel every entry: `_lifetime` is the parent of each `PendingCall` source (231). -/
def cancelAll (s : St) : St :=
  { s with lifetime := true, pending := s.pending.map (fun c => { c with cancelled := true }) }

/-- An exception leaves the loop; the `finally` cancels `_lifetime` (99-103). -/
def fault (s : St) : St := cancelAll { s with loop := .faulted }

/-- One pass of 59-85 over one complete frame. -/
def accept (s : St) : Frame → St
  | .oversize => fault s
  | .garbage => fault s
  | .req _ _ false => fault s
  | .cancel _ false => fault s
  | .cancel id true =>
    { s with pending := s.pending.map (fun c => if c.id == id then { c with cancelled := true } else c) }
  | .req id m true =>
    if s.pending.length ≥ maxPending || (ids s).contains id then fault s
    else { s with pending := s.pending ++ [⟨id, m, .waiting, s.lifetime⟩] }

def setPhase (s : St) (id : Nat) (p : Phase) : List Call :=
  s.pending.map (fun c => if c.id == id then { c with phase := p } else c)

def remove (s : St) (id : Nat) : List Call := s.pending.filter (·.id != id)

def step (s : St) : Ev → Option St
  | .frame f => if s.loop == .reading then some (accept s f) else none
  | .eof => if s.loop == .reading then some (cancelAll { s with loop := .eof }) else none
  | .eofPartial => if s.loop == .reading then some (fault s) else none
  | .stop =>
    if s.exit.isNone then
      some (cancelAll { s with loop := if s.loop == .reading then .stopped else s.loop })
    else none
  | .enter id =>
    match findCall s id with
    | some c =>
      -- A token cancelled while waiting may still win the race for a released worker,
      -- so entering does not require an uncancelled token (an over-approximation).
      if c.phase == .waiting && s.busy < workers then
        some { s with pending := setPhase s id .running, busy := s.busy + 1 }
      else none
    | none => none
  | .respond id isError =>
    match findCall s id with
    | some c =>
      if c.phase == .running && !s.lifetime then
        some { s with pending := setPhase s id .replied, out := s.out ++ [(id, isError)] }
      else none
    | none => none
  | .release id =>
    match findCall s id with
    | some c => if c.phase == .replied then some { s with pending := remove s id, busy := s.busy - 1 } else none
    | none => none
  | .drop id =>
    match findCall s id with
    | some c =>
      if c.cancelled && c.phase == .waiting then some { s with pending := remove s id }
      else if c.cancelled && c.phase == .running then some { s with pending := remove s id, busy := s.busy - 1 }
      else none
    | none => none
  | .exitProcess =>
    if s.loop != .reading && s.pending.isEmpty && s.exit.isNone then
      some { s with exit := some (s.loop != .faulted) }
    else none

/-- Run events from a state with a given step function; `none` as soon as one is not
enabled. Claims are stated for any step function so that broken variants can be checked
against them (`Mutants.lean`). -/
def runBy (stp : St → Ev → Option St) (s : St) : List Ev → Option St
  | [] => some s
  | e :: es => (stp s e).bind (runBy stp · es)

def ReachableBy (stp : St → Ev → Option St) (s : St) : Prop := ∃ es, runBy stp init es = some s

def run : St → List Ev → Option St := runBy step

def Reachable (s : St) : Prop := ReachableBy step s

def accepts (es : List Ev) : Bool := (run init es).isSome

end NamiProofs.HostEngine.Engine
