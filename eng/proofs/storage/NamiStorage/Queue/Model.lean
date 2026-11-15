/-
The single writer of nami's DuckDB store, as one state machine.

Code (commit 843383e, `src/nami.collector.storage/`):
* `DuckDbStore.cs:386-424`  ExecuteWriteAsync (two overloads) and ExecuteMaintenanceWriteAsync
* `DuckDbStore.cs:453-486`  WriterLoopAsync
* `DuckDbStore.cs:966-1052` WriteJob<TResult>: TryClaim, ArmCancellation, ExecuteAsync, OnAborted, CancelBeforeClaim
* `DuckDbStore.cs:203-295`  DisposeAsync and ShutdownAsync
* `DuckDbStore.Snapshots.cs:8-114` both kinds of read run as writer jobs
* `DuckDbStore.cs:298-367`, `DuckDbStore.Metrics.cs:10-41` the three ingest paths
* `DuckDbStore.Retention.cs`, `DuckDbStore.Metrics.cs:43-115` maintenance jobs
Each event below names the lines it stands for.

What one step abstracts:
* A job's action is `work` units, each ending at a token check (row loop `Snapshots.cs:106-110`,
  serialization chunks `:68-69`, `RecordIngestion` `:126`, chunked inserts `DuckDbStore.cs:448`, the
  native interrupt `:1012`), then one commit, its last statement. Time is not modelled: a unit can be
  long (`ReadBytes` `:28`, `ToArray` `:76`, the payload insert `:83`, `Commit`).
* The database is the commit log `db` (job ids in commit order); DuckDB's transaction is atomic.
* A read sees the commit log at its claim (`seen`), because it runs on the writer connection inside one
  transaction while no other job runs.
* A replay of a snapshot that is not stored fails at its claim (`SnapshotUnavailableException`,
  `Snapshots.cs:25`); expiry (`:20`) and the 30-minute lifetime are not modelled, so the model's
  replays fail less often than the code's.

Not modelled: the reader pool of file mode (`ExecuteReadAsync`, `DuckDbStore.cs:370-384`, used only by
health and metric points, never by the six tools); the startup rollback (`:163-189`); the writer loop's
non-cancellation exit (`:475-478`, unreachable: every job exception is caught at `:465-468`); the
5-second `DisposeAsync` deadline (`:215`); wall-clock time behind `received`, `watermark` and `now`
(`Snapshots.cs:12-13`, `:119`).
-/
namespace Nami.Storage.Queue

/-- The four ways a job enters the channel. -/
inductive Kind where
  /-- Spans, logs, metrics: `ExecuteWriteAsync(op, ct)`, `cancelAfterClaim: false`, `TryWrite`. -/
  | ingest
  /-- Fresh `ReadSnapshotAsync` (and in-memory `ExecuteReadAsync`): `cancelAfterClaim: true`, `TryWrite`. -/
  | read
  /-- `ReadSnapshotAsync(project, snapshotId)` of the snapshot written by job `r`. -/
  | replay (r : Nat)
  /-- Retention and checkpoint: `ExecuteMaintenanceWriteAsync`, `cancelAfterClaim: true`, `WriteAsync`. -/
  | maint
  deriving DecidableEq, Repr, Inhabited

/-- Where the caller's job is: before `TryWrite`, in `_jobs`, taken out by the writer, or never queued. -/
inductive Stage where
  | absent
  | begun
  | queued
  | taken
  | refused
  deriving DecidableEq, Repr, Inhabited

/-- `WriteJob<T>._state`: 0 unclaimed, 1 claimed by `TryClaim`, 2 cancelled by `CancelBeforeClaim`. -/
inductive Claim where
  | unclaimed
  | claimed
  | cancelledBeforeClaim
  deriving DecidableEq, Repr, Inhabited

/-- What the caller's awaited task ends with. `rejected` is a synchronous throw: the job never had a
completion source in the channel (ObjectDisposedException, NamiStoreUnavailableException,
ChannelClosedException). -/
inductive Outcome where
  | pending
  | ok
  | canceled
  | failed
  | rejected
  deriving DecidableEq, Repr, Inhabited

structure Job where
  kind : Kind := .ingest
  /-- Units of cooperative work before the commit: row chunks, read rows, serialized chunks. -/
  work : Nat := 0
  stage : Stage := .absent
  claim : Claim := .unclaimed
  armed : Bool := false
  callerCancelled : Bool := false
  outcome : Outcome := .pending
  deriving DecidableEq, Repr, Inhabited

