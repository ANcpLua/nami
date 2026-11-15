import NamiStorage.Queue.Model
/-
Replay of runs recorded from the real store (`StorageTraceTests` in `tests/Nami.IntegrationTests`)
through the model's `step`. A recorded run holds what a test can observe without a hook in product
code; the replayer adds the steps nobody can observe and rejects the run when no step fits.

Observed lines:
* `cap N`               the store's `jobQueueCapacity`
* `call KIND J`         the test starts a store call; job J is `nextId`: `begin KIND 0`, `tryWrite J`, and
                        `arm J` when the job entered the channel (`ArmCancellation` follows `TryWrite` on the
                        caller's thread with no await between, `DuckDbStore.cs:392-394`)
* `arm J`               an explicit `arm J`, for runs recorded with finer steps
* `cancel J`            the test cancels the call's token
* `claim`               the store's `beforeWrite` hook ran: the writer took the next job
* `commit`              `COMMIT` (or a maintenance `CHECKPOINT`) ran on the writer connection
* `commit J`            the same, for a recorder that also knows the job: the running job must be J
* `outcome J RESULT`    the caller's task ended (`ok`, `canceled`, `failed`) or the call threw (`rejected`)
* `dispose`             `DisposeAsync` was called
* `stopped`             `DisposeAsync` returned
Steps added by the replayer, never observed: the skip of a job cancelled before its claim (forced by
FIFO order), the end of the running job before the writer takes the next one (`finish`, `abort` or
`fail`, chosen by that job's recorded outcome), `complete` and `shutdownCancel` after `dispose`, and
`exit` before `stopped`. Work units are not recorded: every job is begun with `work = 0`.
-/
namespace Nami.Storage.Queue.Trace
open Nami.Storage.Queue

inductive Line where
  | cap (n : Nat)
  | call (k : Kind) (j : Nat)
  | arm (j : Nat)
  | cancel (j : Nat)
  | claim
  | commit (j : Nat)
  | commitRunning
  | outcome (j : Nat) (o : Outcome)
  | dispose
  | stopped
  deriving DecidableEq, Repr

/-- The recorded result of job `j` later in the trace, if any. -/
def recordedOutcome (j : Nat) : List Line → Option Outcome
  | [] => none
  | .outcome i o :: rest => if i = j then some o else recordedOutcome j rest
  | _ :: rest => recordedOutcome j rest

/-- End the running job the way its recorded outcome says, or as the model allows. -/
def release (s : State) (rest : List Line) : Except String State :=
  match s.writer with
  | .running j _ committed =>
      let event :=
        match recordedOutcome j rest, committed with
        | some .ok, _ => Event.finish
        | some .canceled, _ => Event.abort
        | some .failed, _ => Event.fail
        | _, true => Event.finish
        | _, false => if tokenCancelled s j then Event.abort else Event.fail
      match step s event with
      | some s' => .ok s'
      | none => .error s!"job {j} cannot end with {repr event}"
  | _ => .ok s

/-- Take queue heads that were cancelled before their claim; they never reach `beforeWrite`. -/
def skipCancelled (s : State) : Nat → State
  | 0 => s
  | fuel + 1 =>
      match s.queue with
      | j :: _ =>
          if (s.job j).claim = .cancelledBeforeClaim then
            match step s .dequeue with
            | some s' => skipCancelled s' fuel
            | none => s
          else s
      | [] => s

def need (s : State) (e : Event) (what : String) : Except String State :=
  match step s e with
  | some s' => .ok s'
  | none => .error s!"{what}: {repr e} is not enabled"

def replayLine (s : State) (line : Line) (rest : List Line) : Except String State :=
  match line with
  | .cap n => if s.nextId = 0 then .ok { s with cap := n } else .error "cap after the first call"
  | .call k j =>
      if j ≠ s.nextId then .error s!"call {j} but the next job id is {s.nextId}"
      else do
        let s ← need s (.begin k 0) s!"call {j}"
        let s ← need s (.tryWrite j) s!"call {j}"
        if (s.job j).stage = .queued then need s (.arm j) s!"call {j}" else .ok s
  | .arm j => need s (.arm j) s!"arm {j}"
  | .cancel j => need s (.cancel j) s!"cancel {j}"
  | .claim => do
      let s ← release s rest
      let s := skipCancelled s s.queue.length
      need s .dequeue "claim"
  | .commit j =>
      match s.writer with
      | .running r _ _ => if r = j then need s .commit s!"commit {j}" else .error s!"commit {j} while job {r} runs"
      | _ => .error s!"commit {j} with no running job"
  | .commitRunning =>
      match s.writer with
      | .running _ _ _ => need s .commit "commit"
      | _ => .error "commit with no running job"
  | .outcome j o => do
      let s ← match s.writer with
        | .running r _ _ => if r = j then release s (line :: rest) else .ok s
        | _ => .ok s
      if (s.job j).outcome = o then .ok s
      else .error s!"job {j} ended {repr (s.job j).outcome}, the run recorded {repr o}"
  | .dispose => do
      let s ← need s .dispose "dispose"
      let s ← need s .complete "dispose"
      need s .shutdownCancel "dispose"
  | .stopped => do
      let s ← release s rest
      let s := skipCancelled s s.queue.length
      let s ← need s .exit "stopped"
      if s.writer = .stopped then .ok s else .error "writer did not stop"

/-- Replay a recorded run; the error names the first line that does not fit. -/
def replay (s : State) : List Line → Nat → Except String State
  | [], _ => .ok s
  | line :: rest, n =>
      match replayLine s line rest with
      | .ok s' => replay s' rest (n + 1)
      | .error e => .error s!"line {n}: {e}"

def accepts (lines : List Line) : Bool :=
  match replay (init 1000) lines 1 with
  | .ok _ => true
  | .error _ => false

def parseKind : String → Option Kind
  | "ingest" => some .ingest
  | "read" => some .read
  | "maint" => some .maint
  | s =>
      if s.startsWith "replay:" then (s.drop 7).toNat?.map Kind.replay else none

def parseOutcome : String → Option Outcome
  | "ok" => some .ok
  | "canceled" => some .canceled
  | "failed" => some .failed
  | "rejected" => some .rejected
  | _ => none

def parseLine (text : String) : Option (Option Line) :=
  let words := (text.splitOn " ").filter (· ≠ "")
  match words with
  | [] => some none
  | w :: _ =>
      if w.startsWith "#" then some none
      else match words with
        | ["cap", n] => n.toNat?.map fun n => some (.cap n)
        | ["call", k, j] => do let k ← parseKind k; let j ← j.toNat?; pure (some (.call k j))
        | ["arm", j] => j.toNat?.map fun j => some (.arm j)
        | ["cancel", j] => j.toNat?.map fun j => some (.cancel j)
        | ["claim"] => some (some .claim)
        | ["commit"] => some (some .commitRunning)
        | ["commit", j] => j.toNat?.map fun j => some (.commit j)
        | ["outcome", j, o] => do let j ← j.toNat?; let o ← parseOutcome o; pure (some (.outcome j o))
        | ["dispose"] => some (some .dispose)
        | ["stopped"] => some (some .stopped)
        | _ => none

end Nami.Storage.Queue.Trace
