/-!
# The .NET engine: the private JSONL protocol

One machine: `src/nami/Hosting/EngineProtocol.cs` (class `EngineProtocol` with its
`PendingCall`) and the process around it in `src/nami/Program.cs`. Members are named instead
of line numbers; `eng/proofs/anchor/cited.json` holds the line of every write site.

## What one step is
* `frame f`: one pass of the inner loop of `ProcessAsync` over one complete frame taken from
  the read buffer, that is `Accept` or a drop. A refusal awaits its `Respond` inside the
  loop, so the next frame is read after the refusal was written; other tasks may run
  meanwhile, which the model allows between any two events.
* `enter`, `respond`, `drop`, `unwindEnter`, `unwind`: the parts of one `Execute` task that
  are separated by awaits: acquiring a worker, `Reply` (which removes the entry from
  `_pending` and then writes the reply), and the `finally` that releases the worker.

## The slots (A1 = H3)
An entry leaves `_pending` at two moments, both before the host can act on the freed slot:
when a cancel frame for it is read (`TryRemove`, then `Cancel`), and when its reply is about
to be written (`Reply`). The task that owned the entry lives on for a while: it is counted in
`endingIdle` (no worker) or `endingBusy` (holds a worker) until its `finally` ran. Those
tasks have no id any more: the host may reuse nothing of them, and `exitProcess` waits for
them (`Task.WhenAll(_tasks)`).

## Abstractions
* Bytes and framing are in `Framing.lean`. Here a frame is already classified: a decodable
  request or cancel with a matching or wrong revision; `undecodable` (JSON or schema failure
  in `Decode` with a readable id of at most 128 characters); `garbage` (the same without
  such an id); `oversize` (longer than `MaxFrameBytesDefault`, discarded up to its newline).
* A tool's work is abstract: a running call may finish with a reply (normal, refusal or
  internal error, all one `Reply`) or, once its token is cancelled, end without one.
* Removing the entry and writing the reply are one step (`respond`): the entry is removed
  first, and the host cannot see the reply before it is written, so every interleaving of the
  code is an interleaving of the model.
* `exit = some true` is a clean exit (code 0); `some false` is an exception leaving
  `RunAsync` (Program.cs) and the process ending with an unhandled exception.
* Startup (`Respond("ready", ...)`) happened before the initial state.

## Not modelled
Write failures on stdout (a host that is gone), the one line on standard error for a dropped
frame, the DuckDB queues behind the tools, the Kestrel listeners that `Program.cs` also
starts, and time.
-/

namespace NamiProofs.HostEngine.Engine

/-- `EngineLimits.MaxPendingDefault` (engine.tsp). -/
def maxPending : Nat := 32
/-- `EngineLimits.WorkersDefault` (engine.tsp). -/
def workers : Nat := 2

inductive Method where
  | call
  | health
  deriving DecidableEq, Repr

/-- A complete frame as the read loop classifies it. -/
inductive Frame where
  /-- A request that decodes; `revOk` is `request.Revision == ContractRevision.Value`. -/
  | req (id : Nat) (m : Method) (revOk : Bool)
  | cancel (id : Nat) (revOk : Bool)
  /-- `JsonDocument.Parse` or `ContractJson.Validate` fails and `RequestId` reads an id. -/
  | undecodable (id : Nat)
  /-- The same failure in a frame that names no id to refuse. -/
  | garbage
  /-- Longer than `MaxFrameBytesDefault`. -/
  | oversize
  deriving DecidableEq, Repr

inductive Phase where
  /-- Waiting for a worker (`_execution.WaitAsync`). -/
  | waiting
  /-- Holding a worker. -/
  | running
  deriving DecidableEq, Repr

/-- One `_pending` entry with its `PendingCall`. -/
structure Call where
  id : Nat
  m : Method
  phase : Phase
  /-- The entry's token is cancelled through `_lifetime` (a cancel frame removes the entry). -/
  cancelled : Bool
  deriving DecidableEq, Repr

/-- Why the read loop is no longer reading. -/
inductive Loop where
  | reading
  /-- End of input with an empty buffer. -/
  | eof
  /-- `ApplicationStopping` cancelled `_lifetime`. -/
  | stopped
  /-- An exception left the loop: end of input inside a frame. -/
  | faulted
  deriving DecidableEq, Repr

