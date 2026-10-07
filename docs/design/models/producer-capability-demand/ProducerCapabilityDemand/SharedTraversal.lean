/-!
# Shared forward traversal

The joined demand chooses one shared physical source; it must not change any
participant's result. `query-space-composition.md#preserve-terminal-specialized-plans`
also requires that each settled terminal "stops receiving work and being
charged at the point its independent execution would have stopped."

An analyzer is an arbitrary left-to-right step function with its own settlement
predicate. Running it alone stops at settlement. A host shares one forward
traversal between two analyzers, steps only unsettled participants, charges
each participant for the items it actually stepped, and stops when both have
settled. `both` is itself an analyzer, so the theorems extend to any number of
participants by nesting.

Items are arbitrary, so a failing operand is an ordinary item that an
analyzer's step may turn into a settled failure.
-/

namespace ProducerCapabilityDemand

structure Analyzer (I σ : Type) where
  step : σ → I → σ
  settled : σ → Bool

namespace Analyzer

variable {I σ τ : Type}

/--
Independent execution: final state and the number of items charged before
settlement or exhaustion.
-/
def runAlone (a : Analyzer I σ) : σ → List I → σ × Nat
  | s, [] => (s, 0)
  | s, i :: is =>
      if a.settled s then
        (s, 0)
      else
        let r := runAlone a (a.step s i) is
        (r.1, r.2 + 1)

/-- A host step that leaves a settled participant and its charge unchanged. -/
def guarded (a : Analyzer I σ) (p : σ × Nat) (i : I) : σ × Nat :=
  if a.settled p.1 then p else (a.step p.1 i, p.2 + 1)

/--
Two participants sharing one forward traversal. Each component carries that
participant's own state and charge.
-/
def both (a : Analyzer I σ) (b : Analyzer I τ) :
    Analyzer I ((σ × Nat) × (τ × Nat)) where
  step p i := (guarded a p.1 i, guarded b p.2 i)
  settled p := a.settled p.1.1 && b.settled p.2.1

theorem runAlone_settled (a : Analyzer I σ) {s : σ}
    (h : a.settled s = true) (xs : List I) :
    runAlone a s xs = (s, 0) := by
  cases xs <;> simp [runAlone, h]

/--
Sharing preserves each participant's independent result and charge, and the
shared traversal visits exactly as many items as the longest participant.
-/
theorem runAlone_both (a : Analyzer I σ) (b : Analyzer I τ)
    (xs : List I) (s : σ) (t : τ) (n m : Nat) :
    runAlone (both a b) ((s, n), (t, m)) xs =
      ((((runAlone a s xs).1, n + (runAlone a s xs).2),
        ((runAlone b t xs).1, m + (runAlone b t xs).2)),
       max (runAlone a s xs).2 (runAlone b t xs).2) := by
  induction xs generalizing s t n m with
  | nil => simp [runAlone]
  | cons i is ih =>
      by_cases ha : a.settled s = true <;> by_cases hb : b.settled t = true
      · simp [runAlone, both, ha, hb]
      · have := ih s (b.step t i) n (m + 1)
        simp only [runAlone, both, guarded, ha, hb] at this ⊢
        simp only [Bool.true_and, Bool.false_eq_true, ↓reduceIte] at this ⊢
        rw [this, runAlone_settled a ha]
        simp only [Prod.mk.injEq, true_and]
        and_intros <;> first | trivial | omega
      · have := ih (a.step s i) t (n + 1) m
        simp only [runAlone, both, guarded, ha, hb] at this ⊢
        simp only [Bool.and_true, Bool.false_eq_true, ↓reduceIte] at this ⊢
        rw [this, runAlone_settled b hb]
        simp only [Prod.mk.injEq, true_and, and_true]
        and_intros <;> first | trivial | omega
      · have := ih (a.step s i) (b.step t i) (n + 1) (m + 1)
        simp only [runAlone, both, guarded, ha, hb] at this ⊢
        simp only [Bool.and_false, Bool.false_eq_true, ↓reduceIte] at this ⊢
        rw [this]
        simp only [Prod.mk.injEq]
        and_intros <;> first | trivial | omega

/-- Each participant's shared result equals its independent result. -/
theorem shared_result_left (a : Analyzer I σ) (b : Analyzer I τ)
    (xs : List I) (s : σ) (t : τ) :
    (runAlone (both a b) ((s, 0), (t, 0)) xs).1.1 = runAlone a s xs := by
  simp [runAlone_both]

theorem shared_result_right (a : Analyzer I σ) (b : Analyzer I τ)
    (xs : List I) (s : σ) (t : τ) :
    (runAlone (both a b) ((s, 0), (t, 0)) xs).1.2 = runAlone b t xs := by
  simp [runAlone_both]

/-- One shared traversal never visits more items than two independent ones. -/
theorem shared_visits_le_independent (a : Analyzer I σ) (b : Analyzer I τ)
    (xs : List I) (s : σ) (t : τ) :
    (runAlone (both a b) ((s, 0), (t, 0)) xs).2 ≤
      (runAlone a s xs).2 + (runAlone b t xs).2 := by
  rw [runAlone_both]
  omega

end Analyzer

/-! ## The terminal-folding pathology, revisited -/

inductive Operand where
  | rejected
  | admitted (rows : Nat)
  | failed
  deriving DecidableEq, Repr

inductive TerminalState where
  | active (value : Nat)
  | settled (value : Nat)
  | failed
  deriving DecidableEq, Repr

def TerminalState.isActive : TerminalState → Bool
  | .active _ => true
  | _ => false

/-- `Exists` settles at the first admitted operand that has a row. -/
def existsTerminal : Analyzer Operand TerminalState where
  step
    | .active _, .admitted (_ + 1) => .settled 1
    | .active v, .admitted 0 => .active v
    | .active v, .rejected => .active v
    | .active _, .failed => .failed
    | s, _ => s
  settled s := !s.isActive

/-- `Count` must exhaust; a failure settles it as failed. -/
def countTerminal : Analyzer Operand TerminalState where
  step
    | .active v, .admitted rows => .active (v + rows)
    | .active v, .rejected => .active v
    | .active _, .failed => .failed
    | s, _ => s
  settled s := !s.isActive

/-- Rejected, two admitted rows, a failure, then more work. -/
def pathological : List Operand :=
  [.rejected, .admitted 2, .failed, .admitted 1]

/-- `Exists` settles before the failure and is charged for two operands. -/
example :
    (Analyzer.runAlone
      (Analyzer.both existsTerminal countTerminal)
      ((.active 0, 0), (.active 0, 0)) pathological) =
    (((.settled 1, 2), (.failed, 3)), 3) := by
  decide

end ProducerCapabilityDemand
