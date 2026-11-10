import ClassicAsyncSplice.StoreRemoval

/-!
# Classic async splice equivalence

`classic-async-reconstruction.md` (General inverse) replaces Roslyn's classic
async state machine by the sequential body it was lowered from: the protocol
roles (dispatch, suspension, resume landing, completion, finally guard) are
removed and every state test is evaluated under the running state `-1`.

This module models the lowered machine and the spliced body over one small
body grammar and proves that, for every body with distinct state constants and
every sequence of suspension choices, the machine's user-effect trace equals
the sequential body's trace. The fold under `-1` is built into the machine
model: the only state tests are the dispatch that skips to a landing and the
finally guard that skips a finally body when the machine returns suspended.
-/

namespace ClassicAsyncSplice

/--
A user effect is an observation; an await is a suspension point carrying its
state constant; a `tryFinally` guards a body whose awaits may suspend with a
finally block of plain effects (an await inside a finally is outside the stage
2 domain and declines).
-/
inductive Stmt where
  | effect (e : Nat)
  | await (state : Nat)
  | tryFinally (body : List Stmt) (finallyEffects : List Nat)

/-- The await state constants of a body, in program order. -/
def awaits : List Stmt → List Nat
  | [] => []
  | .effect _ :: rest => awaits rest
  | .await s :: rest => s :: awaits rest
  | .tryFinally body _ :: rest => awaits body ++ awaits rest

/-- Distinct state constants: Roslyn numbers every suspension point once. -/
def Distinct : List Nat → Prop
  | [] => True
  | x :: xs => x ∉ xs ∧ Distinct xs

/-- The spliced body: awaits are suspension points with no effect of their own. -/
def runSeq : List Stmt → List Nat
  | [] => []
  | .effect e :: rest => e :: runSeq rest
  | .await _ :: rest => runSeq rest
  | .tryFinally body fin :: rest => runSeq body ++ fin ++ runSeq rest

