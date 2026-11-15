import NamiStorage.Queue.Theorems
/-
The writer is exclusive: while one job holds it, no other transaction commits. This is the fact
behind both P13 refutations.
-/
namespace Nami.Storage.Queue

/-- Only `commit` changes the commit log, and it appends the running job. -/
theorem step_db {s s' : State} {e : Event} (h : step s e = some s') :
    s'.db = s.db ∨ ∃ j, (∃ n, s.writer = .running j n false) ∧ s'.db = s.db ++ [j] ∧
      s'.writer = .running j 0 true := by
  cases e with
  | commit =>
      simp only [step] at h
      split at h
      · rename_i j hw
        split at h
        · cases h
        · cases h; exact Or.inr ⟨j, ⟨0, hw⟩, rfl, rfl⟩
      · cases h
  | begin k w => simp only [step, Option.some.injEq] at h; subst h; exact Or.inl rfl
  | tryWrite j0 =>
      simp only [step] at h
      split at h
      · cases h
      · split at h
        · cases h; exact Or.inl rfl
        · split at h
          · cases h; exact Or.inl rfl
          · split at h
            · cases h
            · cases h; exact Or.inl rfl
  | arm j0 =>
      simp only [step] at h; split at h
      · cases h; exact Or.inl rfl
      · cases h
  | cancel j0 =>
      simp only [step] at h; split at h
      · cases h
      · cases h; exact Or.inl rfl
  | dequeue =>
      simp only [step] at h
      split at h
      · split at h
        · split at h
          · split at h
            · cases h; exact Or.inl rfl
            · cases h; exact Or.inl rfl
          · cases h; exact Or.inl rfl
          · cases h; exact Or.inl rfl
        · cases h; exact Or.inl rfl
      · cases h
  | progress =>
      simp only [step] at h; split at h
      · split at h
        · cases h
        · cases h; exact Or.inl rfl
      · cases h
  | finish =>
      simp only [step] at h; split at h
      · cases h; exact Or.inl rfl
      · cases h
  | abort =>
      simp only [step] at h; split at h
      · split at h
        · cases h; exact Or.inl rfl
        · cases h
      · cases h
  | fail =>
      simp only [step] at h; split at h
      · cases h; exact Or.inl rfl
      · cases h
  | dispose =>
      simp only [step] at h; split at h
      · cases h
      · cases h; exact Or.inl rfl
  | complete =>
      simp only [step] at h; split at h
      · cases h; exact Or.inl rfl
      · cases h
  | shutdownCancel =>
      simp only [step] at h; split at h
      · cases h; exact Or.inl rfl
      · cases h
  | exit =>
      simp only [step] at h; split at h
      · split at h
        · cases h; exact Or.inl rfl
        · cases h
      · cases h

