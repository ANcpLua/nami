import NamiStorage.Queue.Model
/-
Claims about the write queue, written from the contract, not from the implementation.

Sources:
* PRD `docs/prd/nami.md`: I13 (accepted data becomes queryable), I17 (duplicates follow the
  documented ingest contract), I18 `Shutdown_DrainsAcceptedTelemetryOrReportsTheRemainder`,
  4.1 step 5 (analysis runs over a consistent snapshot), T2/T3 (a snapshot id answers the same).
* Contract `contracts/common/telemetry-query.tsp`: `Freshness.watermark` "Ingest is complete through
  this instant for every signal read"; `SnapshotId` "immutable telemetry snapshot".
* Plan P13: "A cancelled or timed-out call frees the store's single write queue", the test names
  `CancelledFreshCall_ReleasesTheWriteQueue` and `FreshCall_..._WhileIngestContinues`.
* OTLP: a success response acknowledges the export; a failure may be retried.

`Claims.*` take the step function as a parameter so that deliberately broken models can be checked
against the same words. `Spec.*` are the closed statements about the model of the code; every one of
them is the exact type of a theorem (`lean_audit.sh --spec Nami.Storage.Spec`). Editing this file
reopens review.
-/
namespace Nami.Storage.Spec
open Nami.Storage.Queue

namespace Claims

variable (stp : State → Event → Option State)

/-- A caller that received a result had its transaction committed. -/
def NoLostAcknowledgedWrite : Prop :=
  ∀ cap s, ReachableBy stp cap s → ∀ j, (s.job j).outcome = .ok → j ∈ s.db

/-- No job's transaction is committed twice. -/
def NoDuplicatedWrite : Prop :=
  ∀ cap s, ReachableBy stp cap s → s.db.Nodup

/-- A caller told "cancelled", "failed" or "refused" may retry: its transaction is not committed. -/
def ReportedFailureMeansNotWritten : Prop :=
  ∀ cap s, ReachableBy stp cap s → ∀ j,
    ((s.job j).outcome = .canceled ∨ (s.job j).outcome = .failed ∨ (s.job j).outcome = .rejected) →
    j ∉ s.db

