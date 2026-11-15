import NamiProofs.HostEngine.Spec
import NamiProofs.HostEngine.Proofs.HostRefute
import NamiProofs.HostEngine.Proofs.Engine
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

/-- A frame cut across reads and two frames in one read. -/
theorem witness_framing :
    Framing.readAllBy Framing.onChunk 1048576 [[1, 2], [3, 10, 4], [10]] =
      some ⟨[[1, 2, 3], [4]], []⟩ := by
  unfold Framing.readAllBy
  rw [Framing.foldlM_onChunk_eq_flatten _ _ _ ⟨by simp [Framing.Reader.empty], by simp [Framing.Reader.empty]⟩]
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

/-- A cancelled call that ends without a reply keeps its `_pending` entry (the `TryRemove` in
EngineProtocol.cs:173 skipped on that path). -/
def engineDropKeepsEntry (s : Engine.St) : Engine.Ev → Option Engine.St
  | .drop id =>
    match Engine.findCall s id with
    | some c => if c.cancelled && c.phase != .replied then some s else none
    | none => none
  | e => Engine.step s e

/-- In the broken engine, once the loop has ended with call 1 unanswered, nothing reaches the
exit: the entry never leaves `_pending`. -/
theorem mutant_engineExit_stuck : ∀ (es : List Engine.Ev) (s s' : Engine.St),
    s.loop ≠ .reading → s.lifetime = true → s.exit = none →
    (∃ c ∈ s.pending, c.id = 1) → (∀ c ∈ s.pending, c.id = 1 → c.phase ≠ .replied) →
    Engine.runBy engineDropKeepsEntry s es = some s' → s'.exit = none := by
  intro es
  induction es with
  | nil => intro s s' _ _ hx _ _ hr; simp [Engine.runBy] at hr; subst hr; exact hx
  | cons e es ih =>
    intro s s' hl hlt hx hex hall hr
    simp only [Engine.runBy] at hr
    cases hs : engineDropKeepsEntry s e with
    | none => simp [hs] at hr
    | some s1 =>
      simp only [hs, Option.bind_some] at hr
      have hfind : ∀ {id c}, Engine.findCall s id = some c → c ∈ s.pending ∧ c.id = id := by
        intro id c h
        exact ⟨List.mem_of_find?_eq_some h, by simpa using List.find?_some h⟩
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
        apply ih _ s' _ _ _ _ _ hr
        · simp [Engine.cancelAll, hreading, hl]
        · simp [Engine.cancelAll]
        · simp only [Engine.cancelAll]
        · exact ⟨{ c1 with cancelled := true }, by simp [Engine.cancelAll]; exact ⟨c1, hc1, by simp⟩, hc1id⟩
        · intro c hc hcid
          simp [Engine.cancelAll] at hc
          obtain ⟨d, hd, rfl⟩ := hc
          exact hall d hd hcid
      | enter id =>
        simp only [engineDropKeepsEntry, Engine.step] at hs
        split at hs
        · rename_i c hfc
          split at hs
          · simp only [Option.some.injEq] at hs
            subst hs
            apply ih _ s' (by simpa using hl) (by simpa using hlt) (by simpa using hx) ?_ ?_ hr
            · refine ⟨if c1.id == id then { c1 with phase := .running } else c1, ?_, ?_⟩
              · simp only [Engine.setPhase, List.mem_map]; exact ⟨c1, hc1, rfl⟩
              · split <;> simp [hc1id]
            · intro c hc hcid
              simp only [Engine.setPhase, List.mem_map] at hc
              obtain ⟨d, hd, rfl⟩ := hc
              by_cases hdi : d.id = id
              · simp [hdi]
              · have hne : (d.id == id) = false := by simpa using hdi
                simp only [hne, Bool.false_eq_true, ite_false] at hcid ⊢
                exact hall d hd hcid
          · simp at hs
        · simp at hs
      | respond id isError =>
        simp only [engineDropKeepsEntry, Engine.step] at hs
        split at hs
        · split at hs
          · rename_i hcond; simp [hlt] at hcond
          · simp at hs
        · simp at hs
      | release id =>
        simp only [engineDropKeepsEntry, Engine.step] at hs
        split at hs
        · rename_i c hfc
          split at hs
          · rename_i hph
            simp only [Option.some.injEq] at hs
            subst hs
            obtain ⟨hcm, hcid⟩ := hfind hfc
            have hid : id ≠ 1 := by
              intro h1; subst h1
              exact hall c hcm hcid (by simpa using hph)
            apply ih _ s' (by simpa using hl) (by simpa using hlt) (by simpa using hx) ?_ ?_ hr
            · refine ⟨c1, ?_, hc1id⟩
              simp only [Engine.remove, List.mem_filter]
              refine ⟨hc1, ?_⟩
              simp [hc1id]; omega
            · intro c' hc' hcid'
              exact hall c' (List.mem_filter.mp hc').1 hcid'
          · simp at hs
        · simp at hs
      | drop id =>
        simp only [engineDropKeepsEntry] at hs
        split at hs
        · split at hs
          · simp only [Option.some.injEq] at hs; subst hs
            exact ih _ s' hl hlt hx ⟨c1, hc1, hc1id⟩ hall hr
          · simp at hs
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
  have := mutant_engineExit_stuck es s0 s' (by decide) rfl rfl ⟨⟨1, .call, .waiting, true⟩, by decide, rfl⟩
    (by decide) hr
  rw [this] at hx; simp at hx

/-- A reader that forgets the bytes it kept between reads. -/
def onChunkNoKeep (lim : Nat) (rd : Framing.Reader) (data : List Nat) : Option Framing.Reader :=
  (Framing.drain lim data).map (fun p => ⟨rd.done ++ p.1, p.2⟩)

theorem drain_bytes (lim : Nat) (buf : List Nat) :
    Framing.drain lim buf =
      (buf.foldlM (Framing.byteStep lim) Framing.Reader.empty).map (fun rd => (rd.done, rd.buffer)) :=
  Framing.drain_eq_bytes lim (buf.length + 1) buf (by omega)

theorem mutant_framing : ¬ Claim.ReadsKeepFrameIdentity onChunkNoKeep := by
  intro hc
  have := hc 1048576 [[1], [10]]
  simp only [Framing.readAllBy, List.foldlM, onChunkNoKeep, drain_bytes] at this
  revert this; decide

end NamiProofs.HostEngine.Mutants
