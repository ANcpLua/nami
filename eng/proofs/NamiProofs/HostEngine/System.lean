import NamiProofs.HostEngine.Host
import NamiProofs.HostEngine.Engine

/-!
# Host and engine together, joined by the two pipes

The steady state after readiness: callers enter and abort requests on the host
(`Host.step`), the engine's read loop takes frames from the stdin pipe in order
(`Engine.step` on `.frame`), its tasks work and reply (`Engine.step` on `enter`, `respond`,
`drop`, `unwindEnter`, `unwind`), and the host's `data` handler takes reply frames in order
(`Host.step` on `.reply`). Both pipes are FIFO and lose nothing (both readers take complete
frames in order; `Framing.lean` proves that the splitting into frames does not depend on how
the bytes arrive).

Engine death and shutdown are left to the two machines on their own; this composition asks
only what frames a correct host can send. A call frame carries an agent's arguments, so the
model keeps the case that the engine cannot decode one: `deliverUndecodable` delivers the
next call frame as `undecodable` with its id, which the engine answers with an error for
that id (`Decode` in EngineProtocol.cs). Whether the host still writes such a frame is the
host's matter (server.ts: arguments its schema refused, a malformed trace context); the
engine's answer to one is what this event models.
-/

namespace NamiProofs.HostEngine.System

open NamiProofs.HostEngine

structure St where
  host : Host.St
  eng : Engine.St
  /-- Frames of `host.sent` the engine has taken. -/
  delivered : Nat := 0
  /-- Reply frames of `eng.out` the host has taken. -/
  received : Nat := 0
  deriving DecidableEq, Repr

/-- engine.ts:72 and 80 always write the generated `CONTRACT_REVISION`, and `encode`
(engine.ts:137-142) produces one schema-valid line within the frame limit. -/
def toEngine : Host.Frame → Engine.Frame
  | .call id => .req id .call true
  | .health id => .req id .health true
  | .cancel id => .cancel id true

/-- The host right after the ready frame (engine.ts:55, main.ts:17-70). -/
def hostReady : Host.St := { Host.init with ready := true, serving := true }

def init : St := { host := hostReady, eng := Engine.init }

inductive Ev where
  /-- A caller: `requestTool`, `requestHealth` or `abort`. -/
  | host (e : Host.Ev)
  /-- The read loop takes the next frame of the stdin pipe. -/
  | deliver
  /-- An engine task: `enter`, `respond`, `drop`, `unwindEnter` or `unwind`. -/
  | eng (e : Engine.Ev)
  /-- The host's `data` handler takes the next reply frame. -/
  | receive
  /-- The read loop takes the next frame, a call whose content it cannot decode. -/
  | deliverUndecodable
  deriving DecidableEq, Repr

/-- One step of the composition over a given engine step function, so that broken engines can
be checked against the same claims (`Mutants.lean`). -/
def stepBy (estp : Engine.St → Engine.Ev → Option Engine.St) (s : St) : Ev → Option St
  | .host e =>
    match e with
    | .requestTool _ | .requestHealth | .abort _ => (Host.step s.host e).map ({ s with host := · })
    | _ => none
  | .deliver =>
    match s.host.sent[s.delivered]? with
    | some f => (estp s.eng (.frame (toEngine f))).map
        (fun g => { s with eng := g, delivered := s.delivered + 1 })
    | none => none
  | .eng e =>
    match e with
    | .enter _ | .respond _ _ | .drop _ | .unwindEnter | .unwind _ => (estp s.eng e).map ({ s with eng := · })
    | _ => none
  | .deliverUndecodable =>
    match s.host.sent[s.delivered]? with
    | some (.call id) | some (.health id) => (estp s.eng (.frame (.undecodable id))).map
        (fun g => { s with eng := g, delivered := s.delivered + 1 })
    | _ => none
  | .receive =>
    match s.eng.out[s.received]? with
    | some (id, isError) => (Host.step s.host (.reply id isError)).map
        (fun h => { s with host := h, received := s.received + 1 })
    | none => none

def step : St → Ev → Option St := stepBy Engine.step

def runBy (estp : Engine.St → Engine.Ev → Option Engine.St) (s : St) : List Ev → Option St
  | [] => some s
  | e :: es => (stepBy estp s e).bind (runBy estp · es)

def ReachableBy (estp : Engine.St → Engine.Ev → Option Engine.St) (s : St) : Prop :=
  ∃ es, runBy estp init es = some s

def run : St → List Ev → Option St := runBy Engine.step

def Reachable (s : St) : Prop := ReachableBy Engine.step s

def accepts (es : List Ev) : Bool := (run init es).isSome

end NamiProofs.HostEngine.System