/--
One `MoveNext` call ends either by completing (the builder's `SetResult`) or
by suspending at a state constant after storing it; each carries the schedule
of suspension choices still unconsumed.
-/
inductive Outcome where
  | completed (schedule : List Bool)
  | suspended (state : Nat) (schedule : List Bool)

/--
The machine in running mode. An `await` consumes one choice: `true` means the
awaiter had completed and execution continues; `false` means the machine stores
the state and returns, and the return passes through every enclosing finally
with the guard `state < 0` false, so no finally effect runs. An exhausted
schedule completes synchronously.
-/
def runStmts : List Stmt → List Bool → List Nat × Outcome
  | [], sched => ([], .completed sched)
  | .effect e :: rest, sched =>
      let r := runStmts rest sched
      (e :: r.1, r.2)
  | .await s :: rest, sched =>
      match sched with
      | false :: sched' => ([], .suspended s sched')
      | true :: sched' => runStmts rest sched'
      | [] => runStmts rest []
  | .tryFinally body fin :: rest, sched =>
      match runStmts body sched with
      | (t, .completed sched') =>
          let r := runStmts rest sched'
          (t ++ fin ++ r.1, r.2)
      | (t, .suspended s sched') => (t, .suspended s sched')

/--
The machine re-entered with a stored state: dispatch skips every statement
before the matching await, re-dispatching at the entry of each exception
region that contains it, resets the state to `-1` at the landing, and runs on.
-/
def resumeStmts : List Stmt → Nat → List Bool → List Nat × Outcome
  | [], _, sched => ([], .completed sched)
  | .effect _ :: rest, s, sched => resumeStmts rest s sched
  | .await s' :: rest, s, sched =>
      if s' = s then runStmts rest sched else resumeStmts rest s sched
  | .tryFinally body fin :: rest, s, sched =>
      if s ∈ awaits body then
        match resumeStmts body s sched with
        | (t, .completed sched') =>
            let r := runStmts rest sched'
            (t ++ fin ++ r.1, r.2)
        | (t, .suspended s2 sched') => (t, .suspended s2 sched')
      else
        resumeStmts rest s sched

/-- Drive the machine to completion; every suspension consumed one choice, so the schedule length bounds the re-entries. -/
def drive (stmts : List Stmt) : Nat → List Nat × Outcome → List Nat
  | _, (t, .completed _) => t
  | 0, (t, .suspended _ _) => t
  | fuel + 1, (t, .suspended s sched) => t ++ drive stmts fuel (resumeStmts stmts s sched)

/-- The lowered machine's complete user-effect trace. -/
def runMachine (stmts : List Stmt) (sched : List Bool) : List Nat :=
  drive stmts sched.length (runStmts stmts sched)

/--
The statements the machine still has to run after resuming at `s`: the rest of
the innermost body, then that region's finally effects (now unguarded, because
the state is `-1` again), then whatever followed the region.
-/
def after : List Stmt → Nat → List Stmt
  | [], _ => []
  | .effect _ :: rest, s => after rest s
  | .await s' :: rest, s => if s' = s then rest else after rest s
  | .tryFinally body fin :: rest, s =>
      if s ∈ awaits body then after body s ++ fin.map .effect ++ rest
      else after rest s

/-! ## Bookkeeping lemmas -/

theorem awaits_append (xs ys : List Stmt) :
    awaits (xs ++ ys) = awaits xs ++ awaits ys := by
  induction xs with
  | nil => simp [awaits]
  | cons x rest ih =>
    cases x <;> simp [awaits, ih, List.append_assoc]

theorem awaits_map_effect (fin : List Nat) : awaits (fin.map Stmt.effect) = [] := by
  induction fin with
  | nil => simp [awaits]
  | cons f rest ih => simp [awaits, ih]

theorem runSeq_append (xs ys : List Stmt) :
    runSeq (xs ++ ys) = runSeq xs ++ runSeq ys := by
  induction xs with
  | nil => simp [runSeq]
  | cons x rest ih =>
    cases x <;> simp [runSeq, ih, List.append_assoc]

theorem runSeq_map_effect (fin : List Nat) : runSeq (fin.map Stmt.effect) = fin := by
  induction fin with
  | nil => simp [runSeq]
  | cons f rest ih => simp [runSeq, ih]

theorem distinct_append_left {xs ys : List Nat} (h : Distinct (xs ++ ys)) : Distinct xs := by
  induction xs with
  | nil => trivial
  | cons x rest ih =>
    simp only [List.cons_append, Distinct] at h
    exact ⟨fun hx => h.1 (List.mem_append_left _ hx), ih h.2⟩

theorem distinct_append_right {xs ys : List Nat} (h : Distinct (xs ++ ys)) : Distinct ys := by
  induction xs with
  | nil => simpa using h
  | cons x rest ih =>
    simp only [List.cons_append, Distinct] at h
    exact ih h.2

theorem distinct_append_disjoint {xs ys : List Nat} (h : Distinct (xs ++ ys)) {a : Nat}
    (ha : a ∈ xs) : a ∉ ys := by
  induction xs with
  | nil => cases ha
  | cons x rest ih =>
    simp only [List.cons_append, Distinct] at h
    rcases List.mem_cons.mp ha with rfl | hrest
    · exact fun hy => h.1 (List.mem_append_right _ hy)
    · exact ih h.2 hrest

theorem distinct_append_of {xs ys : List Nat} (hx : Distinct xs) (hy : Distinct ys)
    (hd : ∀ a, a ∈ xs → a ∉ ys) : Distinct (xs ++ ys) := by
  induction xs with
  | nil => simpa using hy
  | cons x rest ih =>
    simp only [Distinct] at hx
    simp only [List.cons_append, Distinct]
    refine ⟨?_, ih hx.2 (fun a ha => hd a (List.mem_cons_of_mem _ ha))⟩
    intro hmem
    rcases List.mem_append.mp hmem with h1 | h2
    · exact hx.1 h1
    · exact hd x (List.mem_cons_self ..) h2

/-- Running a concatenation sequences the two runs: the second starts only if the first completes. -/
theorem runStmts_append : ∀ (xs ys : List Stmt) (sched : List Bool),
    runStmts (xs ++ ys) sched =
      match runStmts xs sched with
      | (t, .completed sched') =>
          let r := runStmts ys sched'
          (t ++ r.1, r.2)
      | (t, .suspended s sched') => (t, .suspended s sched')
  | [], ys, sched => by simp [runStmts]
  | .effect e :: rest, ys, sched => by
      simp only [List.cons_append, runStmts]
      rw [runStmts_append rest ys sched]
      cases runStmts rest sched with
      | mk t o => cases o <;> simp
  | .await s :: rest, ys, sched => by
      cases sched with
      | nil => simpa [runStmts] using runStmts_append rest ys []
      | cons b sched' =>
        cases b
        · simp [runStmts]
        · simpa [runStmts] using runStmts_append rest ys sched'
  | .tryFinally body fin :: rest, ys, sched => by
      simp only [List.cons_append, runStmts]
      cases runStmts body sched with
      | mk tb ob =>
        cases ob with
        | completed sb =>
          simp only
          rw [runStmts_append rest ys sb]
          cases runStmts rest sb with
          | mk tr or => cases or <;> simp [List.append_assoc]
        | suspended s2 sb => simp

theorem runStmts_map_effect (fin : List Nat) (sched : List Bool) :
    runStmts (fin.map Stmt.effect) sched = (fin, .completed sched) := by
  induction fin with
  | nil => simp [runStmts]
  | cons f rest ih => simp [runStmts, ih]

/-- A completed run produced the whole sequential trace. -/
theorem runStmts_completed : ∀ (stmts : List Stmt) (sched sched' : List Bool) (t : List Nat),
    runStmts stmts sched = (t, .completed sched') → t = runSeq stmts
  | [], sched, sched', t, h => by
      simp [runStmts] at h
      simp [runSeq, h]
  | .effect e :: rest, sched, sched', t, h => by
      simp only [runStmts, runSeq] at h ⊢
      cases hr : runStmts rest sched with
      | mk t' o =>
        rw [hr] at h
        simp at h
        obtain ⟨rfl, rfl⟩ := h
        rw [runStmts_completed rest sched sched' t' hr]
  | .await s :: rest, sched, sched', t, h => by
      simp only [runSeq]
      cases sched with
      | nil => simp only [runStmts] at h; exact runStmts_completed rest _ _ _ h
      | cons b sched'' =>
        cases b
        · simp [runStmts] at h
        · simp only [runStmts] at h; exact runStmts_completed rest _ _ _ h
  | .tryFinally body fin :: rest, sched, sched', t, h => by
      simp only [runStmts, runSeq] at h ⊢
      cases hb : runStmts body sched with
      | mk tb ob =>
        rw [hb] at h
        cases ob with
        | completed sb =>
          dsimp only at h
          cases hr : runStmts rest sb with
          | mk tr or =>
            rw [hr] at h
            cases or with
            | completed sc =>
              simp at h
              obtain ⟨rfl, rfl⟩ := h
              rw [runStmts_completed body sched sb tb hb, runStmts_completed rest sb sc tr hr]
              simp [List.append_assoc]
            | suspended s2 sc => simp at h
        | suspended s2 sb => simp at h

/-- A run over an empty schedule never suspends. -/
theorem runStmts_nil_completes : ∀ (stmts : List Stmt),
    ∃ t, runStmts stmts [] = (t, .completed [])
  | [] => ⟨[], by simp [runStmts]⟩
  | .effect e :: rest => by
      obtain ⟨t, ht⟩ := runStmts_nil_completes rest
      exact ⟨e :: t, by simp [runStmts, ht]⟩
  | .await s :: rest => by
      obtain ⟨t, ht⟩ := runStmts_nil_completes rest
      exact ⟨t, by simp [runStmts, ht]⟩
  | .tryFinally body fin :: rest => by
      obtain ⟨tb, hb⟩ := runStmts_nil_completes body
      obtain ⟨tr, hr⟩ := runStmts_nil_completes rest
      exact ⟨tb ++ fin ++ tr, by simp [runStmts, hb, hr]⟩

/-- The schedule a run hands back is a suffix of what it received; a suspension consumed at least one choice. -/
theorem runStmts_schedule : ∀ (stmts : List Stmt) (sched : List Bool),
    (∀ t sched', runStmts stmts sched = (t, .completed sched') → sched'.length ≤ sched.length) ∧
    (∀ t s sched', runStmts stmts sched = (t, .suspended s sched') → sched'.length < sched.length)
  | [], sched => by
      refine ⟨fun t sched' h => ?_, fun t s sched' h => ?_⟩
      · simp [runStmts] at h; simp [h]
      · simp [runStmts] at h
  | .effect e :: rest, sched => by
      refine ⟨fun t sched' h => ?_, fun t s sched' h => ?_⟩
      · simp only [runStmts] at h
        cases hr : runStmts rest sched with
        | mk t' o =>
          rw [hr] at h; simp at h; obtain ⟨-, rfl⟩ := h
          exact (runStmts_schedule rest sched).1 _ _ hr
      · simp only [runStmts] at h
        cases hr : runStmts rest sched with
        | mk t' o =>
          rw [hr] at h; simp at h; obtain ⟨-, rfl⟩ := h
          exact (runStmts_schedule rest sched).2 _ _ _ hr
  | .await s :: rest, sched => by
      refine ⟨fun t sched' h => ?_, fun t s0 sched' h => ?_⟩
      · cases sched with
        | nil => simp only [runStmts] at h; exact (runStmts_schedule rest []).1 _ _ h
        | cons b sched'' =>
          cases b
          · simp [runStmts] at h
          · simp only [runStmts] at h
            have := (runStmts_schedule rest sched'').1 _ _ h
            simp; omega
      · cases sched with
        | nil =>
          simp only [runStmts] at h
          have := (runStmts_schedule rest []).2 _ _ _ h
          simp at this
        | cons b sched'' =>
          cases b
          · simp [runStmts] at h; obtain ⟨-, -, rfl⟩ := h; simp
          · simp only [runStmts] at h
            have := (runStmts_schedule rest sched'').2 _ _ _ h
            simp; omega
  | .tryFinally body fin :: rest, sched => by
      refine ⟨fun t sched' h => ?_, fun t s sched' h => ?_⟩
      · simp only [runStmts] at h
        cases hb : runStmts body sched with
        | mk tb ob =>
          rw [hb] at h
          cases ob with
          | completed sb =>
            dsimp only at h
            cases hr : runStmts rest sb with
            | mk tr or =>
              rw [hr] at h
              cases or with
              | completed sc =>
                simp at h; obtain ⟨-, rfl⟩ := h
                have h1 := (runStmts_schedule body sched).1 _ _ hb
                have h2 := (runStmts_schedule rest sb).1 _ _ hr
                omega
              | suspended s2 sc => simp at h
          | suspended s2 sb => simp at h
      · simp only [runStmts] at h
        cases hb : runStmts body sched with
        | mk tb ob =>
          rw [hb] at h
          cases ob with
          | completed sb =>
            dsimp only at h
            cases hr : runStmts rest sb with
            | mk tr or =>
              rw [hr] at h
              cases or with
              | completed sc => simp at h
              | suspended s2 sc =>
                simp at h; obtain ⟨-, rfl, rfl⟩ := h
                have h1 := (runStmts_schedule body sched).1 _ _ hb
                have h2 := (runStmts_schedule rest sb).2 _ _ _ hr
                omega
          | suspended s2 sb =>
            simp at h; obtain ⟨-, rfl, rfl⟩ := h
            exact (runStmts_schedule body sched).2 _ _ _ hb

/-- A suspended run produced the sequential prefix before its await; that await is in the body. -/
theorem runStmts_suspended : ∀ (stmts : List Stmt), Distinct (awaits stmts) →
    ∀ (sched sched' : List Bool) (t : List Nat) (s : Nat),
    runStmts stmts sched = (t, .suspended s sched') →
    s ∈ awaits stmts ∧ runSeq stmts = t ++ runSeq (after stmts s)
  | [], _, sched, sched', t, s, h => by simp [runStmts] at h
  | .effect e :: rest, hd, sched, sched', t, s, h => by
      simp only [runStmts, runSeq, awaits, after] at h hd ⊢
      cases hr : runStmts rest sched with
      | mk t' o =>
        rw [hr] at h; simp at h
        obtain ⟨rfl, rfl⟩ := h
        obtain ⟨hmem, heq⟩ := runStmts_suspended rest hd _ _ _ _ hr
        exact ⟨hmem, by simp [heq]⟩
  | .await s' :: rest, hd, sched, sched', t, s, h => by
      simp only [runSeq, awaits, after, Distinct] at hd ⊢
      cases sched with
      | nil =>
        simp only [runStmts] at h
        obtain ⟨hmem, heq⟩ := runStmts_suspended rest hd.2 _ _ _ _ h
        have hne : s' ≠ s := fun hs => hd.1 (hs ▸ hmem)
        exact ⟨List.mem_cons_of_mem _ hmem, by simp [hne, heq]⟩
      | cons b sched'' =>
        cases b
        · simp [runStmts] at h
          obtain ⟨rfl, rfl, rfl⟩ := h
          simp
        · simp only [runStmts] at h
          obtain ⟨hmem, heq⟩ := runStmts_suspended rest hd.2 _ _ _ _ h
          have hne : s' ≠ s := fun hs => hd.1 (hs ▸ hmem)
          exact ⟨List.mem_cons_of_mem _ hmem, by simp [hne, heq]⟩
  | .tryFinally body fin :: rest, hd, sched, sched', t, s, h => by
      simp only [runStmts, runSeq, awaits, after] at h hd ⊢
      have hdb := distinct_append_left hd
      have hdr := distinct_append_right hd
      cases hb : runStmts body sched with
      | mk tb ob =>
        rw [hb] at h
        cases ob with
        | completed sb =>
          dsimp only at h
          cases hr : runStmts rest sb with
          | mk tr or =>
            rw [hr] at h
            cases or with
            | completed sc => simp at h
            | suspended s2 sc =>
              simp at h
              obtain ⟨rfl, hs, rfl⟩ := h
              subst s2
              obtain ⟨hmem, heq⟩ := runStmts_suspended rest hdr _ _ _ _ hr
              have hnot : s ∉ awaits body := fun hb' => distinct_append_disjoint hd hb' hmem
              refine ⟨List.mem_append_right _ hmem, ?_⟩
              simp only [hnot, ↓reduceIte]
              rw [runStmts_completed body sched sb tb hb, heq]
              simp [List.append_assoc]
        | suspended s2 sb =>
          simp at h
          obtain ⟨rfl, hs, rfl⟩ := h
          subst s2
          obtain ⟨hmem, heq⟩ := runStmts_suspended body hdb _ _ _ _ hb
          refine ⟨List.mem_append_left _ hmem, ?_⟩
          simp only [hmem, ↓reduceIte]
          rw [runSeq_append, runSeq_append, runSeq_map_effect, heq]
          simp [List.append_assoc]

/-! ## The continuation after a resume -/

theorem after_subset : ∀ (stmts : List Stmt) (s a : Nat),
    a ∈ awaits (after stmts s) → a ∈ awaits stmts
  | [], s, a, h => by simp [after, awaits] at h
  | .effect e :: rest, s, a, h => by
      simp only [after, awaits] at h ⊢
      exact after_subset rest s a h
  | .await s' :: rest, s, a, h => by
      simp only [after, awaits] at h ⊢
      by_cases hs : s' = s
      · simp only [hs, ↓reduceIte] at h
        exact List.mem_cons_of_mem _ h
      · simp only [hs, ↓reduceIte] at h
        exact List.mem_cons_of_mem _ (after_subset rest s a h)
  | .tryFinally body fin :: rest, s, a, h => by
      simp only [after, awaits] at h ⊢
      by_cases hs : s ∈ awaits body
      · simp only [hs, ↓reduceIte, awaits_append, awaits_map_effect, List.append_nil] at h
        rcases List.mem_append.mp h with h | h
        · exact List.mem_append_left _ (after_subset body s a h)
        · exact List.mem_append_right _ h
      · simp only [hs, ↓reduceIte] at h
        exact List.mem_append_right _ (after_subset rest s a h)

theorem after_distinct : ∀ (stmts : List Stmt) (s : Nat), Distinct (awaits stmts) →
    Distinct (awaits (after stmts s))
  | [], s, hd => by simp [after, awaits, Distinct]
  | .effect e :: rest, s, hd => by
      simp only [after, awaits] at hd ⊢
      exact after_distinct rest s hd
  | .await s' :: rest, s, hd => by
      simp only [after, awaits, Distinct] at hd ⊢
      by_cases hs : s' = s
      · simp only [hs, ↓reduceIte]; exact hd.2
      · simp only [hs, ↓reduceIte]; exact after_distinct rest s hd.2
  | .tryFinally body fin :: rest, s, hd => by
      simp only [after, awaits] at hd ⊢
      by_cases hs : s ∈ awaits body
      · simp only [hs, ↓reduceIte, awaits_append, awaits_map_effect, List.append_nil]
        refine distinct_append_of (after_distinct body s (distinct_append_left hd))
          (distinct_append_right hd) ?_
        intro a ha
        exact distinct_append_disjoint hd (after_subset body s a ha)
      · simp only [hs, ↓reduceIte]
        exact after_distinct rest s (distinct_append_right hd)

theorem after_append_in (xs ys : List Stmt) (s : Nat) (h : s ∈ awaits xs) :
    after (xs ++ ys) s = after xs s ++ ys := by
  induction xs with
  | nil => simp [awaits] at h
  | cons x rest ih =>
    cases x with
    | effect e => simp only [List.cons_append, after, awaits] at h ⊢; exact ih h
    | await s' =>
      simp only [List.cons_append, after, awaits] at h ⊢
      by_cases hs : s' = s
      · simp [hs]
      · simp only [hs, ↓reduceIte]
        exact ih (by rcases List.mem_cons.mp h with h | h; exact absurd h.symm hs; exact h)
    | tryFinally body fin =>
      simp only [List.cons_append, after, awaits] at h ⊢
      by_cases hs : s ∈ awaits body
      · simp [hs, List.append_assoc]
      · simp only [hs, ↓reduceIte]
        exact ih (by rcases List.mem_append.mp h with h | h; exact absurd h hs; exact h)

theorem after_append_out (xs ys : List Stmt) (s : Nat) (h : s ∉ awaits xs) :
    after (xs ++ ys) s = after ys s := by
  induction xs with
  | nil => rfl
  | cons x rest ih =>
    cases x with
    | effect e => simp only [List.cons_append, after, awaits] at h ⊢; exact ih h
    | await s' =>
      simp only [List.cons_append, after, awaits] at h ⊢
      have hs : s' ≠ s := fun e => h (e ▸ List.mem_cons_self ..)
      simp only [hs, ↓reduceIte]
      exact ih (fun hr => h (List.mem_cons_of_mem _ hr))
    | tryFinally body fin =>
      simp only [List.cons_append, after, awaits] at h ⊢
      have hs : s ∉ awaits body := fun hb => h (List.mem_append_left _ hb)
      simp only [hs, ↓reduceIte]
      exact ih (fun hr => h (List.mem_append_right _ hr))

/-- Resuming later in a body already resumed once lands where resuming the original body would. -/
theorem after_after : ∀ (stmts : List Stmt), Distinct (awaits stmts) → ∀ (s1 s : Nat),
    s ∈ awaits (after stmts s1) → after stmts s = after (after stmts s1) s
  | [], _, s1, s, h => by simp [after]
  | .effect e :: rest, hd, s1, s, h => by
      simp only [after, awaits] at h hd ⊢
      exact after_after rest hd s1 s h
  | .await s' :: rest, hd, s1, s, h => by
      simp only [awaits, Distinct] at hd
      by_cases h1 : s' = s1
      · subst h1
        have h' : s ∈ awaits rest := by simpa [after] using h
        have hne : s' ≠ s := fun e => hd.1 (e ▸ h')
        simp [after, hne]
      · have h' : s ∈ awaits (after rest s1) := by simpa [after, h1] using h
        have hne : s' ≠ s := fun e => hd.1 (e ▸ after_subset rest s1 s h')
        simp only [after, hne, h1, ↓reduceIte]
        exact after_after rest hd.2 s1 s h'
  | .tryFinally body fin :: rest, hd, s1, s, h => by
      simp only [after, awaits] at h hd ⊢
      have hdb := distinct_append_left hd
      have hdr := distinct_append_right hd
      by_cases h1 : s1 ∈ awaits body
      · simp only [h1, ↓reduceIte] at h ⊢
        rw [awaits_append, awaits_append, awaits_map_effect, List.append_nil] at h
        rcases List.mem_append.mp h with hin | hin
        · have hs : s ∈ awaits body := after_subset body s1 s hin
          simp only [hs, ↓reduceIte]
          rw [after_append_in (after body s1 ++ fin.map Stmt.effect) rest s
              (by rw [awaits_append]; exact List.mem_append_left _ hin),
            after_append_in _ _ _ hin, after_after body hdb s1 s hin]
        · have hs : s ∉ awaits body := fun hb => distinct_append_disjoint hd hb hin
          simp only [hs, ↓reduceIte]
          rw [after_append_out (after body s1 ++ fin.map Stmt.effect) rest s]
          rw [awaits_append, awaits_map_effect, List.append_nil]
          intro ha
          exact hs (after_subset body s1 s ha)
      · simp only [h1, ↓reduceIte] at h ⊢
        have hs : s ∉ awaits body := fun hb =>
          distinct_append_disjoint hd hb (after_subset rest s1 s h)
        simp only [hs, ↓reduceIte]
        exact after_after rest hdr s1 s h

/-- Resuming at a stored state runs exactly the continuation after that await. -/
theorem resume_eq : ∀ (stmts : List Stmt), Distinct (awaits stmts) → ∀ (s : Nat),
    s ∈ awaits stmts → ∀ (sched : List Bool),
    resumeStmts stmts s sched = runStmts (after stmts s) sched
  | [], _, s, hs, sched => by simp [awaits] at hs
  | .effect e :: rest, hd, s, hs, sched => by
      simp only [resumeStmts, after, awaits] at hs hd ⊢
      exact resume_eq rest hd s hs sched
  | .await s' :: rest, hd, s, hs, sched => by
      simp only [resumeStmts, after, awaits, Distinct] at hs hd ⊢
      by_cases h1 : s' = s
      · simp [h1]
      · simp only [h1, ↓reduceIte]
        exact resume_eq rest hd.2 s
          (by rcases List.mem_cons.mp hs with h | h; exact absurd h.symm h1; exact h) sched
  | .tryFinally body fin :: rest, hd, s, hs, sched => by
      simp only [resumeStmts, after, awaits] at hs hd ⊢
      have hdb := distinct_append_left hd
      have hdr := distinct_append_right hd
      by_cases hb : s ∈ awaits body
      · simp only [hb, ↓reduceIte]
        rw [resume_eq body hdb s hb sched, List.append_assoc, runStmts_append]
        cases runStmts (after body s) sched with
        | mk t o =>
          cases o with
          | completed sched' =>
            simp only
            rw [runStmts_append, runStmts_map_effect]
            simp [List.append_assoc]
          | suspended s2 sched' => simp
      · simp only [hb, ↓reduceIte]
        exact resume_eq rest hdr s
          (by rcases List.mem_append.mp hs with h | h; exact absurd h hb; exact h) sched

/-- The bodies the driver may be running: the original or any resume continuation of one. -/
inductive Suffix (stmts : List Stmt) : List Stmt → Prop where
  | refl : Suffix stmts stmts
  | step {q : List Stmt} (s : Nat) : Suffix stmts q → s ∈ awaits q → Suffix stmts (after q s)

theorem suffix_awaits_subset {stmts q : List Stmt} (h : Suffix stmts q) {a : Nat}
    (ha : a ∈ awaits q) : a ∈ awaits stmts := by
  induction h with
  | refl => exact ha
  | step s _ _ ih => exact ih (after_subset _ s a ha)

theorem suffix_distinct {stmts q : List Stmt} (hd : Distinct (awaits stmts)) (h : Suffix stmts q) :
    Distinct (awaits q) := by
  induction h with
  | refl => exact hd
  | step s _ _ ih => exact after_distinct _ s ih

theorem suffix_after {stmts q : List Stmt} (hd : Distinct (awaits stmts)) (h : Suffix stmts q)
    (s : Nat) (hs : s ∈ awaits q) : after stmts s = after q s := by
  induction h with
  | refl => rfl
  | step s1 hq hs1 ih =>
    rw [ih (after_subset _ s1 s hs)]
    exact after_after _ (suffix_distinct hd hq) s1 s hs

/-- The driver, however many times it re-enters, replays the sequential body it is running. -/
theorem drive_eq (stmts : List Stmt) (hd : Distinct (awaits stmts)) :
    ∀ (fuel : Nat) (sched : List Bool) (q : List Stmt), sched.length ≤ fuel → Suffix stmts q →
      drive stmts fuel (runStmts q sched) = runSeq q := by
  intro fuel
  induction fuel with
  | zero =>
    intro sched q hlen hq
    have hnil : sched = [] := List.eq_nil_of_length_eq_zero (Nat.le_zero.mp hlen)
    subst hnil
    obtain ⟨t, ht⟩ := runStmts_nil_completes q
    rw [ht]
    simp only [drive]
    exact runStmts_completed q [] [] t ht
  | succ m ih =>
    intro sched q hlen hq
    cases hr : runStmts q sched with
    | mk t o =>
      cases o with
      | completed sched' =>
        simp only [drive]
        exact runStmts_completed q sched sched' t hr
      | suspended s sched' =>
        simp only [drive]
        have hdq := suffix_distinct hd hq
        obtain ⟨hmem, heq⟩ := runStmts_suspended q hdq sched sched' t s hr
        have hlt := (runStmts_schedule q sched).2 _ _ _ hr
        rw [resume_eq stmts hd s (suffix_awaits_subset hq hmem) sched', suffix_after hd hq s hmem]
        rw [ih sched' (after q s) (by omega) (Suffix.step s hq hmem)]
        exact heq.symm

/-- **Splice equivalence.** For every body with distinct state constants and every schedule of suspension choices, the lowered machine's user-effect trace is the sequential body's trace. -/
theorem runMachine_eq_runSeq (stmts : List Stmt) (hd : Distinct (awaits stmts)) (sched : List Bool) :
    runMachine stmts sched = runSeq stmts :=
  drive_eq stmts hd sched.length sched stmts (Nat.le_refl _) Suffix.refl

end ClassicAsyncSplice