/-- The writer starts a job only by taking it from the head of the queue. -/
theorem step_writer_start {s s' : State} {e : Event} (h : step s e = some s') {j n c}
    (hw' : s'.writer = .running j n c) :
    (∃ n0 c0, s.writer = .running j n0 c0) ∨ (s.writer = .idle ∧ ∃ rest, s.queue = j :: rest) := by
  cases e with
  | dequeue =>
      simp only [step] at h
      split at h
      · rename_i j0 rest hw hq
        split at h
        · split at h
          · split at h
            · cases h; cases hw'; exact Or.inr ⟨hw, rest, hq⟩
            · cases h; rw [hw] at hw'; cases hw'
          · cases h; cases hw'; exact Or.inr ⟨hw, rest, hq⟩
          · cases h; cases hw'; exact Or.inr ⟨hw, rest, hq⟩
        · cases h; rw [hw] at hw'; cases hw'
      · cases h
  | begin k w =>
      simp only [step, Option.some.injEq] at h; subst h; exact Or.inl ⟨n, c, hw'⟩
  | tryWrite j0 =>
      simp only [step] at h
      split at h
      · cases h
      · split at h
        · cases h; exact Or.inl ⟨n, c, hw'⟩
        · split at h
          · cases h; exact Or.inl ⟨n, c, hw'⟩
          · split at h
            · cases h
            · cases h; exact Or.inl ⟨n, c, hw'⟩
  | arm j0 =>
      simp only [step] at h; split at h
      · cases h; exact Or.inl ⟨n, c, hw'⟩
      · cases h
  | cancel j0 =>
      simp only [step] at h; split at h
      · cases h
      · cases h; exact Or.inl ⟨n, c, hw'⟩
  | progress =>
      simp only [step] at h; split at h
      · rename_i j1 m hw
        split at h
        · cases h
        · cases h; cases hw'; exact Or.inl ⟨_, _, hw⟩
      · cases h
  | commit =>
      simp only [step] at h; split at h
      · rename_i j1 hw
        split at h
        · cases h
        · cases h; cases hw'; exact Or.inl ⟨_, _, hw⟩
      · cases h
  | finish =>
      simp only [step] at h; split at h
      · cases h; cases hw'
      · cases h
  | abort =>
      simp only [step] at h; split at h
      · split at h
        · cases h; cases hw'
        · cases h
      · cases h
  | fail =>
      simp only [step] at h; split at h
      · cases h; cases hw'
      · cases h
  | dispose =>
      simp only [step] at h; split at h
      · cases h
      · cases h; exact Or.inl ⟨n, c, hw'⟩
  | complete =>
      simp only [step] at h; split at h
      · cases h; exact Or.inl ⟨n, c, hw'⟩
      · cases h
  | shutdownCancel =>
      simp only [step] at h; split at h
      · cases h; exact Or.inl ⟨n, c, hw'⟩
      · cases h
  | exit =>
      simp only [step] at h; split at h
      · split at h
        · cases h; cases hw'
        · cases h
      · cases h

