import NamiStorage
/-
`lake exe replay FILE...`: replays recorded runs of the store through the model's `step`
(`NamiStorage/Queue/Trace.lean`). Exit 0 when every run is accepted, 1 when one is rejected, 2 on a
line it cannot read.
-/
open Nami.Storage.Queue Nami.Storage.Queue.Trace

def replayFile (path : String) : IO UInt32 := do
  let text ← IO.FS.readFile path
  let mut lines : Array Line := #[]
  let mut number := 0
  for raw in text.splitOn "\n" do
    number := number + 1
    match parseLine raw.trimAscii.toString with
    | some none => pure ()
    | some (some line) => lines := lines.push line
    | none =>
        IO.eprintln s!"{path}:{number}: cannot read '{raw}'"
        return 2
  match replay (init 1000) lines.toList 1 with
  | .ok s =>
      IO.println s!"ACCEPTED {path}: {lines.size} lines, {s.nextId} jobs, commit log {repr s.db}"
      return 0
  | .error e =>
      IO.println s!"REJECTED {path}: {e}"
      return 1

def main (args : List String) : IO UInt32 := do
  if args.isEmpty then
    IO.eprintln "usage: lake exe replay FILE..."
    return 2
  let mut worst : UInt32 := 0
  for path in args do
    let code ← replayFile path
    if code > worst then worst := code
  return worst
