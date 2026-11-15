import NamiStorage.Queue.Trace
/-
Runs recorded from the real store and accepted by the model. Each list is the exact sequence a test in
`tests/Nami.IntegrationTests/StorageLifetimeTests.cs` records and asserts; the kernel checks here that
the model's `step` accepts it. A change to the store that changes a recorded run fails that test; a
change to the model that rejects the run fails this file.
-/
namespace Nami.Storage.Queue.Traces
open Nami.Storage.Queue Nami.Storage.Queue.Trace

/-- `Trace_SequentialCalls_MatchTheModel`: ingest, fresh read, replay of that read. -/
def sequential : List Line :=
  [.call .ingest 0, .claim, .commitRunning, .call .read 1, .claim, .commitRunning, .call (.replay 1) 2, .claim, .commitRunning,
   .outcome 0 .ok, .outcome 1 .ok, .outcome 2 .ok]

/-- `Trace_CancelledBeforeClaim_MatchesTheModel`: a read cancelled while an ingest job holds the writer is
skipped; the next ingest job runs. -/
def cancelledBeforeClaim : List Line :=
  [.call .ingest 0, .claim, .call .read 1, .cancel 1, .call .ingest 2, .commitRunning, .claim, .commitRunning,
   .outcome 0 .ok, .outcome 1 .canceled, .outcome 2 .ok]

/-- `Trace_DisposeWithQueuedJobs_MatchesTheModel`: disposal with one running and two queued jobs. -/
def disposeQueued : List Line :=
  [.call .ingest 0, .claim, .call .ingest 1, .call .read 2, .dispose, .stopped,
   .outcome 0 .canceled, .outcome 1 .canceled, .outcome 2 .canceled]

theorem sequential_accepted : accepts sequential = true := by decide
theorem cancelledBeforeClaim_accepted : accepts cancelledBeforeClaim = true := by decide
theorem disposeQueued_accepted : accepts disposeQueued = true := by decide

/-- The replayer is not vacuous: a run whose writer commits a job it never took is rejected. -/
theorem commitWithoutClaim_rejected : accepts [.call .ingest 0, .commitRunning] = false := by decide
/-- ... and so is a run that reports a cancelled read as committed. -/
theorem wrongOutcome_rejected :
    accepts [.call .ingest 0, .claim, .call .read 1, .cancel 1, .commitRunning, .outcome 1 .ok] = false := by decide

end Nami.Storage.Queue.Traces
