/-!
# The Bun host: engine supervisor and request handling

One machine: `src/nami.mcp/src/engine.ts` (class `Engine`) together with the parts of
`src/nami.mcp/src/main.ts` and `src/nami.mcp/src/server.ts` that decide how a waiting
caller ends and when the host process ends. Line numbers in the comments refer to commit
843383e; `eng/proofs/anchor/cited.json` holds the current line of every write site.

## What one step is
Each event is one JavaScript task: a stdout `data` chunk's frame, a child-process event, a
timer, a signal handler, or one caller entering `Engine.request`. Bun runs one task at a
time, so a step runs without interleaving up to its first `await`; the model ends each step
there. The asynchronous tails are separate events: `childClose` resolves `exited`
(engine.ts, constructor and `stopChild`), `transportStopped` resolves `closeTransport`
(main.ts, `close`), and `failureExit` is the continuation of `engine.failed` (main.ts).

## Abstractions
* Frame content is abstract: a reply frame is either valid for its id (`reply`) or one of
  the faults that `fail` the engine (`badFrame`; engine.ts:50-53, 57-58). Reply payloads
  are assumed to satisfy the tool's declared schema (engine.ts:94-96 not modelled).
* `child` is the operating-system truth about the engine process; `childExit` can happen at
  any time while it runs (crash, signal, end of input). Delivering a signal is recorded in
  `sigterm`/`sigkill`; the engine model says what the real engine does with them.
* Clients are abstract: `request*` is a new MCP tool call or `GET /health`, `abort` is the
  caller's `AbortSignal` (MCP cancel, HTTP disconnect). Host-name allowlists, credentials
  and the MCP SDK's own validation are not modelled.
* Bun runtime facts the model relies on, each checked on Bun 1.4.4 in this container:
  an uncaught error at top level ends the process with code 1 and runs `exit` listeners;
  `process.exit` runs them too; a child process with open pipes keeps the event loop alive
  until it closes; a signal with a registered handler does not end the process.
* `GET /health` no longer throws: `health` in main.ts catches every rejection and answers
  503 with a report of its own, so no `fetch` handler of the model throws.

## Not modelled
Time (the 5 s and 8 s timers are events without durations), the MCP SDK's own request
handling beyond the abort signal, stdio-mode details beyond "end of stdin starts close"
(main.ts:80-83), and configuration errors after readiness (main.ts:19-54).
-/

namespace NamiProofs.HostEngine.Host

/-- `ENGINE_LIMITS.max_pending` (manifest.ts:81, contracts/models/engine.tsp:31). -/
def maxPending : Nat := 32

/-- Who waits on an engine request. -/
inductive Via where
  /-- An MCP `tools/call`; server.ts:39-58 maps every failure to a typed tool error. -/
  | tool
  /-- `GET /health`; `health` in main.ts awaits the engine and catches every rejection. -/
  | health
  deriving DecidableEq, Repr

/-- The error value a waiting request is rejected with. -/
inductive Err where
  /-- `EngineError("collector_unavailable")` (engine.ts:68, 104). -/
  | unavailable
  /-- `EngineError("collector_contract_mismatch")` (engine.ts:53). -/
  | mismatch
  /-- `EngineError("invalid_argument")` (engine.ts:139-140). -/
  | invalidArgument
  /-- Any other `Error`: an oversized or unparsable reply line, "Engine exited", the startup
  timeout and stream errors. -/
  | plain
  deriving DecidableEq, Repr

/-- How a waiting caller ended, as the client sees it. -/
inductive Outcome where
  /-- The engine's reply was delivered (`isError` is the engine's `is_error`). -/
  | result (isError : Bool)
  /-- Typed `collector_unavailable`, `retryable: true` (server.ts:53-56). -/
  | unavailable
  /-- Typed `collector_contract_mismatch`, `retryable: false`. -/
  | mismatch
  /-- Typed `invalid_argument`. -/
  | invalidArgument
  /-- The caller aborted; nothing is delivered to it (server.ts:52 rethrows the abort). -/
  | cancelled
  /-- An HTTP status for `GET /health`. -/
  | http (status : Nat)
  deriving DecidableEq, Repr