/-- A call has one result: once reported, it never changes. -/
def ResultIsFinal : Prop :=
  ∀ cap s e s', ReachableBy stp cap s → stp s e = some s' → ∀ j,
    (s.job j).outcome ≠ .pending → (s'.job j).outcome = (s.job j).outcome

/-- A job whose caller cancelled before the writer took it never runs. -/
def CancelledBeforeClaimNeverRuns : Prop :=
  ∀ cap s, ReachableBy stp cap s → ∀ j, (s.job j).claim = .cancelledBeforeClaim →
    j ∉ s.db ∧ ∀ n c, s.writer ≠ .running j n c

/-- A caller that cancelled a job the writer has not taken gets its result at once. -/
def CancelledCallerReleasedAtOnce : Prop :=
  ∀ cap s, ReachableBy stp cap s → ∀ j, (s.job j).armed = true → (s.job j).callerCancelled = true →
    (s.job j).claim ≠ .claimed → (s.job j).outcome ≠ .pending

/-- Once a running job's token is cancelled before its commit, the writer's next step frees the
writer, and such a step exists. -/
def CancelledJobReleasesWriter : Prop :=
  ∀ cap s, ReachableBy stp cap s → ∀ j n, s.writer = .running j n false → tokenCancelled s j = true →
    (∀ e s', e.isWriter = true → stp s e = some s' → s'.writer = .idle) ∧
    ∃ e s', e.isWriter = true ∧ stp s e = some s'

/-- The writer can always move while it has work. -/
def WriterNeverStuck : Prop :=
  ∀ cap s, ReachableBy stp cap s → (s.writer ≠ .idle ∨ s.queue ≠ []) → s.writer ≠ .stopped →
    ∃ e s', e.isWriter = true ∧ stp s e = some s'

/-- I18: after the writer has stopped, every job that entered the queue has a result. -/
def ShutdownLeavesNoPendingQueuedJob : Prop :=
  ∀ cap s, ReachableBy stp cap s → s.writer = .stopped → ∀ j,
    ((s.job j).stage = .queued ∨ (s.job j).stage = .taken) → (s.job j).outcome ≠ .pending

/-- I18: once shutdown has completed the channel, a new job is refused at once, not left waiting. -/
def NoJobAcceptedAfterComplete : Prop :=
  ∀ cap s j s', ReachableBy stp cap s → s.completed = true → stp s (.tryWrite j) = some s' →
    (s'.job j).outcome = .rejected ∧ s'.queue = s.queue

/-- I13 and `Freshness.watermark`: a fresh read sees every write acknowledged before the read was
queued. -/
def ReadSeesEveryEarlierAcknowledgedWrite : Prop :=
  ∀ cap s, ReachableBy stp cap s → ∀ r, (s.job r).kind = .read → (s.job r).claim = .claimed →
    ∀ k ∈ s.ackedAtEnqueue r, k ∈ s.seen r

/-- PRD 4.1 step 5: a read is a consistent snapshot, a cut of the commit order. -/
def ReadIsConsistentCut : Prop :=
  ∀ cap s, ReachableBy stp cap s → ∀ r, (s.job r).kind = .read → (s.job r).claim = .claimed →
    s.seen r <+: s.db

/-- T2/T3 and `SnapshotId`: a replay that succeeds returns exactly what its fresh read captured. -/
def ReplayReturnsCapturedRead : Prop :=
  ∀ cap s, ReachableBy stp cap s → ∀ q r, (s.job q).kind = .replay r → (s.job q).outcome = .ok →
    r ∈ s.db ∧ s.seen q = s.seen r

/-- While one job holds the writer, no other transaction commits. -/
def WriterHeldExclusively : Prop :=
  ∀ cap s t, ReachableBy stp cap s → StepsBy stp s t → ∀ r n c n' c',
    s.writer = .running r n c → t.writer = .running r n' c' → ∀ k ∈ t.db, k ∈ s.db ∨ k = r

end Claims

namespace P13
/-! What plan P13 asks for. Stated as P13 states it; the theorems refute them. -/

variable (stp : State → Event → Option State)

/-- `FreshCall_..._WhileIngestContinues`: an ingest job queued while a read (fresh or replay) holds
the writer can commit before that read ends. -/
def IngestCommitsWhileReadRuns : Prop :=
  ∀ cap s, ReachableBy stp cap s → ∀ r n c k, s.writer = .running r n c →
    ((s.job r).kind = .read ∨ ∃ x, (s.job r).kind = .replay x) →
    k ∈ s.queue → (s.job k).kind = .ingest →
    ∃ t, StepsBy stp s t ∧ k ∈ t.db ∧ ∃ n' c', t.writer = .running r n' c'

/-- "A cancelled or timed-out call frees the store's single write queue": a cancelled job does not
keep its place in the queue. -/
def CancelledCallFreesItsSlot : Prop :=
  ∀ cap s, ReachableBy stp cap s → ∀ j, (s.job j).outcome = .canceled → j ∉ s.queue

end P13

/-! Closed statements about the model of the code. -/

def NoLostAcknowledgedWrite : Prop := Claims.NoLostAcknowledgedWrite step
def NoDuplicatedWrite : Prop := Claims.NoDuplicatedWrite step
def ReportedFailureMeansNotWritten : Prop := Claims.ReportedFailureMeansNotWritten step
def ResultIsFinal : Prop := Claims.ResultIsFinal step
def CancelledBeforeClaimNeverRuns : Prop := Claims.CancelledBeforeClaimNeverRuns step
def CancelledCallerReleasedAtOnce : Prop := Claims.CancelledCallerReleasedAtOnce step
def CancelledJobReleasesWriter : Prop := Claims.CancelledJobReleasesWriter step
def WriterNeverStuck : Prop := Claims.WriterNeverStuck step
def ShutdownLeavesNoPendingQueuedJob : Prop := Claims.ShutdownLeavesNoPendingQueuedJob step
def NoJobAcceptedAfterComplete : Prop := Claims.NoJobAcceptedAfterComplete step
def ReadSeesEveryEarlierAcknowledgedWrite : Prop := Claims.ReadSeesEveryEarlierAcknowledgedWrite step
def ReadIsConsistentCut : Prop := Claims.ReadIsConsistentCut step
def ReplayReturnsCapturedRead : Prop := Claims.ReplayReturnsCapturedRead step
def WriterHeldExclusively : Prop := Claims.WriterHeldExclusively step

/-- Refutation of P13's "ingest continues while a fresh call runs". -/
def IngestStallsWhileReadRuns : Prop := ¬ P13.IngestCommitsWhileReadRuns step

/-- For every queue capacity the store accepts (`jobQueueCapacity > 0`, `DuckDbStore.cs:81`) there is
a run in which an ingest call is refused because a read holds the writer. -/
def IngestRefusedWhileReadRuns : Prop :=
  ∀ cap, 0 < cap → ∃ s, Reachable cap s ∧ (∃ r n c, s.writer = .running r n c ∧ (s.job r).kind = .read) ∧
    ∃ j s', (s.job j).kind = .ingest ∧ step s (.tryWrite j) = some s' ∧ (s'.job j).outcome = .rejected

/-- Refutation of "a cancelled call frees its place in the queue". -/
def CancelledCallKeepsItsSlot : Prop := ¬ P13.CancelledCallFreesItsSlot step

end Nami.Storage.Spec