/-- `WriterLoopAsync`: waiting in `ReadAllAsync`, executing one job, or returned. -/
inductive Writer where
  | idle
  | running (j : Nat) (left : Nat) (committed : Bool)
  | stopped
  deriving DecidableEq, Repr, Inhabited

structure State where
  /-- `jobQueueCapacity` (default 1000, `DuckDbStore.cs:34`). -/
  cap : Nat
  nextId : Nat := 0
  job : Nat → Job := fun _ => {}
  /-- The contents of `_jobs`, oldest first. -/
  queue : List Nat := []
  writer : Writer := .idle
  /-- Transactions committed on the writer connection, in commit order. -/
  db : List Nat := []
  /-- `_disposed = 1`. -/
  disposed : Bool := false
  /-- `_jobs.Writer.TryComplete()`. -/
  completed : Bool := false
  /-- `_cts` cancelled. -/
  shutdownCancelled : Bool := false
  /-- Ghost: what a read job saw (the commit log at its claim). No guard reads it. -/
  seen : Nat → List Nat := fun _ => []
  /-- Ghost: the jobs acknowledged when this job entered `_jobs`. No guard reads it. -/
  ackedAtEnqueue : Nat → List Nat := fun _ => []

/-- Point update of a function. -/
def upd {α : Type} (f : Nat → α) (j : Nat) (x : α) : Nat → α := fun i => if i = j then x else f i

/-- Every kind except ingestion links the caller's token after the claim (`DuckDbStore.cs:1004-1009`). -/
def cancelAfterClaim : Kind → Bool
  | .ingest => false
  | _ => true

/-- The token `ExecuteAsync` runs the action with: `_cts.Token`, linked to the caller's for reads and
maintenance (`DuckDbStore.cs:1006-1009`). -/
def tokenCancelled (s : State) (j : Nat) : Bool :=
  s.shutdownCancelled || (cancelAfterClaim (s.job j).kind && (s.job j).callerCancelled)

/-- Jobs whose caller has received a result. -/
def okJobs (s : State) : List Nat :=
  (List.range s.nextId).filter fun k => (s.job k).outcome == .ok

/-- `CancelBeforeClaim` (`DuckDbStore.cs:1047-1051`): compare-exchange 0 → 2, then `TrySetCanceled`. -/
def cancelBeforeClaim (x : Job) : Job :=
  if x.claim = .unclaimed then
    { x with claim := .cancelledBeforeClaim,
             outcome := if x.outcome = .pending then .canceled else x.outcome }
  else x

/-- `OnAborted(new OperationCanceledException(...))` in the writer's `finally` (`DuckDbStore.cs:481-484`):
`TrySetCanceled`; `_state` is left as it was. -/
def drainOne (x : Job) : Job :=
  { x with stage := .taken, outcome := if x.outcome = .pending then .canceled else x.outcome }

inductive Event where
  /-- `ThrowIfDisposed(); ct.ThrowIfCancellationRequested(); new WriteJob<T>(...)` -/
  | begin (k : Kind) (work : Nat)
  /-- `_jobs.Writer.TryWrite(job)` (or `WriteAsync` returning for maintenance) -/
  | tryWrite (j : Nat)
  /-- `job.ArmCancellation()` -/
  | arm (j : Nat)
  /-- The caller's token is cancelled (`CancelBeforeClaim` runs if it is registered). -/
  | cancel (j : Nat)
  /-- The writer takes the oldest job and runs `TryClaim`. -/
  | dequeue
  /-- One unit of the action's work between two token checks. -/
  | progress
  /-- The action's transaction commits (its last statement). -/
  | commit
  /-- `_tcs.TrySetResult(result)` -/
  | finish
  /-- The token is observed cancelled before the commit: rollback, `TrySetCanceled`. -/
  | abort
  /-- The action throws before the commit: rollback, `TrySetException` (or `OnAborted`). -/
  | fail
  /-- `DisposeAsync`: `_disposed = 1` -/
  | dispose
  /-- `ShutdownAsync`: `_jobs.Writer.TryComplete()` -/
  | complete
  /-- `ShutdownAsync`: `_cts.CancelAsync()` -/
  | shutdownCancel
  /-- `ReadAllAsync` ends (completed and empty, or cancelled); the `finally` drains the channel. -/
  | exit
  deriving DecidableEq, Repr

