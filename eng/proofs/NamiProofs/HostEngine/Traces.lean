import NamiProofs.HostEngine.Host
import NamiProofs.HostEngine.Engine
import NamiProofs.HostEngine.System

/-!
# Real runs replayed through the models

Each trace is the event sequence of one real run, written from what the run printed, and the
observation the run made. A trace is accepted when every event is enabled in turn (`step`
returns `some`) and the model's final state shows the same observation. All runs were made
in this container on 15 Nov 2026 against the code at 843383e (the product files are
unchanged on this branch):

* Host runs: `bun tests/Nami.IntegrationTests/Fixtures/mcp-supervisor.ts <scenario> <dir>`,
  the scripts `McpSupervisorTests` runs, over the fixture engine `mcp-engine.ts`.
* Engine runs: the published NativeAOT engine `artifacts/publish/nami --engine`, driven over
  its standard input and output by a scratch probe that writes the same frames as
  `EnginePipeTests`; and the engine auditor's run of `audit/engine_pending_race.py` (evidence
  `audit-out/engine-pending-race*.txt` in the audit-engine worktree).

Mapping: `/health` probes that fail to connect are not host events; the engine's ids are
renumbered from 1 in the order the engine received them; `mcp-engine.ts` ends on end of input
unless its mode ignores it.

Transitions the accepted traces exercise: host 13 of 16 events (not `stdinError`,
`startupTimeout`, `externalKill`); engine 7 of 9 (not `eofPartial`, `stop`); system all 5.
Line coverage of the runs was not measured.

One trace was first rejected: `busy` ended with exit code 1 after SIGTERM where the model
predicted 0. Bun ends a process with code 1 once a `fetch` handler has thrown, even after a
clean shutdown; `St.threw` models that now, and the trace is accepted.
-/

namespace NamiProofs.HostEngine.Traces

open NamiProofs.HostEngine

/-! ## Host (fixture engine) -/

/-- What a host run shows: how each caller ended, the exit, the child, and the signals sent. -/
def hostObs (s : Host.St) : List Host.Outcome × Option Host.Exit × Host.Child × Bool × Bool :=
  (s.done.map (·.outcome), s.exit, s.child, s.sigterm, s.sigkill)

/-- `gone` (P04): healthBefore 200, engineFailed true, healthStatus 500, hostExit "alive",
childGone true. The engine fails with a line that is not JSON and ignores SIGTERM; the host
SIGKILLs it at 8 s and keeps serving. -/
def gone : List Host.Ev :=
  [.readyFrame, .requestHealth, .reply 1 false, .requestHealth, .reply 2 false, .badFrame false,
   .requestHealth, .gracefulFires, .forcedFires, .childExit, .childClose]

theorem gone_accepted :
    (Host.run Host.init gone).map hostObs =
      some ([.http 200, .http 200, .http 500], none, .exited, true, true) := by decide

/-- `unhealthy`: healthStatus 503, exitCodeAfterSignal 0. -/
def unhealthy : List Host.Ev :=
  [.readyFrame, .requestHealth, .reply 1 true, .requestHealth, .reply 2 true, .hostSignal,
   .transportStopped, .childExit, .childClose, .loopEmpty]

theorem unhealthy_accepted :
    (Host.run Host.init unhealthy).map hostObs =
      some ([.http 503, .http 503], some (.code 0), .exited, false, false) := by decide

/-- `busy`: allWaiting true, healthStatus 500, callsSucceeded 32, exitCodeAfterSignal 1. -/
def busy : List Host.Ev :=
  [.readyFrame, .requestHealth, .reply 1 false] ++ List.replicate 32 (.requestTool true) ++
  [.requestHealth] ++ (List.range 32).map (fun i => .reply (i + 2) false) ++
  [.hostSignal, .transportStopped, .childExit, .childClose, .loopEmpty]

theorem busy_accepted :
    (Host.run Host.init busy).map (fun s =>
      ((s.done.filter (·.via == .health)).map (·.outcome),
       (s.done.filter (fun d => d.via == .tool && d.outcome == .result false)).length, s.exit)) =
      some ([.http 200, .http 500], 32, some (.code 1)) := by decide

/-- `cancel` (P14): allWaiting true, abortedCaller AbortError, cancelForwarded true with
cancelledId 33, nextForwarded true, nextIsError false, othersSucceeded 31,
exitCodeAfterSignal 0. The fixture answers every held id on release, the cancelled one too. -/
def cancel : List Host.Ev :=
  [.readyFrame, .requestHealth, .reply 1 false] ++ List.replicate 32 (.requestTool true) ++
  [.abort 33, .requestTool true] ++ (List.range 33).map (fun i => .reply (i + 2) false) ++
  [.hostSignal, .transportStopped, .childExit, .childClose, .loopEmpty]

theorem cancel_accepted :
    (Host.run Host.init cancel).map (fun s =>
      ((s.sent.filter (fun f => match f with | .cancel _ => true | _ => false)),
       s.sent.contains (.call 34),
       (s.done.filter (fun d => d.via == .tool && d.outcome == .result false)).length,
       (s.done.filter (·.outcome == .cancelled)).length, s.exit)) =
      some ([.cancel 33], true, 32, 1, some (.code 0)) := by decide

/-- `failure` (existing): termReceived true, childRunningAfterClose false. -/
def failure : List Host.Ev :=
  [.readyFrame, .requestTool true, .badFrame false, .gracefulFires, .forcedFires, .childExit, .childClose]

theorem failure_accepted :
    (Host.run Host.init failure).map hostObs =
      some ([.unavailable], none, .exited, true, true) := by decide