/-- A job the writer has taken never returns to the queue. -/
theorem taken_stays {s s' : State} (hi : Inv s) {e : Event} (h : step s e = some s') {r : Nat}
    (hr : (s.job r).stage = .taken) : (s'.job r).stage = .taken := by
  have hnew : r ≠ s.nextId := Nat.ne_of_lt (hi.lt_of_stage (by rw [hr]; decide))
  cases e with
  | begin k w =>
      simp only [step, Option.some.injEq] at h; subst h; dsimp only; rw [upd_other _ _ hnew]; exact hr
  | tryWrite j0 =>
      simp only [step] at h
      split at h
      · cases h
      · rename_i hb
        have hb' : (s.job j0).stage = .begun := by simpa using hb
        have hj0 : r ≠ j0 := fun e => by rw [e, hb'] at hr; cases hr
        split at h
        · cases h; dsimp only; rw [upd_other _ _ hj0]; exact hr
        · split at h
          · cases h; dsimp only; rw [upd_other _ _ hj0]; exact hr
          · split at h
            · cases h
            · cases h; dsimp only; rw [upd_other _ _ hj0]; exact hr
  | arm j0 =>
      simp only [step] at h; split at h
      · have hnew' : s' = { s with job := upd s.job j0 (armed (s.job j0)) } := by cases h; rfl
        subst hnew'; dsimp only
        by_cases hj : r = j0
        · subst hj; rw [upd_same, armed_stage]; exact hr
        · rw [upd_other _ _ hj]; exact hr
      · cases h
  | cancel j0 =>
      simp only [step] at h; split at h
      · cases h
      · have hnew' : s' = { s with job := upd s.job j0 (cancelled (s.job j0)) } := by cases h; rfl
        subst hnew'; dsimp only
        by_cases hj : r = j0
        · subst hj; rw [upd_same, cancelled_stage _ (by rw [hr]; intro h; cases h.1)]; exact hr
        · rw [upd_other _ _ hj]; exact hr
  | dequeue =>
      simp only [step] at h
      split at h
      · rename_i j0 rest hw hq
        split at h
        · split at h
          · split at h
            · cases h; dsimp only
              by_cases hj : r = j0
              · subst hj; rw [upd_same]
              · rw [upd_other _ _ hj]; exact hr
            · cases h; dsimp only
              by_cases hj : r = j0
              · subst hj; rw [upd_same]
              · rw [upd_other _ _ hj]; exact hr
          · cases h; dsimp only
            by_cases hj : r = j0
            · subst hj; rw [upd_same]
            · rw [upd_other _ _ hj]; exact hr
          · cases h; dsimp only
            by_cases hj : r = j0
            · subst hj; rw [upd_same]
            · rw [upd_other _ _ hj]; exact hr
        · cases h; dsimp only
          by_cases hj : r = j0
          · subst hj; rw [upd_same]
          · rw [upd_other _ _ hj]; exact hr
      · cases h
  | progress =>
      simp only [step] at h; split at h
      · split at h
        · cases h
        · cases h; exact hr
      · cases h
  | commit =>
      simp only [step] at h; split at h
      · split at h
        · cases h
        · cases h; exact hr
      · cases h
  | finish =>
      simp only [step] at h; split at h
      · rename_i j0 _
        cases h; dsimp only
        by_cases hj : r = j0
        · subst hj; rw [upd_same]; exact hr
        · rw [upd_other _ _ hj]; exact hr
      · cases h
  | abort =>
      simp only [step] at h; split at h
      · rename_i j0 _ _
        split at h
        · cases h; dsimp only
          by_cases hj : r = j0
          · subst hj; rw [upd_same]; exact hr
          · rw [upd_other _ _ hj]; exact hr
        · cases h
      · cases h
  | fail =>
      simp only [step] at h; split at h
      · rename_i j0 _ _
        cases h; dsimp only
        by_cases hj : r = j0
        · subst hj; rw [upd_same]; exact hr
        · rw [upd_other _ _ hj]; exact hr
      · cases h
  | dispose =>
      simp only [step] at h; split at h
      · cases h
      · cases h; exact hr
  | complete =>
      simp only [step] at h; split at h
      · cases h; exact hr
      · cases h
  | shutdownCancel =>
      simp only [step] at h; split at h
      · cases h; exact hr
      · cases h
  | exit =>
      simp only [step] at h; split at h
      · split at h
        · cases h; dsimp only
          by_cases hm : r ∈ s.queue
          · simp only [hm, ite_true]; simp [drainOne]
          · simp only [hm, ite_false]; exact hr
        · cases h
      · cases h

theorem writerHeldExclusively_aux {cap : Nat} {s t : State} (hs : Reachable cap s) (hst : Steps s t)
    {r n c} (hw : s.writer = .running r n c) :
    (t.job r).stage = .taken ∧ ∀ n' c', t.writer = .running r n' c' → ∀ k ∈ t.db, k ∈ s.db ∨ k = r := by
  induction hst with
  | refl =>
      refine ⟨((inv_reachable hs).run r n c hw).2.2.1, fun _ _ _ k hk => Or.inl hk⟩
  | tail e hpath hstep ih =>
      rename_i t u
      obtain ⟨htaken, hdb⟩ := ih
      have hti : Inv t := inv_reachable (stepsBy_reachable hs hpath)
      refine ⟨taken_stays hti hstep htaken, ?_⟩
      intro n' c' hw' k hk
      rcases step_writer_start hstep hw' with ⟨n0, c0, hw0⟩ | ⟨hidle, rest, hq⟩
      · rcases step_db hstep with e | ⟨j, ⟨m, hwj⟩, hdbu, _⟩
        · rw [e] at hk; exact hdb n0 c0 hw0 k hk
        · rw [hdbu, List.mem_append] at hk
          rcases hk with hk | hk
          · exact hdb n0 c0 hw0 k hk
          · simp at hk; subst hk; rw [hw0] at hwj; cases hwj; exact Or.inr rfl
      · have hq' : (t.job r).stage = .queued := (hti.qstage r).1 (by rw [hq]; simp)
        rw [htaken] at hq'; cases hq'

end Nami.Storage.Queue

namespace Nami.Storage
open Queue

theorem writerHeldExclusively : Spec.WriterHeldExclusively := by
  intro cap s t hs hst r n c n' c' hw hw' k hk
  exact (writerHeldExclusively_aux hs hst hw).2 n' c' hw' k hk

end Nami.Storage