/-- One step. `none` means the event cannot happen in this state.
* `begin`: `ThrowIfDisposed` (`DuckDbStore.cs:389`, `:401`, `:418`, `:949`) throws when disposed.
* `tryWrite`: `TryWrite` false on a full or completed channel throws `NamiStoreUnavailableException`
  (`:392-393`, `:408-409`); maintenance waits in `WriteAsync` (`:421`), which throws once completed.
* `arm`: `ArmCancellation` (`:980-987`) registers `CancelBeforeClaim`, which runs at once if the token is
  already cancelled.
* `cancel`: the registered `CancelBeforeClaim` (`:1047-1051`); maintenance still waiting in `WriteAsync`
  ends with `OperationCanceledException`.
* `dequeue`: `ReadAllAsync` (`:457`) and `TryClaim` (`:459`, `:989-999`); a lost claim skips the job.
* `progress`/`commit`/`finish`/`abort`/`fail`: `ExecuteAsync` (`:1001-1036`): the token is `_cts.Token`
  for ingestion and linked to the caller's for the rest (`:1006-1009`); `TrySetResult` after the action
  returns (`:1017-1018`); a cancelled token before the commit rolls back and `TrySetCanceled`
  (`:1020-1027`); any other exception `TrySetException` (`:1028-1031`, `:465-468`).
* `dispose`/`complete`/`shutdownCancel`: `DisposeAsync` (`:206-210`), `ShutdownAsync` (`:237`, `:241`).
* `exit`: `ReadAllAsync` ends or throws (`:471-474`); the `finally` drains with `OnAborted` (`:479-485`). -/
def step (s : State) : Event → Option State
  | .begin k w =>
      let x : Job :=
        { kind := k, work := w,
          stage := if s.disposed then .refused else .begun,
          outcome := if s.disposed then .rejected else .pending }
      some { s with nextId := s.nextId + 1, job := upd s.job s.nextId x }
  | .tryWrite j =>
      let x := s.job j
      if x.stage ≠ .begun then none
      else if s.completed then
        some { s with job := upd s.job j { x with stage := .refused, outcome := .rejected } }
      else if s.queue.length < s.cap then
        some { s with job := upd s.job j { x with stage := .queued },
                      queue := s.queue ++ [j],
                      ackedAtEnqueue := upd s.ackedAtEnqueue j (okJobs s) }
      else if x.kind = .maint then none
      else some { s with job := upd s.job j { x with stage := .refused, outcome := .rejected } }
  | .arm j =>
      let x := s.job j
      if (x.stage = .queued ∨ x.stage = .taken) ∧ x.armed = false then
        let y := { x with armed := true }
        some { s with job := upd s.job j (if y.callerCancelled then cancelBeforeClaim y else y) }
      else none
  | .cancel j =>
      let x := s.job j
      if x.stage = .absent ∨ x.callerCancelled = true then none
      else
        let y := { x with callerCancelled := true }
        let y :=
          if x.stage = .begun ∧ x.kind = .maint then { y with stage := .refused, outcome := .canceled }
          else if y.armed then cancelBeforeClaim y
          else y
        some { s with job := upd s.job j y }
  | .dequeue =>
      match s.writer, s.queue with
      | .idle, j :: rest =>
          let x := s.job j
          if x.claim = .unclaimed then
            match x.kind with
            | .replay r =>
                if r ∈ s.db then
                  some { s with queue := rest,
                                job := upd s.job j { x with stage := .taken, claim := .claimed },
                                writer := .running j x.work false,
                                seen := upd s.seen j (s.seen r) }
                else
                  -- `SELECT payload ... ` finds nothing: SnapshotUnavailableException before any work.
                  some { s with queue := rest,
                                job := upd s.job j { x with stage := .taken, claim := .claimed,
                                                            outcome := .failed } }
            | .read =>
                some { s with queue := rest,
                              job := upd s.job j { x with stage := .taken, claim := .claimed },
                              writer := .running j x.work false,
                              seen := upd s.seen j s.db }
            | _ =>
                some { s with queue := rest,
                              job := upd s.job j { x with stage := .taken, claim := .claimed },
                              writer := .running j x.work false }
          else
            some { s with queue := rest, job := upd s.job j { x with stage := .taken } }
      | _, _ => none
  | .progress =>
      match s.writer with
      | .running j (n + 1) false =>
          if tokenCancelled s j then none else some { s with writer := .running j n false }
      | _ => none
  | .commit =>
      match s.writer with
      | .running j 0 false =>
          if tokenCancelled s j then none
          else some { s with writer := .running j 0 true, db := s.db ++ [j] }
      | _ => none
  | .finish =>
      match s.writer with
      | .running j 0 true =>
          some { s with writer := .idle, job := upd s.job j { s.job j with outcome := .ok } }
      | _ => none
  | .abort =>
      match s.writer with
      | .running j _ false =>
          if tokenCancelled s j then
            some { s with writer := .idle, job := upd s.job j { s.job j with outcome := .canceled } }
          else none
      | _ => none
  | .fail =>
      match s.writer with
      | .running j _ false =>
          some { s with writer := .idle, job := upd s.job j { s.job j with outcome := .failed } }
      | _ => none
  | .dispose => if s.disposed then none else some { s with disposed := true }
  | .complete =>
      if s.disposed ∧ s.completed = false then some { s with completed := true } else none
  | .shutdownCancel =>
      if s.completed ∧ s.shutdownCancelled = false then some { s with shutdownCancelled := true } else none
  | .exit =>
      match s.writer with
      | .idle =>
          if (s.completed ∧ s.queue = []) ∨ s.shutdownCancelled then
            some { s with writer := .stopped, queue := [],
                          job := fun i => if i ∈ s.queue then drainOne (s.job i) else s.job i }
          else none
      | _ => none

