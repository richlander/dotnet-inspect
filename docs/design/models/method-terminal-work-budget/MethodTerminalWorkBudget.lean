/-!
# Method-source terminal work budget

A model of `MethodDefinitionTerminalWorkBudget` and its call sites in
`MethodDefinitionUnit.GetBody` from
[#9364](https://github.com/richlander/dotnet-inspect/pull/9364).

The model classifies the budget's work by whether it can change an observable
result:

* `run_ok_of_universe_fits` and `unbounded_never_reaches_limit`: under
  `MethodDefinitionTerminalWorkLimits.Unbounded`, no sequence of body
  acquisitions over one ECMA-335 MethodDef table can reach a limit.
* `untracked_unbounded_budget_unobservable`: an untracked execution also
  discards the budget's coverage, so the budget changes nothing it publishes.
* `tracked_admitted_eq_acquired` and its failure companions: in a tracked
  lane the budget's admitted-method set equals the lane's `BodiesAcquired`
  coverage, except for the one body an encoded-IL exhaustion acquired but did
  not admit.
* `admit_eq_admitUnchecked_of_require`: inside `GetBody`, `Admit`'s own
  capacity check repeats the check `GetBody` already made against the same
  state.

MethodDefs are row numbers. `il m` is the length `GetILReader().Length`
reports for MethodDef `m`'s body; one image fixes it.
-/

namespace MethodTerminalWorkBudget

/-- `MethodDefinitionTerminalWorkLimits`. -/
structure Limits where
  maxBodies : Nat
  maxBytes : Nat

/-- `MethodDefinitionTerminalWorkLimitKind`. -/
inductive LimitKind where
  | bodies
  | encodedIlBytes
  deriving DecidableEq, Repr

/-- The budget's retained state: admitted MethodDefs, newest first, and the
encoded-IL bytes charged for them. -/
structure Budget where
  admitted : List Nat
  bytes : Nat

def Budget.empty : Budget := ⟨[], 0⟩

/-- `RequireBodyCapacity`. -/
def require (L : Limits) (b : Budget) (m : Nat) : Except LimitKind Unit :=
  if m ∈ b.admitted then .ok ()
  else if L.maxBodies ≤ b.admitted.length then .error .bodies
  else .ok ()

/-- `Admit`. C# tests `encodedIlBytes > MaximumEncodedIlBytes - _encodedIlBytes`;
with the retained `_encodedIlBytes ≤ MaximumEncodedIlBytes` that is
`bytes + il > maxBytes`. -/
def admit (L : Limits) (b : Budget) (m il : Nat) : Except LimitKind Budget :=
  if m ∈ b.admitted then .ok b
  else
    match require L b m with
    | .error e => .error e
    | .ok () =>
      if L.maxBytes < b.bytes + il then .error .encodedIlBytes
      else .ok ⟨m :: b.admitted, b.bytes + il⟩

/-- `Admit` without its internal `RequireBodyCapacity`. -/
def admitUnchecked (L : Limits) (b : Budget) (m il : Nat) :
    Except LimitKind Budget :=
  if m ∈ b.admitted then .ok b
  else if L.maxBytes < b.bytes + il then .error .encodedIlBytes
  else .ok ⟨m :: b.admitted, b.bytes + il⟩

/-- The budget calls in `MethodDefinitionUnit.GetBody`: capacity before body
acquisition, admission after it. -/
def getBody (L : Limits) (il : Nat → Nat) (b : Budget) (m : Nat) :
    Except LimitKind Budget :=
  match require L b m with
  | .error e => .error e
  | .ok () => admit L b m (il m)

/-- One execution lane's body acquisitions, in visit order. -/
def run (L : Limits) (il : Nat → Nat) : Budget → List Nat → Except LimitKind Budget
  | b, [] => .ok b
  | b, m :: ms =>
    match getBody L il b m with
    | .error e => .error e
    | .ok b' => run L il b' ms

/-! ## The internal capacity check is a repeat -/

theorem admit_eq_admitUnchecked_of_require {L : Limits} {b : Budget} {m il : Nat}
    (h : require L b m = .ok ()) :
    admit L b m il = admitUnchecked L b m il := by
  unfold admit admitUnchecked
  rw [h]

/-- `GetBody` can use the unchecked admission. -/
theorem getBody_eq_unchecked (L : Limits) (il : Nat → Nat) (b : Budget) (m : Nat) :
    getBody L il b m =
      match require L b m with
      | .error e => .error e
      | .ok () => admitUnchecked L b m (il m) := by
  unfold getBody
  cases h : require L b m with
  | error e => rfl
  | ok u => cases u; exact admit_eq_admitUnchecked_of_require h

/-! ## Unbounded limits cannot be reached -/

theorem length_le_of_nodup_subset :
    ∀ {l U : List Nat}, l.Nodup → l ⊆ U → l.length ≤ U.length
  | [], _, _, _ => Nat.zero_le _
  | a :: l, U, hnd, hsub => by
    rw [List.nodup_cons] at hnd
    have ha : a ∈ U := hsub (List.mem_cons_self ..)
    have hl : l ⊆ U.erase a := by
      intro x hx
      have hne : x ≠ a := fun h => hnd.1 (h ▸ hx)
      exact (List.mem_erase_of_ne hne).2 (hsub (List.mem_cons_of_mem _ hx))
    have ih := length_le_of_nodup_subset hnd.2 hl
    rw [List.length_erase_of_mem ha] at ih
    have hpos : 0 < U.length := List.length_pos_of_mem ha
    simp only [List.length_cons]
    omega

/-- The invariant one lane keeps over the MethodDef table `U`. -/
structure Inv (U : List Nat) (maxIl : Nat) (b : Budget) : Prop where
  nodup : b.admitted.Nodup
  subset : b.admitted ⊆ U
  bytes_le : b.bytes ≤ b.admitted.length * maxIl

theorem Inv.empty (U : List Nat) (maxIl : Nat) : Inv U maxIl Budget.empty where
  nodup := List.nodup_nil
  subset := List.nil_subset _
  bytes_le := Nat.zero_le _

/-- One body acquisition preserves the invariant and cannot fail when the
whole table fits the limits. -/
theorem getBody_ok_of_universe_fits {L : Limits} {il : Nat → Nat} {U : List Nat}
    {maxIl : Nat} {b : Budget} {m : Nat}
    (hU : U.Nodup) (hm : m ∈ U) (hil : ∀ x ∈ U, il x ≤ maxIl)
    (hbodies : U.length ≤ L.maxBodies) (hbytes : U.length * maxIl ≤ L.maxBytes)
    (hinv : Inv U maxIl b) :
    ∃ b', getBody L il b m = .ok b' ∧ Inv U maxIl b' := by
  have _ := hU
  by_cases hin : m ∈ b.admitted
  · refine ⟨b, ?_, hinv⟩
    simp [getBody, require, admit, hin]
  · have hlen : b.admitted.length + 1 ≤ U.length := by
      have := length_le_of_nodup_subset (l := m :: b.admitted)
        (List.nodup_cons.2 ⟨hin, hinv.nodup⟩)
        (List.cons_subset.2 ⟨hm, hinv.subset⟩)
      simpa using this
    have hcap : ¬ L.maxBodies ≤ b.admitted.length := by omega
    have hilm := hil m hm
    have hfit : b.bytes + il m ≤ (b.admitted.length + 1) * maxIl := by
      rw [Nat.succ_mul]
      exact Nat.add_le_add hinv.bytes_le hilm
    have hfit' : ¬ L.maxBytes < b.bytes + il m := by
      have := Nat.mul_le_mul_right maxIl hlen
      omega
    refine ⟨⟨m :: b.admitted, b.bytes + il m⟩, ?_, ?_⟩
    · simp [getBody, require, admit, hin, hcap, hfit']
    · exact {
        nodup := List.nodup_cons.2 ⟨hin, hinv.nodup⟩
        subset := List.cons_subset.2 ⟨hm, hinv.subset⟩
        bytes_le := by simpa using hfit }

/-- No acquisition sequence over the table can reach a limit that the whole
table fits. -/
theorem run_ok_of_universe_fits {L : Limits} {il : Nat → Nat} {U : List Nat}
    {maxIl : Nat}
    (hU : U.Nodup) (hil : ∀ x ∈ U, il x ≤ maxIl)
    (hbodies : U.length ≤ L.maxBodies) (hbytes : U.length * maxIl ≤ L.maxBytes) :
    ∀ (ms : List Nat) (b : Budget), ms ⊆ U → Inv U maxIl b →
      ∃ b', run L il b ms = .ok b' ∧ Inv U maxIl b'
  | [], b, _, hinv => ⟨b, rfl, hinv⟩
  | m :: ms, b, hsub, hinv => by
    obtain ⟨b', hstep, hinv'⟩ :=
      getBody_ok_of_universe_fits hU (hsub (List.mem_cons_self ..)) hil hbodies
        hbytes hinv
    obtain ⟨b'', hrun, hinv''⟩ :=
      run_ok_of_universe_fits hU hil hbodies hbytes ms b'
        (fun x hx => hsub (List.mem_cons_of_mem _ hx)) hinv'
    exact ⟨b'', by simp [run, hstep, hrun], hinv''⟩

/-- `MethodDefinitionTerminalWorkLimits.Unbounded`:
`int.MaxValue` bodies and `long.MaxValue` encoded-IL bytes. -/
def unbounded : Limits := ⟨2 ^ 31 - 1, 2 ^ 63 - 1⟩

/-- ECMA-335 metadata tokens carry a 24-bit row number, so a MethodDef table
has fewer than `2^24` rows, and `GetILReader().Length` is a nonnegative `int`.
Under those facts no acquisition sequence reaches an `Unbounded` limit. -/
theorem unbounded_never_reaches_limit {il : Nat → Nat} {U : List Nat}
    (hU : U.Nodup) (hrows : U.length < 2 ^ 24) (hil : ∀ x ∈ U, il x < 2 ^ 31)
    (ms : List Nat) (hms : ms ⊆ U) :
    ∃ b, run unbounded il Budget.empty ms = .ok b := by
  have hil' : ∀ x ∈ U, il x ≤ 2 ^ 31 - 1 := fun x hx =>
    Nat.le_pred_of_lt (hil x hx)
  have hbodies : U.length ≤ unbounded.maxBodies :=
    show U.length ≤ 2 ^ 31 - 1 from
      Nat.le_pred_of_lt
        (Nat.lt_of_lt_of_le hrows (by decide : 2 ^ 24 ≤ 2 ^ 31))
  have hbytes : U.length * (2 ^ 31 - 1) ≤ unbounded.maxBytes := by
    have h1 : U.length * (2 ^ 31 - 1) ≤ (2 ^ 24 - 1) * (2 ^ 31 - 1) :=
      Nat.mul_le_mul_right _ (Nat.le_pred_of_lt hrows)
    have h2 : (2 ^ 24 - 1) * (2 ^ 31 - 1) ≤ 2 ^ 63 - 1 := by decide
    exact show U.length * (2 ^ 31 - 1) ≤ 2 ^ 63 - 1 from Nat.le_trans h1 h2
  obtain ⟨b, h, _⟩ :=
    run_ok_of_universe_fits hU hil' hbodies hbytes ms Budget.empty hms
      (Inv.empty U _)
  exact ⟨b, h⟩

/-! ## An untracked, unbounded budget is unobservable -/

/-- What an execution publishes from its budget: a limit failure, the
`TerminalWork` coverage when coverage is tracked, or nothing. -/
def published (tracked : Bool) : Except LimitKind Budget →
    Except LimitKind (Option (Nat × Nat))
  | .error e => .error e
  | .ok b => .ok (if tracked then some (b.admitted.length, b.bytes) else none)

/-- Every production caller of `MethodDefinitionExecution.Execute(description,
sourceName, peReader)` is untracked and `Unbounded`. Running the budget there
publishes exactly what running no budget publishes. -/
theorem untracked_unbounded_budget_unobservable {il : Nat → Nat} {U : List Nat}
    (hU : U.Nodup) (hrows : U.length < 2 ^ 24) (hil : ∀ x ∈ U, il x < 2 ^ 31)
    (ms : List Nat) (hms : ms ⊆ U) :
    published false (run unbounded il Budget.empty ms) = .ok none := by
  obtain ⟨b, h⟩ := unbounded_never_reaches_limit hU hrows hil ms hms
  simp [h, published]

/-! ## A tracked lane's admitted set is its acquired coverage -/

/-- `MethodDefinitionHandleCoverageBuilder.Add` as a set: insert once. -/
def record (m : Nat) (acquired : List Nat) : List Nat :=
  if m ∈ acquired then acquired else m :: acquired

/-- A tracked lane: the budget plus the lane's `BodiesAcquired` coverage. -/
structure Lane where
  budget : Budget
  acquired : List Nat

/-- Tracked `GetBody`: `RequireBodyCapacity`, body acquisition and
`RecordBodyAcquired`, then `Admit`. A failure keeps the coverage it reached. -/
def getBodyTracked (L : Limits) (il : Nat → Nat) (s : Lane) (m : Nat) :
    Except (LimitKind × List Nat) Lane :=
  match require L s.budget m with
  | .error e => .error (e, s.acquired)
  | .ok () =>
    match admit L s.budget m (il m) with
    | .error e => .error (e, record m s.acquired)
    | .ok b => .ok ⟨b, record m s.acquired⟩

def runTracked (L : Limits) (il : Nat → Nat) :
    Lane → List Nat → Except (LimitKind × List Nat) Lane
  | s, [] => .ok s
  | s, m :: ms =>
    match getBodyTracked L il s m with
    | .error e => .error e
    | .ok s' => runTracked L il s' ms

theorem admit_admitted {L : Limits} {b b' : Budget} {m il : Nat}
    (h : admit L b m il = .ok b') : b'.admitted = record m b.admitted := by
  unfold admit at h
  unfold record
  by_cases hin : m ∈ b.admitted
  · simp only [hin, ite_true] at h
    cases h; simp [hin]
  · simp only [hin, ite_false] at h
    split at h
    · cases h
    · split at h
      · cases h
      · cases h; simp [hin]

/-- Every successful tracked step keeps the admitted set equal to the
acquired coverage. -/
theorem getBodyTracked_admitted_eq {L : Limits} {il : Nat → Nat} {s s' : Lane}
    {m : Nat} (heq : s.budget.admitted = s.acquired)
    (h : getBodyTracked L il s m = .ok s') :
    s'.budget.admitted = s'.acquired := by
  unfold getBodyTracked at h
  split at h
  · cases h
  · split at h
    · cases h
    · rename_i b hadmit
      cases h
      rw [admit_admitted hadmit, heq]

/-- A tracked lane that completes has admitted exactly the bodies it
acquired, so `BodiesAdmitted = BodiesAcquired.Count`. -/
theorem tracked_admitted_eq_acquired {L : Limits} {il : Nat → Nat} :
    ∀ (ms : List Nat) (s s' : Lane), s.budget.admitted = s.acquired →
      runTracked L il s ms = .ok s' → s'.budget.admitted = s'.acquired
  | [], s, s', heq, h => by cases h; exact heq
  | m :: ms, s, s', heq, h => by
    unfold runTracked at h
    split at h
    · cases h
    · rename_i s₁ hstep
      exact tracked_admitted_eq_acquired ms s₁ s'
        (getBodyTracked_admitted_eq heq hstep) h

/-- A failing step's coverage: body-count exhaustion acquired nothing more;
encoded-IL exhaustion acquired exactly the one body it did not admit. -/
theorem getBodyTracked_failure {L : Limits} {il : Nat → Nat} {s : Lane} {m : Nat}
    {e : LimitKind} {acq : List Nat} (heq : s.budget.admitted = s.acquired)
    (h : getBodyTracked L il s m = .error (e, acq)) :
    (e = .bodies ∧ acq = s.budget.admitted) ∨
      (e = .encodedIlBytes ∧ m ∉ s.budget.admitted ∧ acq = m :: s.budget.admitted) := by
  unfold getBodyTracked at h
  split at h
  · rename_i e' hreq
    cases h
    left
    unfold require at hreq
    split at hreq
    · cases hreq
    · split at hreq
      · cases hreq; exact ⟨rfl, heq.symm⟩
      · cases hreq
  · rename_i hreq
    split at h
    · rename_i e' hadmit
      cases h
      unfold admit at hadmit
      by_cases hin : m ∈ s.budget.admitted
      · simp [hin] at hadmit
      · simp only [hin, ite_false, hreq] at hadmit
        split at hadmit
        · cases hadmit
          right
          refine ⟨rfl, hin, ?_⟩
          unfold record
          rw [← heq]
          simp [hin]
        · cases hadmit
    · cases h

end MethodTerminalWorkBudget
