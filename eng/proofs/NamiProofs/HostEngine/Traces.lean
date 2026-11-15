import NamiProofs.HostEngine.Host
import NamiProofs.HostEngine.Engine
import NamiProofs.HostEngine.System

/-!
# Real runs replayed through the models

Each trace is the event sequence of one real run, written from what the run printed or
asserted, and the observation the run made. A trace is accepted when every event is enabled
in turn (`step` returns `some`) and the model's final state shows the same observation.

* Host runs: `bun tests/Nami.IntegrationTests/Fixtures/mcp-supervisor.ts <scenario> <dir>`,
  the scripts `McpSupervisorTests` runs, over the fixture engine `mcp-engine.ts`; made on
  15 Nov 2026 in this container against the host with P04 in it.
* Engine runs: the tests of `tests/Nami.IntegrationTests/EngineProtocolTests.cs`, which serve
  the real `EngineProtocol` over in-memory pipes and write the frames `engine.ts` writes. Its
  `Slots_*`, `Refusal_*` and `BadFrames_*` tests hold every tool call at the store until the
  test opens it, so the order of the events below is the order the test forced, not one
  possible order. `fragments` and `cancelThenNewCalls` are runs of the published NativeAOT
  engine over its real standard input and output (`tests/Nami.EndToEndTests/EnginePipeTests.cs`).
  The runs of the engine at 843383e, which ended it (exit -6), are in this file's history
  (commit 333fad0) and are what `Mutants.engineBefore` still does.

Mapping: `/health` probes that fail to connect are not host events; the engine's ids are
renumbered from 1 in the order the engine received them; `mcp-engine.ts` ends on end of input
unless its mode ignores it. Where a run does not fix the order of two replies, the trace
picks one.

Transitions the accepted traces exercise: host 14 of 17 events (not `stdinError`,
`startupTimeout`, `externalKill`); engine 8 of 10 (not `eofPartial`, `stop`; `unwindEnter`
and `drop` only where noted); system all 5. Line coverage of the runs was not measured.

The runs of the host at 843383e (`gone` answering 500 and never exiting, `busy` answering
500 and exiting 1 after a clean shutdown, because Bun ends a process with code 1 once a
`fetch` handler has thrown) are in this file's history (commit 333fad0) and are what
`Mutants.hostBefore` still does.
-/

namespace NamiProofs.HostEngine.Traces

open NamiProofs.HostEngine

/-! ## Host (fixture engine) -/

/-- What a host run shows: how each caller ended, the exit, the child, and the signals sent. -/
def hostObs (s : Host.St) : List Host.Outcome × Option Host.Exit × Host.Child × Bool × Bool :=
  (s.done.map (·.outcome), s.exit, s.child, s.sigterm, s.sigkill)

/-- `gone` (P04): healthBefore 200, engineFailed true, healthStatus 503 with report status
`unhealthy`, hostExit 1, childGone true. The engine fails with a line that is not JSON and
ignores SIGTERM; the host SIGKILLs it at 8 s and then ends itself. -/
def gone : List Host.Ev :=
  [.readyFrame, .requestHealth, .reply 1 false, .requestHealth, .reply 2 false, .badFrame false,
   .requestHealth, .gracefulFires, .forcedFires, .childExit, .childClose, .failureExit]

theorem gone_accepted :
    (Host.run Host.init gone).map hostObs =
      some ([.http 200, .http 200, .http 503], some (.code 1), .exited, true, true) := by decide

/-- `twice`: closingBegan true, hostExit 1, hostSignal null, childGone true. The engine
ignores SIGTERM and the end of its input; the second SIGTERM ends the host through
`process.exit`, whose `exit` listener SIGKILLs the engine. -/
def twice : List Host.Ev :=
  [.readyFrame, .requestHealth, .reply 1 false, .hostSignal, .transportStopped, .hostSignal]

theorem twice_accepted :
    (Host.run Host.init twice).map hostObs = some ([.http 200], some (.code 1), .running, false, true) := by
  decide

/-- `unhealthy`: healthStatus 503, exitCodeAfterSignal 0. -/
def unhealthy : List Host.Ev :=
  [.readyFrame, .requestHealth, .reply 1 true, .requestHealth, .reply 2 true, .hostSignal,
   .transportStopped, .childExit, .childClose, .loopEmpty]

theorem unhealthy_accepted :
    (Host.run Host.init unhealthy).map hostObs =
      some ([.http 503, .http 503], some (.code 0), .exited, false, false) := by decide

