import NamiProofs.HostEngine.Spec
import NamiProofs.HostEngine.Proofs.Host
import NamiProofs.HostEngine.Proofs.Engine
import NamiProofs.HostEngine.Proofs.System
import NamiProofs.HostEngine.Proofs.Framing

/-!
# Every held claim has teeth: a witness and a broken model

For each claim that holds on the code, (1) a reachable state where its hypotheses hold, so
the claim is not vacuous, and (2) a deliberately broken variant of the model, one plausible
bug, for which the same claim is false.
-/

namespace NamiProofs.HostEngine.Mutants

open NamiProofs.HostEngine

/-! ## Witnesses -/

def toolWaiting : Host.St := (Host.run Host.init [.readyFrame, .requestTool true]).get!
def healthWaiting : Host.St := (Host.run Host.init [.readyFrame, .requestHealth]).get!

theorem witness_nothingWaits : ∃ s, Host.Reachable s ∧ s.child = .exited ∧ s.exit = none :=
  ⟨Host.engineDied, Host.engineDied_reachable, rfl, rfl⟩

theorem witness_deadEngineFails : ∃ s s', Host.Reachable s ∧ s.exit = none ∧ s.child = .running ∧
    (∃ p ∈ s.pending, p.via = .tool) ∧ Host.step s .childExit = some s' :=
  ⟨toolWaiting, _, ⟨[.readyFrame, .requestTool true], rfl⟩, rfl, rfl, ⟨⟨1, 0, .tool⟩, by decide, rfl⟩, rfl⟩

/-- The engine has died, the host runs, and nobody asked it to stop; `/health` is asked. -/
theorem witness_engineGone : ∃ s s', Host.Reachable s ∧ s.ready = true ∧ s.child = .exited ∧
    s.stopped = true ∧ s.exit = none ∧ s.closing = false ∧ Host.step s .requestHealth = some s' :=
  ⟨Host.engineDied, _, Host.engineDied_reachable, rfl, rfl, rfl, rfl, rfl, rfl⟩

theorem witness_healthAnswered : ∃ s, Host.Reachable s ∧ ∃ d ∈ s.done, d.via = .health :=
  ⟨(Host.run Host.init [.readyFrame, .childExit, .requestHealth]).get!,
    ⟨[.readyFrame, .childExit, .requestHealth], rfl⟩, ⟨0, .health, .http 503⟩, by decide, rfl⟩

theorem witness_noOrphan_crash : ∃ s, Host.Reachable s ∧ s.exit = some (.code 1) :=
  ⟨(Host.run Host.init [.badFrame false]).get!, ⟨[.badFrame false], rfl⟩, rfl⟩

theorem witness_noOrphan_graceful : ∃ s, Host.Reachable s ∧ s.exit = some (.code 0) :=
  ⟨(Host.run Host.init [.readyFrame, .hostSignal, .transportStopped, .childExit, .childClose, .loopEmpty]).get!,
    ⟨[.readyFrame, .hostSignal, .transportStopped, .childExit, .childClose, .loopEmpty], rfl⟩, rfl⟩