/-- server.ts (`execute`, catch) for tools; for `/health` the catch in `health` (main.ts)
answers 503 with a report of the host's own. -/
def outcomeOf : Via → Err → Outcome
  | .tool, .unavailable => .unavailable
  | .tool, .mismatch => .mismatch
  | .tool, .invalidArgument => .invalidArgument
  | .tool, .plain => .unavailable
  | .health, _ => .http 503

/-- engine.ts:55 resolves; server.ts:50 for tools, main.ts:61 for `/health`. -/
def replyOutcome : Via → Bool → Outcome
  | .tool, isError => .result isError
  | .health, isError => .http (if isError then 503 else 200)

/-- A frame the host writes to the engine's stdin (engine.ts:72, 80). -/
inductive Frame where
  | call (id : Nat)
  | health (id : Nat)
  | cancel (id : Nat)
  deriving DecidableEq, Repr

def frameOf : Via → Nat → Frame
  | .tool, id => .call id
  | .health, id => .health id

/-- One entry of `Engine.pending` (engine.ts:22), apart from the startup entry `"ready"`,
which the model keeps as `ready`/`readyFailed`. `ticket` names the waiting caller. -/
structure Pending where
  id : Nat
  ticket : Nat
  via : Via
  deriving DecidableEq, Repr

/-- A caller that has ended, with its outcome. -/
structure Done where
  ticket : Nat
  via : Via
  outcome : Outcome
  deriving DecidableEq, Repr

inductive Child where
  | running
  | exited
  deriving DecidableEq, Repr

inductive Exit where
  /-- The process ended by itself with this code; `exit` listeners ran. -/
  | code (n : Nat)
  /-- Killed from outside (SIGKILL); no listener ran. -/
  | killed
  deriving DecidableEq, Repr

