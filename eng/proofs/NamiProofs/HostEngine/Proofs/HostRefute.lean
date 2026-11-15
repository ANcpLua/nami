import NamiProofs.HostEngine.Spec
import NamiProofs.HostEngine.Proofs.Host

/-! Counterexamples for the host claims that fail on the code as it is. -/

namespace NamiProofs.HostEngine.Host

instance : Inhabited St := ⟨{}⟩

/-- The engine dies right after readiness: `[readyFrame, childExit]`. -/
def engineDied : St := (run init [.readyFrame, .childExit]).get!

theorem engineDied_reachable : Reachable engineDied := ⟨[.readyFrame, .childExit], rfl⟩

/-- After readiness, while the transport serves and nobody asked the host to stop, no event
that needs no client, operator or platform ends the host. -/
theorem internal_keeps_serving {s s' : St} {e : Ev} (h : Inv s) (hsv : s.serving = true)
    (hrd : s.ready = true) (hx : s.exit = none) (hcl : s.closing = false) (hi : e.internal = true)
    (hs : step s e = some s') :
    Inv s' ∧ s'.serving = true ∧ s'.ready = true ∧ s'.exit = none ∧ s'.closing = false := by
  refine ⟨inv_step h hs, ?_⟩
  cases e <;> simp [Ev.internal] at hi <;> simp only [step] at hs
  case readyFrame => simp [hrd] at hs
  case reply id isError =>
    split at hs
    · split at hs <;> simp at hs <;> subst hs <;> simp [hsv, hrd, hx, hcl]
    · simp at hs
  case badFrame typed =>
    split at hs <;> simp at hs; subst hs
    obtain ⟨k1, k2, k3, k4⟩ := fail_keep s (if typed then .mismatch else .plain)
    exact ⟨k1 ▸ hsv, k2 ▸ hrd, (k4 hrd).trans hx, k3 ▸ hcl⟩
  case stdinError =>
    split at hs <;> simp at hs; subst hs
    obtain ⟨k1, k2, k3, k4⟩ := fail_keep s .plain
    exact ⟨k1 ▸ hsv, k2 ▸ hrd, (k4 hrd).trans hx, k3 ▸ hcl⟩
  case startupTimeout => simp [hrd] at hs
  case childExit =>
    split at hs
    · simp only [Option.some.injEq] at hs
      subst hs
      have hxe : ({ s with child := .exited } : St).exit.isNone = true := by simp [hx]
      simp only [hxe, ite_true]
      obtain ⟨k1, k2, k3, k4⟩ := fail_keep { s with child := .exited } .plain
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

theorem internal_never_exits : ∀ (es : List Ev) {s s' : St}, Inv s → s.serving = true →
    s.ready = true → s.exit = none → s.closing = false → es.all Ev.internal = true →
    runBy step s es = some s' → s'.exit = none
  | [], s, s', _, _, _, hx, _, _, hr => by simp [runBy] at hr; subst hr; exact hx
  | e :: es, s, s', h, hsv, hrd, hx, hcl, hi, hr => by
    simp only [List.all_cons, Bool.and_eq_true] at hi
    simp only [runBy] at hr
    cases hs : step s e with
    | none => simp [hs] at hr
    | some s1 =>
      simp only [hs, Option.bind_some] at hr
      obtain ⟨h1, h2, h3, h4, h5⟩ := internal_keeps_serving h hsv hrd hx hcl hi.1 hs
      exact internal_never_exits es h1 h2 h3 h4 h5 hi.2 hr

/-- Counterexample: after `[readyFrame, childExit]` no event without a client, operator or
platform ends the host; it serves with a dead engine. -/
theorem hostExitsNonZeroWhenEngineDies_refuted : ¬ Claim.HostExitsNonZeroWhenEngineDies step := by
  intro hc
  obtain ⟨es, hi, s', n, hr, hx, _⟩ :=
    hc engineDied engineDied_reachable rfl rfl rfl rfl
  have := internal_never_exits es (inv_reachable engineDied_reachable) rfl rfl rfl rfl hi hr
  rw [this] at hx; simp at hx

/-- `GET /health` after the engine died: answered 500, not 503. -/
def healthAfterDeath : St := (step engineDied .requestHealth).get!

theorem healthAfterDeath_done : healthAfterDeath.done = [⟨0, .health, .http 500⟩] := by decide

theorem health503WhileEngineGone_refuted : ¬ Claim.Health503WhileEngineGone step := by
  intro hc
  have := hc engineDied engineDied_reachable rfl rfl rfl rfl healthAfterDeath rfl
  revert this
  decide

theorem healthAnswersAreReports_refuted : ¬ Claim.HealthAnswersAreReports step := by
  intro hc
  have hr : Reachable healthAfterDeath := ⟨[.readyFrame, .childExit, .requestHealth], rfl⟩
  have := hc healthAfterDeath hr ⟨0, .health, .http 500⟩ (by decide) rfl
  revert this
  decide

/-- Second counterexample to `HealthAnswersAreReports`: a healthy engine with 32 waiting
calls; `GET /health` meets the host's own queue limit (engine.ts:69) and gets 500. -/
def fullQueueHealth : List Ev := .readyFrame :: List.replicate 32 (.requestTool true) ++ [.requestHealth]

theorem fullQueueHealth_answers_500 :
    ((run init fullQueueHealth).map (fun s => (s.stopped, s.child, s.pending.length, s.done))) =
      some (false, .running, 32, [⟨32, .health, .http 500⟩]) := by decide

end NamiProofs.HostEngine.Host