/-- `busy`: allWaiting true, healthStatus 503 with report status `unhealthy`,
callsSucceeded 32, exitCodeAfterSignal 0. -/
def busy : List Host.Ev :=
  [.readyFrame, .requestHealth, .reply 1 false] ++ List.replicate 32 (.requestTool true) ++
  [.requestHealth] ++ (List.range 32).map (fun i => .reply (i + 2) false) ++
  [.hostSignal, .transportStopped, .childExit, .childClose, .loopEmpty]

theorem busy_accepted :
    (Host.run Host.init busy).map (fun s =>
      ((s.done.filter (·.via == .health)).map (·.outcome),
       (s.done.filter (fun d => d.via == .tool && d.outcome == .result false)).length, s.exit)) =
      some ([.http 200, .http 503], 32, some (.code 0)) := by decide

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

/-! ## Engine -/

def engineObs (s : Engine.St) : Engine.Loop × List (Nat × Bool) × Option Bool :=
  (s.loop, s.out, s.exit)

/-- Serving `id` from a free worker: the call enters, replies, and its task ends. -/
def serve (id : Nat) (isError : Bool := false) : List Engine.Ev :=
  [.enter id, .respond id isError, .unwind true]

/-- `BadFrames_NotJsonWithoutIdAndOversize_DroppedWithOneLineEachAndCallsInFlightFinish`: one
write of a call, `not-json`, `{"id":7}`, a frame of more than 1 MiB, and a call. Run: three
lines on the diagnostics stream, both calls answered, a clean end at end of input. -/
def badFrames : List Engine.Ev :=
  [.frame (.req 1 .call true), .enter 1, .frame .garbage, .frame .garbage, .frame .oversize,
   .frame (.req 2 .call true), .enter 2, .respond 1 false, .respond 2 false, .unwind true, .unwind true,
   .eof, .exitProcess]

theorem badFrames_accepted :
    (Engine.run Engine.init badFrames).map engineObs = some (.eof, [(1, false), (2, false)], some true) := by
  decide

/-- `Refusal_WrongRevision_RefusedBeforeDispatchAndTheEngineKeepsServing`: a call, a call
with revision `sha256:0`, a cancel of the first call with that revision. Run: the second
call refused at once, one line for the cancel, the first call answered after the store
opened, one snapshot read in all. -/
def wrongRevision : List Engine.Ev :=
  [.frame (.req 1 .call true), .enter 1, .frame (.req 2 .call false), .frame (.cancel 1 false),
   .respond 1 false, .unwind true]

theorem wrongRevision_accepted :
    (Engine.run Engine.init wrongRevision).map engineObs = some (.reading, [(2, true), (1, false)], none) := by
  decide

/-- `Refusal_DuplicateId_RefusedForThatIdAndTheFirstCallFinishes`: call 1, call 1, call 2.
Run: an `invalid_argument` refusal for id 1 at once; both calls answered after the store
opened. -/
def duplicate : List Engine.Ev :=
  [.frame (.req 1 .call true), .enter 1, .frame (.req 1 .call true), .frame (.req 2 .call true), .enter 2,
   .respond 1 false, .respond 2 false, .unwind true, .unwind true]

theorem duplicate_accepted :
    (Engine.run Engine.init duplicate).map engineObs =
      some (.reading, [(1, true), (1, false), (2, false)], none) := by decide

def calls (first count : Nat) : List Engine.Ev :=
  (List.range count).map (fun i => .frame (.req (first + i) .call true))

/-- `Refusal_CallPastThePendingLimit_RefusedForItsIdAndTheOthersFinish`: 33 calls in one
write. Run: a retryable `collector_unavailable` for the 33rd at once; the 32 answered after
the store opened. -/
def pastTheLimit : List Engine.Ev :=
  [.frame (.req 1 .call true), .enter 1, .frame (.req 2 .call true), .enter 2] ++ calls 3 31 ++
  [.respond 1 false, .respond 2 false, .unwind true, .unwind true] ++
  ((List.range 30).map (fun i => serve (i + 3))).flatten

set_option maxRecDepth 20000 in
theorem pastTheLimit_accepted :
    (Engine.run Engine.init pastTheLimit).map
      (fun s => (s.loop, s.out.head?, (s.out.filter (!·.2)).length, s.pending.length)) =
      some (.reading, some (33, true), 32, 0) := by decide