structure St where
  loop : Loop := .reading
  /-- `_lifetime` cancelled. -/
  lifetime : Bool := false
  pending : List Call := []
  /-- Workers taken from `_execution`. -/
  busy : Nat := 0
  /-- Tasks whose entry left `_pending` and that have not ended, without a worker. -/
  endingIdle : Nat := 0
  /-- The same, holding a worker. -/
  endingBusy : Nat := 0
  /-- Reply frames written after `ready`, oldest first: (id, is_error). -/
  out : List (Nat × Bool) := []
  /-- `some true`: the process ended with code 0; `some false`: with an unhandled exception. -/
  exit : Option Bool := none
  deriving DecidableEq, Repr

/-- After `Respond("ready", ...)`. -/
def init : St := {}

inductive Ev where
  | frame (f : Frame)
  /-- `read.IsCompleted` with an empty remainder. -/
  | eof
  /-- `read.IsCompleted` with part of a frame left. -/
  | eofPartial
  /-- `ApplicationStopping`, for example SIGTERM (Program.cs). -/
  | stop
  | enter (id : Nat)
  /-- `Reply`: the entry leaves `_pending`, then the reply is written. -/
  | respond (id : Nat) (isError : Bool)
  /-- A call cancelled through `_lifetime` ends without a reply; its `finally` removes it. -/
  | drop (id : Nat)
  /-- A task whose entry a cancel frame removed still wins a worker. -/
  | unwindEnter
  /-- The `finally` of a task whose entry is gone: `worker` says whether it held one. -/
  | unwind (worker : Bool)
  /-- `Task.WhenAll(_tasks)` finished and the process ends (Program.cs). -/
  | exitProcess
  deriving DecidableEq, Repr

def ids (s : St) : List Nat := s.pending.map (·.id)

def findCall (s : St) (id : Nat) : Option Call := s.pending.find? (·.id == id)

/-- Cancel every entry: `_lifetime` is the parent of each `PendingCall` source. -/
def cancelAll (s : St) : St :=
  { s with lifetime := true, pending := s.pending.map (fun c => { c with cancelled := true }) }

/-- An exception leaves the loop; the `finally` cancels `_lifetime`. -/
def fault (s : St) : St := cancelAll { s with loop := .faulted }

def remove (s : St) (id : Nat) : List Call := s.pending.filter (·.id != id)

/-- The read loop answers `id` with an error (`Respond` in `Accept`). -/
def refuse (s : St) (id : Nat) : St := { s with out := s.out ++ [(id, true)] }

/-- One pass of the inner loop over one complete frame. -/
def accept (s : St) : Frame → St
  | .oversize => s
  | .garbage => s
  | .undecodable id => refuse s id
  | .req id _ false => refuse s id
  | .cancel _ false => s
  | .cancel id true =>
    match findCall s id with
    | some c =>
      { s with pending := remove s id,
               endingIdle := if c.phase == .waiting then s.endingIdle + 1 else s.endingIdle,
               endingBusy := if c.phase == .running then s.endingBusy + 1 else s.endingBusy }
    | none => s
  | .req id m true =>
    if s.pending.length ≥ maxPending || (ids s).contains id then refuse s id
    else { s with pending := s.pending ++ [⟨id, m, .waiting, s.lifetime⟩] }

def setPhase (s : St) (id : Nat) (p : Phase) : List Call :=
  s.pending.map (fun c => if c.id == id then { c with phase := p } else c)

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
        some { s with pending := remove s id, out := s.out ++ [(id, isError)],
                      endingBusy := s.endingBusy + 1 }
      else none
    | none => none
  | .drop id =>
    match findCall s id with
    | some c =>
      if c.cancelled && c.phase == .waiting then some { s with pending := remove s id }
      else if c.cancelled && c.phase == .running then some { s with pending := remove s id, busy := s.busy - 1 }
      else none
    | none => none
  | .unwindEnter =>
    if s.endingIdle > 0 && s.busy < workers then
      some { s with endingIdle := s.endingIdle - 1, endingBusy := s.endingBusy + 1, busy := s.busy + 1 }
    else none
  | .unwind worker =>
    if worker then
      if s.endingBusy > 0 then some { s with endingBusy := s.endingBusy - 1, busy := s.busy - 1 } else none
    else
      if s.endingIdle > 0 then some { s with endingIdle := s.endingIdle - 1 } else none
  | .exitProcess =>
    if s.loop != .reading && s.pending.isEmpty && s.endingIdle == 0 && s.endingBusy == 0 && s.exit.isNone then
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