/-- `http` (existing): healthStatus 200, toolStatus 200 with the snapshot, exit code 0 after
SIGTERM, child not running. -/
def http : List Host.Ev :=
  [.readyFrame, .requestHealth, .reply 1 false, .requestHealth, .reply 2 false, .requestTool true,
   .reply 3 false, .hostSignal, .transportStopped, .childExit, .childClose, .loopEmpty]

theorem http_accepted :
    (Host.run Host.init http).map hostObs =
      some ([.http 200, .http 200, .result false], some (.code 0), .exited, false, false) := by decide

/-- `stdio` (existing): exit code 0 on end of input, child not running. -/
def stdio : List Host.Ev :=
  [.readyFrame, .hostSignal, .transportStopped, .childExit, .childClose, .loopEmpty]

theorem stdio_accepted :
    (Host.run Host.init stdio).map hostObs = some ([], some (.code 0), .exited, false, false) := by decide

/-! ## Engine (published NativeAOT engine) -/

def engineObs (s : Engine.St) : Engine.Loop × List (Nat × Bool) × Option Bool :=
  (s.loop, s.out, s.exit)

/-- One write: a call, `not-json`, a call. Run: no reply, exit -6 (SIGABRT). -/
def malformed : List Engine.Ev :=
  [.frame (.req 1 .call true), .enter 1, .frame .garbage, .drop 1, .exitProcess]

theorem malformed_accepted :
    (Engine.run Engine.init malformed).map engineObs = some (.faulted, [], some false) := by decide

/-- One write: a call with revision `sha256:0`, a call. Run: no reply, exit -6. -/
def wrongRevision : List Engine.Ev := [.frame (.req 1 .call false), .exitProcess]

theorem wrongRevision_accepted :
    (Engine.run Engine.init wrongRevision).map engineObs = some (.faulted, [], some false) := by decide

/-- One write: call 1, call 1, call 2. Run: no reply, exit -6. -/
def duplicate : List Engine.Ev :=
  [.frame (.req 1 .call true), .enter 1, .frame (.req 1 .call true), .drop 1, .exitProcess]

theorem duplicate_accepted :
    (Engine.run Engine.init duplicate).map engineObs = some (.faulted, [], some false) := by decide

/-- Frames cut across writes and inside a two-byte character (a, b, c, d renumbered 1 to 4).
Run: four replies, b an `invalid_argument` refusal of its cursor; exit 0 at end of input. -/
def fragments : List Engine.Ev :=
  [.frame (.req 1 .health true), .enter 1, .respond 1 false, .release 1,
   .frame (.req 2 .call true), .enter 2, .respond 2 true, .release 2,
   .frame (.req 3 .call true), .enter 3, .respond 3 false, .release 3,
   .frame (.req 4 .health true), .enter 4, .respond 4 false, .release 4, .eof, .exitProcess]

theorem fragments_accepted :
    (Engine.run Engine.init fragments).map engineObs =
      some (.eof, [(1, false), (2, true), (3, false), (4, false)], some true) := by decide

/-! ## Host and engine (published engine; frames as engine.ts writes them) -/

def systemObs (s : System.St) : Engine.Loop × Nat × Nat :=
  (s.eng.loop, s.host.pending.length, (s.eng.out.filter (·.1 ≥ 3)).length)

/-- Own probe, 3 of 3 runs: call 1 (`traces_get` over a 20 000-span trace) and call 2; call 2
answers while call 1 still runs; then the cancel of 1 and 32 new calls in one write. Run:
`InvalidDataException: Duplicate or excessive pending engine requests.`, exit -6, 0 of the
32 new calls answered. -/
def cancelThenNewCalls : List System.Ev :=
  [.host (.requestTool true), .host (.requestTool true), .deliver, .deliver, .eng (.enter 1),
   .eng (.enter 2), .eng (.respond 2 false), .eng (.release 2), .receive, .host (.abort 1)] ++
  List.replicate 32 (.host (.requestTool true)) ++ List.replicate 33 .deliver

set_option maxRecDepth 20000 in
theorem cancelThenNewCalls_accepted :
    (System.run System.init cancelThenNewCalls).map systemObs = some (.faulted, 32, 0) := by decide

/-- The engine auditor's run (2 of 2 at 15:03 and 15:04, and once on its own debug build):
2 running calls, 30 waiting, 32 cancels, 32 new calls. Run: the same exception, exit -6, 0 of
32 answered. The 30 waiting calls unwind (`drop`) before the new calls arrive in this
rendering; without those drops the fault comes earlier, at the first new call. -/
def auditorRace : List System.Ev :=
  List.replicate 32 (.host (.requestTool true)) ++ List.replicate 32 .deliver ++
  [.eng (.enter 1), .eng (.enter 2)] ++ (List.range 32).map (fun i => .host (.abort (i + 1))) ++
  List.replicate 32 .deliver ++ (List.range 30).map (fun i => .eng (.drop (i + 3))) ++
  List.replicate 32 (.host (.requestTool true)) ++ List.replicate 31 .deliver

set_option maxRecDepth 20000 in
theorem auditorRace_accepted :
    (System.run System.init auditorRace).map systemObs = some (.faulted, 32, 0) := by decide

/-- `decode_probe.py deep` and `surrogate`: one call frame as engine.ts writes it for
arguments nested 70 deep, and for a lone surrogate in `traceparent`. Run: no reply, exit -6
(`JsonReaderException` depth 64; `InvalidOperationException` missing low surrogate). -/
def undecodable : List System.Ev := [.host (.requestTool true), .deliverUndecodable]

theorem undecodable_accepted :
    (System.run System.init undecodable).map systemObs = some (.faulted, 1, 0) := by decide

end NamiProofs.HostEngine.Traces