structure St where
  /-- The `"ready"` entry was resolved (engine.ts:39, 55). -/
  ready : Bool := false
  /-- The `"ready"` entry was rejected (`rejectPending` before the ready frame). -/
  readyFailed : Bool := false
  /-- engine.ts:24. -/
  stopped : Bool := false
  /-- engine.ts:21 `writesClosed` aborted. -/
  writesClosed : Bool := false
  /-- engine.ts:26 `shutdown` assigned (close ran). -/
  shutdown : Bool := false
  /-- engine.ts:111 `child.stdin.end()` was called. -/
  stdinEnded : Bool := false
  /-- engine.ts:25 (set on the child's `close`, line 35). -/
  hasExited : Bool := false
  /-- engine.ts:112 timer armed and not cleared. -/
  graceful : Bool := false
  /-- engine.ts:113 timer armed and not cleared. -/
  forced : Bool := false
  /-- engine.ts:33 `exit` listener still registered (removed at line 36). -/
  hookArmed : Bool := true
  /-- engine.ts:23. -/
  seq : Nat := 0
  /-- Names callers; not program state. -/
  nextTicket : Nat := 0
  pending : List Pending := []
  done : List Done := []
  /-- Every frame written to the child's stdin, oldest first. -/
  sent : List Frame := []
  child : Child := .running
  sigterm : Bool := false
  sigkill : Bool := false
  /-- main.ts:71. -/
  closing : Bool := false
  /-- The transport accepts requests: from readiness (main.ts:17, 37-70) until close begins. -/
  serving : Bool := false
  /-- `closeTransport()` resolved (main.ts:75). -/
  transportDone : Bool := false
  /-- The engine failed by itself (`fail` ran before any `close`): `engine.failed` resolves
  once the child has closed (engine.ts, `fail`). -/
  failed : Bool := false
  exit : Option Exit := none
  deriving DecidableEq, Repr

def init : St := {}

inductive Ev where
  /-- The engine's ready frame arrives (engine.ts:53-55 with id `"ready"`). -/
  | readyFrame
  /-- An MCP tool call enters `Engine.request` (server.ts:46-48 → engine.ts:64-88);
  `fits` is false when the encoded frame exceeds 1 MiB (engine.ts:139). -/
  | requestTool (fits : Bool)
  /-- `GET /health` enters `Engine.request` (main.ts:59-60). -/
  | requestHealth
  /-- A valid reply frame for `id` arrives (engine.ts:53-55). -/
  | reply (id : Nat) (isError : Bool)
  /-- An invalid reply frame arrives: `typed` for a wrong revision or shape
  (engine.ts:53), otherwise an oversized or unparsable line (engine.ts:50, 51, 57). -/
  | badFrame (typed : Bool)
  /-- The caller's `AbortSignal` fires (engine.ts:76-81). -/
  | abort (id : Nat)
  /-- engine.ts:43. -/
  | stdinError
  /-- engine.ts:40-41. -/
  | startupTimeout
  /-- The engine process ends; the host sees `exit` (engine.ts:61). -/
  | childExit
  /-- The child's `close` (engine.ts:34-38): `exited` resolves. -/
  | childClose
  /-- engine.ts:112. -/
  | gracefulFires
  /-- engine.ts:113. -/
  | forcedFires
  /-- SIGTERM or SIGINT (main.ts, `stop`), or end of stdin in stdio mode. Delivered again
  while closing, the handler ends the process with `process.exit(1)`. -/
  | hostSignal
  /-- `closeTransport()` resolves (main.ts:39, 69, 75). -/
  | transportStopped
  /-- Nothing keeps Bun's event loop alive: the process ends with `process.exitCode`, which
  is 1 once the engine failed, else 0. -/
  | loopEmpty
  /-- The continuation of `engine.failed` (main.ts): the engine failed by itself and its
  process has closed; the host stops its transport and calls `process.exit(1)`. -/
  | failureExit
  /-- SIGKILL from outside. -/
  | externalKill
  deriving DecidableEq, Repr

/-- Events no client, operator or platform sends: the engine, its pipes, timers and the
runtime. -/
def Ev.internal : Ev → Bool
  | .requestTool _ | .requestHealth | .abort _ | .hostSignal | .externalKill => false
  | _ => true

/-- engine.ts, `rejectPending`. -/
def rejectPending (s : St) (e : Err) : St :=
  { s with
    done := s.done ++ s.pending.map (fun p => ⟨p.ticket, p.via, outcomeOf p.via e⟩)
    pending := [] }

/-- The synchronous part of engine.ts:109-114. -/
def stopChild (s : St) : St :=
  if s.hasExited then s else { s with stdinEnded := true, graceful := true, forced := true }

/-- engine.ts:100-107. -/
def close (s : St) : St :=
  if s.shutdown then s
  else stopChild { rejectPending { s with stopped := true, writesClosed := true } .unavailable with
    shutdown := true }

/-- The process ends with a code; the `exit` listener (engine.ts:32-33) sends SIGKILL. -/
def exitWith (s : St) (n : Nat) : St :=
  { s with exit := some (.code n), sigkill := s.sigkill || (s.hookArmed && s.child == .running) }

/-- engine.ts, `fail`. Before readiness the rejected `ready` promise ends the host with an
uncaught error at `await engine.ready` in main.ts (exit code 1). -/
def fail (s : St) (e : Err) : St :=
  if s.stopped then s
  else
    let s1 := rejectPending { s with stopped := true, writesClosed := true, failed := true } e
    let s2 := close { s1 with sigterm := s1.sigterm || s1.child == .running }
    if s.ready then s2 else exitWith { s2 with readyFailed := true } 1

def findPending (s : St) (id : Nat) : Option Pending := s.pending.find? (·.id == id)

def removePending (s : St) (id : Nat) : List Pending := s.pending.filter (·.id != id)

/-- engine.ts:64-88. `t` is the caller's ticket. -/
def request (s : St) (v : Via) (fits : Bool) : St :=
  let t := s.nextTicket
  let s := { s with nextTicket := t + 1 }
  if s.stopped then { s with done := s.done ++ [⟨t, v, outcomeOf v .unavailable⟩] }   -- "Engine has stopped."
  else if s.pending.length ≥ maxPending then
    { s with done := s.done ++ [⟨t, v, outcomeOf v .unavailable⟩] }                   -- "queue is full."
  else
    let id := s.seq + 1
    if !fits then { s with seq := id, done := s.done ++ [⟨t, v, outcomeOf v .invalidArgument⟩] }  -- `encode`
    else { s with seq := id, pending := s.pending ++ [⟨id, t, v⟩], sent := s.sent ++ [frameOf v id] }

def step (s : St) : Ev → Option St
  | .readyFrame =>
    if s.exit.isNone && !s.ready && !s.stopped && !s.hasExited then
      some { s with ready := true, serving := true }
    else none
  | .requestTool fits =>
    if s.exit.isNone && s.serving then some (request s .tool fits) else none
  | .requestHealth =>
    if s.exit.isNone && s.serving then some (request s .health true) else none
  | .reply id isError =>
    if s.exit.isNone && !s.hasExited then
      match findPending s id with
      | some p => some { s with pending := removePending s id,
                                done := s.done ++ [⟨p.ticket, p.via, replyOutcome p.via isError⟩] }
      | none => some s
    else none
  | .badFrame typed =>
    if s.exit.isNone && !s.hasExited then some (fail s (if typed then .mismatch else .plain))
    else none
  | .abort id =>
    if s.exit.isNone then
      match findPending s id with
      | some p => some { s with pending := removePending s id,
                                done := s.done ++ [⟨p.ticket, p.via, .cancelled⟩],
                                sent := if s.writesClosed then s.sent else s.sent ++ [.cancel id] }
      | none => none
    else none
  | .stdinError =>
    if s.exit.isNone && !s.hasExited then some (fail s .plain) else none
  | .startupTimeout =>
    if s.exit.isNone && !s.ready && !s.readyFailed then some (fail s .plain) else none
  | .childExit =>
    if s.child == .running then
      let s' := { s with child := .exited }
      some (if s'.exit.isNone then fail s' .plain else s')
    else none
  | .childClose =>
    if s.exit.isNone && s.child == .exited && !s.hasExited then
      some { s with hasExited := true, graceful := false, forced := false, hookArmed := false }
    else none
  | .gracefulFires =>
    if s.exit.isNone && s.graceful then
      some { s with graceful := false, sigterm := s.sigterm || s.child == .running }
    else none
  | .forcedFires =>
    if s.exit.isNone && s.forced then
      some { s with forced := false, sigkill := s.sigkill || s.child == .running }
    else none
  | .hostSignal =>
    if s.exit.isNone && s.ready then
      -- main.ts, `stop`: the handlers stay registered (`process.on`), so a repeated signal
      -- runs `process.exit(1)` and with it the `exit` listener of engine.ts.
      if s.closing then some (exitWith s 1)
      else some { s with closing := true, serving := false }
    else none
  | .transportStopped =>
    if s.exit.isNone && s.closing && !s.transportDone then some (close { s with transportDone := true })
    else none
  | .loopEmpty =>
    if s.exit.isNone && !s.serving && s.transportDone && s.hasExited then
      some (exitWith s (if s.failed then 1 else 0))
    else none
  | .failureExit =>
    if s.exit.isNone && s.ready && s.failed && s.hasExited then
      some (exitWith { s with closing := true, serving := false, transportDone := true } 1)
    else none
  | .externalKill =>
    if s.exit.isNone then some { s with exit := some .killed } else none

/-- Run events from a state with a given step function; `none` as soon as one is not
enabled. Claims are stated for any step function so that broken variants can be checked
against them (`Mutants.lean`). -/
def runBy (stp : St → Ev → Option St) (s : St) : List Ev → Option St
  | [] => some s
  | e :: es => (stp s e).bind (runBy stp · es)

def ReachableBy (stp : St → Ev → Option St) (s : St) : Prop := ∃ es, runBy stp init es = some s

def run : St → List Ev → Option St := runBy step

def Reachable (s : St) : Prop := ReachableBy step s

/-- A trace is accepted when every event is enabled in turn. -/
def accepts (es : List Ev) : Bool := (run init es).isSome

end NamiProofs.HostEngine.Host