theorem witness_health503 : ∃ s s', Host.Reachable s ∧ s.exit = none ∧ s.hasExited = false ∧
    (∃ p ∈ s.pending, p.via = .health ∧ Host.step s (.reply p.id true) = some s') :=
  ⟨healthWaiting, _, ⟨[.readyFrame, .requestHealth], rfl⟩, rfl, rfl, ⟨⟨1, 0, .health⟩, by decide, rfl, rfl⟩⟩

theorem witness_cancel : ∃ s, Host.Reachable s ∧ s.exit = none ∧ s.pending ≠ [] :=
  ⟨toolWaiting, ⟨[.readyFrame, .requestTool true], rfl⟩, rfl, by decide⟩

theorem witness_engineExit : ∃ s, Engine.Reachable s ∧ s.loop ≠ .reading ∧ s.pending ≠ [] :=
  ⟨(Engine.run Engine.init [.frame (.req 1 .call true), .eof]).get!, ⟨[.frame (.req 1 .call true), .eof], rfl⟩,
    by decide, by decide⟩

/-- A bad frame arrives while a call is in flight. -/
theorem witness_badFrame : ∃ s s', Engine.Reachable s ∧ s.loop = .reading ∧ s.pending ≠ [] ∧
    Engine.step s (.frame .garbage) = some s' :=
  ⟨Engine.oneCall, _, Engine.oneCall_reachable, rfl, by decide, rfl⟩

theorem witness_wrongRevision : ∃ s s', Engine.Reachable s ∧ s.loop = .reading ∧
    Engine.step s (.frame (.req 1 .call false)) = some s' :=
  ⟨Engine.init, _, Engine.init_reachable, rfl, rfl⟩

/-- A repeated id, and a 33rd call at a full table. -/
theorem witness_duplicate : ∃ s, Engine.Reachable s ∧ s.loop = .reading ∧ (Engine.ids s).contains 1 = true :=
  ⟨Engine.oneCall, Engine.oneCall_reachable, rfl, by decide⟩

def fullTable : List Engine.Ev := (List.range 32).map (fun i => .frame (.req (i + 1) .call true))

theorem witness_full : ∃ s, Engine.Reachable s ∧ s.loop = .reading ∧ s.pending.length ≥ Engine.maxPending :=
  ⟨(Engine.run Engine.init fullTable).get!, ⟨fullTable, by decide⟩, by decide, by decide⟩

/-- The end of the input does end the loop. -/
theorem witness_endOfInput : ∃ s s', Engine.Reachable s ∧ s.loop = .reading ∧
    Engine.step s .eof = some s' ∧ s'.loop ≠ .reading :=
  ⟨Engine.init, _, Engine.init_reachable, rfl, rfl, by decide⟩

/-- The composition reaches a full table on both sides (`System.cancelAtFullQueue`). -/
theorem witness_correctHost : ∃ s, System.Reachable s ∧ s.host.pending.length = 32 ∧ s.eng.pending.length = 32 :=
  ⟨(System.run System.init System.cancelAtFullQueue).get!, ⟨System.cancelAtFullQueue, by decide⟩,
    by decide, by decide⟩

/-- A frame cut across reads and two frames in one read. -/
theorem witness_framing :
    Framing.readAllBy Framing.onChunk 1048576 [[1, 2], [3, 10, 4], [10]] =
      some ⟨[[1, 2, 3], [4]], []⟩ := by
  unfold Framing.readAllBy
  rw [Framing.foldlM_onChunk_eq_flatten _ _ _ ⟨by simp [Framing.Reader.empty], by simp [Framing.Reader.empty]⟩]
  decide

/-- The engine's reader over a frame of three bytes at a limit of two, cut across three reads:
the frame is skipped, the next one is handed on, and nothing of the skipped frame is kept. -/
theorem witness_skipReader :
    Framing.readAllSkipBy Framing.onChunkSkip 2 [[1, 2], [3], [4, 10, 5, 10, 6]] = ⟨[[5]], [6], false⟩ ∧
    Framing.readAllSkipBy Framing.onChunkSkip 2 [[1, 2], [3]] = ⟨[], [], true⟩ := by
  unfold Framing.readAllSkipBy
  rw [Framing.foldl_onChunkSkip_eq_flatten _ _ _ (Framing.goodSkip_empty 2),
    Framing.foldl_onChunkSkip_eq_flatten _ _ _ (Framing.goodSkip_empty 2)]
  decide

/-! ## Broken models -/

/-- The host forgets to fail on the child's `exit` (engine.ts:61 removed). -/
def hostNoFailOnExit (s : Host.St) : Host.Ev → Option Host.St
  | .childExit => if s.child == .running then some { s with child := .exited } else none
  | e => Host.step s e

theorem mutant_nothingWaits : ¬ Claim.NothingWaitsOnADeadEngine hostNoFailOnExit := by
  intro hc
  have := hc (Host.runBy hostNoFailOnExit Host.init [.readyFrame, .requestTool true, .childExit]).get!
    ⟨[.readyFrame, .requestTool true, .childExit], rfl⟩ rfl rfl
  revert this; decide

/-- The host rejects waiting calls with a contract mismatch when the child exits. -/
def hostMismatchOnExit (s : Host.St) : Host.Ev → Option Host.St
  | .childExit =>
    if s.child == .running then some (Host.fail { s with child := .exited } .mismatch) else none
  | e => Host.step s e

theorem mutant_deadEngineFails : ¬ Claim.DeadEngineFailsToolCallsUnavailable hostMismatchOnExit := by
  intro hc
  have := hc toolWaiting ⟨[.readyFrame, .requestTool true], rfl⟩ rfl rfl ⟨1, 0, .tool⟩ (by decide) rfl
    (Host.fail { toolWaiting with child := .exited } .mismatch) rfl
  revert this; decide

/-- No `exit` listener (engine.ts:32-33 removed): an exit sends no SIGKILL. -/
def hostNoExitHook (s : Host.St) (e : Host.Ev) : Option Host.St :=
  (Host.step s e).map (fun s' => if s'.exit != s.exit then { s' with sigkill := s.sigkill } else s')

theorem mutant_noOrphan : ¬ Claim.NoOrphanWhenHostExits hostNoExitHook := by
  intro hc
  have := hc (Host.runBy hostNoExitHook Host.init [.badFrame false]).get! ⟨[.badFrame false], rfl⟩ 1 rfl
  revert this; decide

/-- The host before P04 (commit 843383e): nothing acts on `engine.failed`, and a `/health`
request that the engine cannot take leaves the `fetch` handler as an unhandled rejection,
which Bun answers with 500. -/
def hostBefore (s : Host.St) : Host.Ev → Option Host.St
  | .failureExit => none
  | .requestHealth =>
    if s.exit.isNone && s.serving then
      if s.stopped || s.pending.length ≥ Host.maxPending then
        some { s with nextTicket := s.nextTicket + 1, done := s.done ++ [⟨s.nextTicket, .health, .http 500⟩] }
      else Host.step s .requestHealth
    else none
  | e => Host.step s e

def engineDiedBefore : Host.St := (Host.runBy hostBefore Host.init [.readyFrame, .childExit]).get!

theorem engineDiedBefore_reachable : Host.ReachableBy hostBefore engineDiedBefore :=
  ⟨[.readyFrame, .childExit], rfl⟩

theorem mutant_health503WhileEngineGone : ¬ Claim.Health503WhileEngineGone hostBefore := by
  intro hc
  have := hc engineDiedBefore engineDiedBefore_reachable rfl rfl rfl rfl
    (hostBefore engineDiedBefore .requestHealth).get! rfl
  revert this; decide

theorem mutant_healthAnswersAreReports : ¬ Claim.HealthAnswersAreReports hostBefore := by
  intro hc
  have := hc (Host.runBy hostBefore Host.init [.readyFrame, .childExit, .requestHealth]).get!
    ⟨[.readyFrame, .childExit, .requestHealth], rfl⟩ ⟨0, .health, .http 500⟩ (by decide) rfl
  revert this; decide

/-- A healthy engine with 32 waiting calls: `/health` met the host's own queue limit. -/
theorem mutant_healthAtFullQueue :
    ((Host.runBy hostBefore Host.init
        (.readyFrame :: List.replicate 32 (.requestTool true) ++ [.requestHealth])).map
      (fun s => (s.stopped, s.pending.length, s.done))) =
      some (false, 32, [⟨32, .health, .http 500⟩]) := by decide

/-- In the host before P04, while the transport serves and nobody asked the host to stop, no
event that needs no client, operator or platform ends the host. -/
theorem before_keeps_serving {s s' : Host.St} {e : Host.Ev} (hsv : s.serving = true)
    (hrd : s.ready = true) (hx : s.exit = none) (hcl : s.closing = false) (hi : e.internal = true)
    (hs : hostBefore s e = some s') :
    s'.serving = true ∧ s'.ready = true ∧ s'.exit = none ∧ s'.closing = false := by
  cases e <;> simp [Host.Ev.internal] at hi <;> simp only [hostBefore, Host.step] at hs
  case readyFrame => simp [hrd] at hs
  case reply id isError =>
    split at hs
    · split at hs <;> simp at hs <;> subst hs <;> simp [hsv, hrd, hx, hcl]
    · simp at hs
  case badFrame typed =>
    split at hs <;> simp at hs; subst hs
    obtain ⟨k1, k2, k3, k4⟩ := Host.fail_keep s (if typed then .mismatch else .plain)
    exact ⟨k1 ▸ hsv, k2 ▸ hrd, (k4 hrd).trans hx, k3 ▸ hcl⟩
  case stdinError =>
    split at hs <;> simp at hs; subst hs
    obtain ⟨k1, k2, k3, k4⟩ := Host.fail_keep s .plain
    exact ⟨k1 ▸ hsv, k2 ▸ hrd, (k4 hrd).trans hx, k3 ▸ hcl⟩
  case startupTimeout => simp [hrd] at hs
  case childExit =>
    split at hs
    · simp only [Option.some.injEq] at hs
      subst hs
      have hxe : ({ s with child := .exited } : Host.St).exit.isNone = true := by simp [hx]
      simp only [hxe, ite_true]
      obtain ⟨k1, k2, k3, k4⟩ := Host.fail_keep { s with child := .exited } .plain
      refine ⟨?_, ?_, ?_, ?_⟩
      · rw [k1]; exact hsv
      · rw [k2]; exact hrd
      · rw [k4 hrd]; exact hx
      · rw [k3]; exact hcl
    · simp at hs
  case childClose =>
    split at hs <;> simp at hs; subst hs; simp [hsv, hrd, hx, hcl]
  case gracefulFires =>
    split at hs <;> simp at hs; subst hs; simp [hsv, hrd, hx, hcl]
  case forcedFires =>
    split at hs <;> simp at hs; subst hs; simp [hsv, hrd, hx, hcl]
  case transportStopped => simp [hcl] at hs
  case loopEmpty => simp [hsv] at hs
  case failureExit => simp at hs

theorem before_never_exits : ∀ (es : List Host.Ev) {s s' : Host.St}, s.serving = true →
    s.ready = true → s.exit = none → s.closing = false → es.all Host.Ev.internal = true →
    Host.runBy hostBefore s es = some s' → s'.exit = none
  | [], s, s', _, _, hx, _, _, hr => by simp [Host.runBy] at hr; subst hr; exact hx
  | e :: es, s, s', hsv, hrd, hx, hcl, hi, hr => by
    simp only [List.all_cons, Bool.and_eq_true] at hi
    simp only [Host.runBy] at hr
    cases hs : hostBefore s e with
    | none => simp [hs] at hr
    | some s1 =>
      simp only [hs, Option.bind_some] at hr
      obtain ⟨h2, h3, h4, h5⟩ := before_keeps_serving hsv hrd hx hcl hi.1 hs
      exact before_never_exits es h2 h3 h4 h5 hi.2 hr

/-- The host before P04 served on with a dead engine: after `[readyFrame, childExit]` nothing
it does by itself ends it. -/
theorem mutant_hostExitsNonZero : ¬ Claim.HostExitsNonZeroWhenEngineDies hostBefore := by
  intro hc
  obtain ⟨es, hi, s', n, hr, hx, _⟩ := hc engineDiedBefore engineDiedBefore_reachable rfl rfl rfl rfl
  have := before_never_exits es (s := engineDiedBefore) rfl rfl rfl rfl hi hr
  rw [this] at hx; simp at hx

/-- `/health` ignores `is_error` (main.ts:61 always 200). -/
def hostHealthIgnoresError (s : Host.St) : Host.Ev → Option Host.St
  | .reply id _ => Host.step s (.reply id false)
  | e => Host.step s e

theorem mutant_health503 : ¬ Claim.Health503OnEngineError hostHealthIgnoresError := by
  intro hc
  have := hc healthWaiting ⟨[.readyFrame, .requestHealth], rfl⟩ rfl rfl ⟨1, 0, .health⟩ (by decide) rfl
    (Host.step healthWaiting (.reply 1 false)).get! rfl
  revert this; decide

/-- The abort handler does not forward a cancel frame (engine.ts:80 removed). -/
def hostNoCancelFrame (s : Host.St) (e : Host.Ev) : Option Host.St :=
  match e with
  | .abort _ => (Host.step s e).map (fun s' => { s' with sent := s.sent })
  | _ => Host.step s e

theorem mutant_cancel : ¬ Claim.CancelCompletesWaitingCall hostNoCancelFrame := by
  intro hc
  obtain ⟨s', hs', _, _, hsent⟩ := hc toolWaiting ⟨[.readyFrame, .requestTool true], rfl⟩ rfl ⟨1, 0, .tool⟩ (by decide)
  have : s' = { (Host.step toolWaiting (.abort 1)).get! with sent := toolWaiting.sent } := by
    have h2 : hostNoCancelFrame toolWaiting (.abort 1) =
        some { (Host.step toolWaiting (.abort 1)).get! with sent := toolWaiting.sent } := rfl
    rw [h2] at hs'; exact (Option.some.inj hs').symm
  subst this
  revert hsent; decide

/-- The engine before P14 and A1 = H3 (commit 843383e): a frame it cannot serve throws out of
the read loop, and a cancel frame only cancels the entry's token, so the entry stays until
its task has ended. -/
def engineBefore (s : Engine.St) : Engine.Ev → Option Engine.St
  | .frame f =>
    if s.loop == .reading then
      match f with
      | .garbage | .oversize | .undecodable _ | .req _ _ false | .cancel _ false => some (Engine.fault s)
      | .cancel id true =>
        some { s with pending := s.pending.map (fun c => if c.id == id then { c with cancelled := true } else c) }
      | .req id m true =>
        if s.pending.length ≥ Engine.maxPending || (Engine.ids s).contains id then some (Engine.fault s)
        else Engine.step s (.frame (.req id m true))
    else none
  | e => Engine.step s e

def oneCallBefore : Engine.St := (Engine.runBy engineBefore Engine.init [.frame (.req 1 .call true)]).get!

theorem oneCallBefore_reachable : Engine.ReachableBy engineBefore oneCallBefore :=
  ⟨[.frame (.req 1 .call true)], rfl⟩

theorem initBefore_reachable : Engine.ReachableBy engineBefore Engine.init := ⟨[], rfl⟩

theorem mutant_badFrameNotFatal : ¬ Claim.BadFrameNotFatal engineBefore := by
  intro hc
  have := hc Engine.init initBefore_reachable rfl .garbage rfl (Engine.fault Engine.init) rfl
  revert this; decide

theorem mutant_badFrameKeepsOtherCalls : ¬ Claim.BadFrameKeepsOtherCalls engineBefore := by
  intro hc
  have := hc oneCallBefore oneCallBefore_reachable rfl .garbage rfl (Engine.fault oneCallBefore) rfl
    ⟨1, .call, .waiting, false⟩ (by decide)
  revert this; decide

theorem mutant_wrongRevision : ¬ Claim.WrongRevisionRefusedBeforeDispatch engineBefore := by
  intro hc
  have := hc Engine.init initBefore_reachable rfl 1 .call (Engine.fault Engine.init) rfl
  revert this; decide

theorem mutant_duplicateOrFull : ¬ Claim.DuplicateOrFullRefusedForThatId engineBefore := by
  intro hc
  have := hc oneCallBefore oneCallBefore_reachable rfl 1 .call (Or.inl (by decide)) (Engine.fault oneCallBefore) rfl
  revert this; decide

theorem mutant_onlyEndOfInput : ¬ Claim.OnlyEndOfInputEndsTheLoop engineBefore := by
  intro hc
  have := hc Engine.init initBefore_reachable rfl (.frame .garbage) (Engine.fault Engine.init) rfl (by decide)
  revert this; decide

/-- With the engine as it was, a caller that goes away at the host's limit ends the engine
(`System.cancelAtFullQueue`): the reproduced finding A1 = H3. -/
theorem mutant_correctHost : ¬ Claim.CorrectHostNeverEndsTheEngine engineBefore := by
  intro hc
  have := hc (System.runBy engineBefore System.init System.cancelAtFullQueue).get!
    ⟨System.cancelAtFullQueue, by decide⟩
  revert this; decide

/-- A call cancelled through `_lifetime` that ends without a reply keeps its `_pending` entry
(the `TryRemove` in the `finally` of `Execute` skipped). -/
def engineDropKeepsEntry (s : Engine.St) : Engine.Ev → Option Engine.St
  | .drop id =>
    match Engine.findCall s id with
    | some c => if c.cancelled then some s else none
    | none => none
  | e => Engine.step s e

/-- In the broken engine, once the loop has ended with call 1 unanswered, nothing reaches the
exit: the entry never leaves `_pending`. -/
theorem mutant_engineExit_stuck : ∀ (es : List Engine.Ev) (s s' : Engine.St),
    s.loop ≠ .reading → s.lifetime = true → s.exit = none →
    (∃ c ∈ s.pending, c.id = 1) →
    Engine.runBy engineDropKeepsEntry s es = some s' → s'.exit = none := by
  intro es
  induction es with
  | nil => intro s s' _ _ hx _ hr; simp [Engine.runBy] at hr; subst hr; exact hx
  | cons e es ih =>
    intro s s' hl hlt hx hex hr
    simp only [Engine.runBy] at hr
    cases hs : engineDropKeepsEntry s e with
    | none => simp [hs] at hr
    | some s1 =>
      simp only [hs, Option.bind_some] at hr
      obtain ⟨c1, hc1, hc1id⟩ := hex
      have hreading : (s.loop == .reading) = false := by simpa using hl
      cases e with
      | frame f => simp [engineDropKeepsEntry, Engine.step, hreading] at hs
      | eof => simp [engineDropKeepsEntry, Engine.step, hreading] at hs
      | eofPartial => simp [engineDropKeepsEntry, Engine.step, hreading] at hs
      | stop =>
        simp only [engineDropKeepsEntry, Engine.step, hx, Option.isNone_none, ite_true,
          Option.some.injEq] at hs
        subst hs
        apply ih _ s' _ _ _ _ hr
        · simp [Engine.cancelAll, hreading, hl]
        · simp [Engine.cancelAll]
        · simp only [Engine.cancelAll]
        · exact ⟨{ c1 with cancelled := true }, by simp [Engine.cancelAll]; exact ⟨c1, hc1, by simp⟩, hc1id⟩
      | enter id =>
        simp only [engineDropKeepsEntry, Engine.step] at hs
        split at hs
        · split at hs
          · simp only [Option.some.injEq] at hs
            subst hs
            apply ih _ s' (by simpa using hl) (by simpa using hlt) (by simpa using hx) ?_ hr
            refine ⟨if c1.id == id then { c1 with phase := .running } else c1, ?_, ?_⟩
            · simp only [Engine.setPhase, List.mem_map]; exact ⟨c1, hc1, rfl⟩
            · split <;> simp [hc1id]
          · simp at hs
        · simp at hs
      | respond id isError =>
        simp only [engineDropKeepsEntry, Engine.step] at hs
        split at hs
        · split at hs
          · rename_i hcond; simp [hlt] at hcond
          · simp at hs
        · simp at hs
      | drop id =>
        simp only [engineDropKeepsEntry] at hs
        split at hs
        · split at hs
          · simp only [Option.some.injEq] at hs; subst hs
            exact ih _ s' hl hlt hx ⟨c1, hc1, hc1id⟩ hr
          · simp at hs
        · simp at hs
      | unwindEnter =>
        simp only [engineDropKeepsEntry, Engine.step] at hs
        split at hs
        · simp only [Option.some.injEq] at hs; subst hs
          apply ih _ s' (by simpa using hl) (by simpa using hlt) (by simpa using hx) ?_ hr
          exact ⟨c1, hc1, hc1id⟩
        · simp at hs
      | unwind worker =>
        simp only [engineDropKeepsEntry, Engine.step] at hs
        split at hs
        · split at hs
          · simp only [Option.some.injEq] at hs; subst hs
            apply ih _ s' (by simpa using hl) (by simpa using hlt) (by simpa using hx) ?_ hr
            exact ⟨c1, hc1, hc1id⟩
          · simp at hs
        · split at hs
          · simp only [Option.some.injEq] at hs; subst hs
            apply ih _ s' (by simpa using hl) (by simpa using hlt) (by simpa using hx) ?_ hr
            exact ⟨c1, hc1, hc1id⟩
          · simp at hs
      | exitProcess =>
        simp only [engineDropKeepsEntry, Engine.step] at hs
        split at hs
        · rename_i hcond
          have : s.pending ≠ [] := by intro h; rw [h] at hc1; simp at hc1
          simp [this] at hcond
        · simp at hs

theorem mutant_engineExit : ¬ Claim.EngineCanAlwaysExitAfterTheLoopEnds engineDropKeepsEntry := by
  intro hc
  let s0 := (Engine.runBy engineDropKeepsEntry Engine.init [.frame (.req 1 .call true), .eof]).get!
  obtain ⟨es, s', hr, hx⟩ := hc s0 ⟨[.frame (.req 1 .call true), .eof], rfl⟩ (by decide)
  have := mutant_engineExit_stuck es s0 s' (by decide) rfl rfl ⟨⟨1, .call, .waiting, true⟩, by decide, rfl⟩ hr
  rw [this] at hx; simp at hx

/-- A reader that forgets the bytes it kept between reads. -/
def onChunkNoKeep (lim : Nat) (rd : Framing.Reader) (data : List Nat) : Option Framing.Reader :=
  (Framing.drain lim data).map (fun p => ⟨rd.done ++ p.1, p.2⟩)

theorem drain_bytes (lim : Nat) (buf : List Nat) :
    Framing.drain lim buf =
      (buf.foldlM (Framing.byteStep lim) Framing.Reader.empty).map (fun rd => (rd.done, rd.buffer)) :=
  Framing.drain_eq_bytes lim (buf.length + 1) buf (by omega)

theorem drainSkip_bytes (lim : Nat) (sk : Bool) (buf : List Nat) :
    Framing.drainSkip lim sk buf =
      ((buf.foldl (Framing.skipByte lim) ⟨[], [], sk⟩).done, (buf.foldl (Framing.skipByte lim) ⟨[], [], sk⟩).buffer,
        (buf.foldl (Framing.skipByte lim) ⟨[], [], sk⟩).skipping) :=
  Framing.drainSkip_eq_bytes lim (buf.length + 1) buf sk (by omega)

/-- An engine reader that forgets that it is skipping when a read ends: the rest of the long
frame is taken for a frame of its own. -/
def onChunkSkipForgets (lim : Nat) (rd : Framing.SkipReader) (data : List Nat) : Framing.SkipReader :=
  let p := Framing.drainSkip lim false (rd.buffer ++ data)
  ⟨rd.done ++ p.1, p.2.1, p.2.2⟩

theorem mutant_engineFraming : ¬ Claim.EngineReadsKeepFrameIdentity onChunkSkipForgets := by
  intro hc
  have := hc 2 [[1, 2, 3], [4, 10]]
  simp only [Framing.readAllSkipBy, List.foldl, onChunkSkipForgets, drainSkip_bytes] at this
  revert this; decide

/-- An engine reader that keeps the bytes of the frame it skips. -/
def onChunkSkipBuffers (lim : Nat) (rd : Framing.SkipReader) (data : List Nat) : Framing.SkipReader :=
  let p := Framing.drainSkip lim rd.skipping (rd.buffer ++ data)
  ⟨rd.done ++ p.1, if p.2.2 then rd.buffer ++ data else p.2.1, p.2.2⟩

theorem mutant_engineReaderKeeps : ¬ Claim.EngineReaderKeepsAtMostTheLimit onChunkSkipBuffers := by
  intro hc
  have := hc 2 [[1, 2, 3]]
  simp only [Framing.readAllSkipBy, List.foldl, onChunkSkipBuffers, drainSkip_bytes] at this
  revert this; decide

theorem mutant_framing : ¬ Claim.ReadsKeepFrameIdentity onChunkNoKeep := by
  intro hc
  have := hc 1048576 [[1], [10]]
  simp only [Framing.readAllBy, List.foldlM, onChunkNoKeep, drain_bytes] at this
  revert this; decide

end NamiProofs.HostEngine.Mutants