/-- `Slots_CancelAtTheLimitThenNewCalls_AllAcceptedAndTheCancelledCallIsNotAnswered`
(Design.traceA): 32 calls, the first two at the store; then cancel 1 and call 33, cancel 2
and a health request (34). Run: after the store opened, 32 replies, none an error, none for
1 or 2. The two cancelled tasks end without a reply (`unwind`). -/
def cancelAtTheLimit : List Engine.Ev :=
  [.frame (.req 1 .call true), .enter 1, .frame (.req 2 .call true), .enter 2] ++ calls 3 30 ++
  [.frame (.cancel 1 true), .frame (.req 33 .call true), .frame (.cancel 2 true), .frame (.req 34 .health true),
   .unwind true, .unwind true] ++
  ((List.range 32).map (fun i => serve (i + 3))).flatten

set_option maxRecDepth 20000 in
theorem cancelAtTheLimit_accepted :
    (Engine.run Engine.init cancelAtTheLimit).map
      (fun s => (s.loop, s.out.length, s.out.any (·.2), s.out.any (·.1 ≤ 2), s.pending.length, s.endingBusy)) =
      some (.reading, 32, false, false, 0, 0) := by decide

/-- `Slots_ReplyReadBeforeItsWriteReturned_TheNextCallIsAccepted` (Design.traceB): 32 calls;
the reply to the first is readable while its write has not returned; call 33 arrives. Run:
33 replies in all, none an error. -/
def replyReadBeforeWriteReturned : List Engine.Ev :=
  [.frame (.req 1 .call true), .enter 1, .frame (.req 2 .call true), .enter 2] ++ calls 3 30 ++
  [.respond 1 false, .frame (.req 33 .call true), .unwind true, .respond 2 false, .unwind true] ++
  ((List.range 31).map (fun i => serve (i + 3))).flatten

set_option maxRecDepth 20000 in
theorem replyReadBeforeWriteReturned_accepted :
    (Engine.run Engine.init replyReadBeforeWriteReturned).map
      (fun s => (s.loop, s.out.length, s.out.any (·.2), s.pending.length)) =
      some (.reading, 33, false, 0) := by decide

/-- `EngineProtocol_FragmentedAndCoalescedReads_KeepRequestIdentity` (published engine):
frames cut across writes and inside a two-byte character (a, b, c, d renumbered 1 to 4).
Run: four replies, b an `invalid_argument` refusal of its cursor; exit 0 at end of input. -/
def fragments : List Engine.Ev :=
  [.frame (.req 1 .health true)] ++ serve 1 ++ [.frame (.req 2 .call true)] ++ serve 2 true ++
  [.frame (.req 3 .call true)] ++ serve 3 ++ [.frame (.req 4 .health true)] ++ serve 4 ++ [.eof, .exitProcess]

theorem fragments_accepted :
    (Engine.run Engine.init fragments).map engineObs =
      some (.eof, [(1, false), (2, true), (3, false), (4, false)], some true) := by decide

/-! ## Host and engine (frames as engine.ts writes them) -/

def systemObs (s : System.St) : Engine.Loop × Nat × Nat :=
  (s.eng.loop, s.host.pending.length, (s.eng.out.filter (fun r => r.1 ≥ 3 && !r.2)).length)

/-- `EngineProtocol_CancelThenNewCalls_WithinTheHostLimit_KeepServing` (published engine):
call 1 (`traces_get` over a 20 000-span trace) and call 2; call 2 answers while call 1 still
runs; then the cancel of 1 and 32 new calls in one write. Run: all 32 answered. Call 1 keeps
its worker until its work ends, so the other worker serves the 32 one after another. -/
def cancelThenNewCalls : List System.Ev :=
  [.host (.requestTool true), .host (.requestTool true), .deliver, .deliver, .eng (.enter 1),
   .eng (.enter 2), .eng (.respond 2 false), .eng (.unwind true), .receive, .host (.abort 1)] ++
  List.replicate 32 (.host (.requestTool true)) ++ List.replicate 33 .deliver ++
  ((List.range 32).map (fun i => (serve (i + 3)).map System.Ev.eng)).flatten ++
  List.replicate 32 .receive

set_option maxRecDepth 40000 in
theorem cancelThenNewCalls_accepted :
    (System.run System.init cancelThenNewCalls).map systemObs = some (.reading, 0, 32) := by decide

/-- `HostFrame_ArgumentsNestedPastTheReaderDepth_RefusedAloneAndTheEngineKeepsServing` and
`HostFrame_TraceContextWithAnUnpairedSurrogate_...`: one call frame the engine cannot decode.
Run: an `invalid_argument` reply for its id, and the engine answers the next request. -/
def undecodable : List System.Ev := [.host (.requestTool true), .deliverUndecodable, .receive]

theorem undecodable_accepted :
    (System.run System.init undecodable).map (fun s => (s.eng.loop, s.host.pending.length, s.eng.out)) =
      some (.reading, 0, [(1, true)]) := by decide

end NamiProofs.HostEngine.Traces