/-- A store just opened with capacity `cap`. -/
def init (cap : Nat) : State := { cap }

/-- Run a trace through any step function; `none` when some event is not enabled. -/
def runBy (stp : State → Event → Option State) (s : State) : List Event → Option State
  | [] => some s
  | e :: es => (stp s e).bind fun s' => runBy stp s' es

/-- Run a trace through the model. -/
abbrev run := runBy step

/-- States a step function reaches from a store opened with capacity `cap`. -/
inductive ReachableBy (stp : State → Event → Option State) (cap : Nat) : State → Prop where
  | init : ReachableBy stp cap (init cap)
  | step {s s' : State} (e : Event) : ReachableBy stp cap s → stp s e = some s' → ReachableBy stp cap s'

/-- Reachable states of the model. -/
abbrev Reachable := ReachableBy step

/-- Zero or more steps. -/
inductive StepsBy (stp : State → Event → Option State) : State → State → Prop where
  | refl (s : State) : StepsBy stp s s
  | tail {s t u : State} (e : Event) : StepsBy stp s t → stp t e = some u → StepsBy stp s u

abbrev Steps := StepsBy step

theorem runBy_reachable {stp : State → Event → Option State} {cap : Nat} :
    ∀ (es : List Event) (s s' : State), ReachableBy stp cap s → runBy stp s es = some s' →
      ReachableBy stp cap s'
  | [], s, s', h, hr => by
      simp [runBy] at hr
      subst hr
      exact h
  | e :: es, s, s', h, hr => by
      simp only [runBy, Option.bind_eq_some_iff] at hr
      obtain ⟨t, ht, hrest⟩ := hr
      exact runBy_reachable es t s' (ReachableBy.step e h ht) hrest

theorem reachableBy_cap {stp : State → Event → Option State} {cap : Nat}
    (hcap : ∀ s e s', stp s e = some s' → s'.cap = s.cap) :
    ∀ {s : State}, ReachableBy stp cap s → s.cap = cap
  | _, .init => rfl
  | _, .step e h hs => by rw [hcap _ _ _ hs]; exact reachableBy_cap hcap h

theorem stepsBy_trans {stp : State → Event → Option State} {s t u : State}
    (h1 : StepsBy stp s t) (h2 : StepsBy stp t u) : StepsBy stp s u := by
  induction h2 with
  | refl => exact h1
  | tail e _ hs ih => exact StepsBy.tail e ih hs

theorem stepsBy_reachable {stp : State → Event → Option State} {cap : Nat} {s t : State}
    (hs : ReachableBy stp cap s) (h : StepsBy stp s t) : ReachableBy stp cap t := by
  induction h with
  | refl => exact hs
  | tail e _ hst ih => exact ReachableBy.step e ih hst

/-- Steps taken by the writer task (everything else is a caller or `DisposeAsync`). -/
def Event.isWriter : Event → Bool
  | .dequeue | .progress | .commit | .finish | .abort | .fail | .exit => true
  | _ => false

end Nami.Storage.Queue
