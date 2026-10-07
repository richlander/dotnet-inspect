import Std

namespace BodyUseTerminalFolding

inductive Operand (Row Error : Type) where
  | rejected
  | admitted (rows : List Row)
  | failed (error : Error)
  deriving Repr, DecidableEq

inductive Completion where
  | exhausted
  | settled
  deriving Repr, DecidableEq

inductive Outcome (Value Error : Type) where
  | success (value : Value) (completion : Completion)
  | failure (error : Error)
  deriving Repr, DecidableEq

structure Execution (Value Error : Type) where
  outcome : Outcome Value Error
  operandsVisited : Nat
  rowsConstructed : Nat
  deriving Repr, DecidableEq

def Execution.visit
    (execution : Execution Value Error)
    (rowsConstructed : Nat := 0) :
    Execution Value Error :=
  {
    execution with
    operandsVisited := execution.operandsVisited + 1
    rowsConstructed := execution.rowsConstructed + rowsConstructed
  }

def prependCount
    (amount : Nat)
    (execution : Execution Nat Error) :
    Execution Nat Error :=
  match execution.outcome with
  | .success value completion =>
      {
        outcome := .success (amount + value) completion
        operandsVisited := execution.operandsVisited + 1
        rowsConstructed := execution.rowsConstructed
      }
  | .failure error =>
      {
        outcome := .failure error
        operandsVisited := execution.operandsVisited + 1
        rowsConstructed := execution.rowsConstructed
      }

def prependRows
    (rows : List Row)
    (execution : Execution (List Row) Error) :
    Execution (List Row) Error :=
  match execution.outcome with
  | .success value completion =>
      {
        outcome := .success (rows ++ value) completion
        operandsVisited := execution.operandsVisited + 1
        rowsConstructed := rows.length + execution.rowsConstructed
      }
  | .failure error =>
      {
        outcome := .failure error
        operandsVisited := execution.operandsVisited + 1
        rowsConstructed := rows.length + execution.rowsConstructed
      }

def runExists :
    List (Operand Row Error) → Execution Bool Error
  | [] =>
      {
        outcome := .success false .exhausted
        operandsVisited := 0
        rowsConstructed := 0
      }
  | .failed error :: _ =>
      {
        outcome := .failure error
        operandsVisited := 1
        rowsConstructed := 0
      }
  | .rejected :: rest =>
      (runExists rest).visit
  | .admitted [] :: rest =>
      (runExists rest).visit
  | .admitted (_ :: _) :: _ =>
      {
        outcome := .success true .settled
        operandsVisited := 1
        rowsConstructed := 0
      }

def runCount :
    List (Operand Row Error) → Execution Nat Error
  | [] =>
      {
        outcome := .success 0 .exhausted
        operandsVisited := 0
        rowsConstructed := 0
      }
  | .failed error :: _ =>
      {
        outcome := .failure error
        operandsVisited := 1
        rowsConstructed := 0
      }
  | .rejected :: rest =>
      (runCount rest).visit
  | .admitted rows :: rest =>
      prependCount rows.length (runCount rest)

def runRows :
    List (Operand Row Error) → Execution (List Row) Error
  | [] =>
      {
        outcome := .success [] .exhausted
        operandsVisited := 0
        rowsConstructed := 0
      }
  | .failed error :: _ =>
      {
        outcome := .failure error
        operandsVisited := 1
        rowsConstructed := 0
      }
  | .rejected :: rest =>
      (runRows rest).visit
  | .admitted rows :: rest =>
      prependRows rows (runRows rest)

structure FusedExecution (Row Error : Type) where
  existsResult : Execution Bool Error
  countResult : Execution Nat Error
  rowsResult : Execution (List Row) Error
  sharedOperandsVisited : Nat
  deriving Repr, DecidableEq

def runFused :
    List (Operand Row Error) → FusedExecution Row Error
  | [] =>
      {
        existsResult :=
          {
            outcome := .success false .exhausted
            operandsVisited := 0
            rowsConstructed := 0
          }
        countResult :=
          {
            outcome := .success 0 .exhausted
            operandsVisited := 0
            rowsConstructed := 0
          }
        rowsResult :=
          {
            outcome := .success [] .exhausted
            operandsVisited := 0
            rowsConstructed := 0
          }
        sharedOperandsVisited := 0
      }
  | .failed error :: _ =>
      {
        existsResult :=
          {
            outcome := .failure error
            operandsVisited := 1
            rowsConstructed := 0
          }
        countResult :=
          {
            outcome := .failure error
            operandsVisited := 1
            rowsConstructed := 0
          }
        rowsResult :=
          {
            outcome := .failure error
            operandsVisited := 1
            rowsConstructed := 0
          }
        sharedOperandsVisited := 1
      }
  | .rejected :: rest =>
      let tail := runFused rest
      {
        existsResult := tail.existsResult.visit
        countResult := tail.countResult.visit
        rowsResult := tail.rowsResult.visit
        sharedOperandsVisited := tail.sharedOperandsVisited + 1
      }
  | .admitted [] :: rest =>
      let tail := runFused rest
      {
        existsResult := tail.existsResult.visit
        countResult := prependCount 0 tail.countResult
        rowsResult := prependRows [] tail.rowsResult
        sharedOperandsVisited := tail.sharedOperandsVisited + 1
      }
  | .admitted rows@(_ :: _) :: rest =>
      let tail := runFused rest
      {
        existsResult :=
          {
            outcome := .success true .settled
            operandsVisited := 1
            rowsConstructed := 0
          }
        countResult := prependCount rows.length tail.countResult
        rowsResult := prependRows rows tail.rowsResult
        sharedOperandsVisited := tail.sharedOperandsVisited + 1
      }

theorem runFused_exists_eq_runExists
    (source : List (Operand Row Error)) :
    (runFused source).existsResult = runExists source := by
  induction source with
  | nil => rfl
  | cons operand rest inductionHypothesis =>
      cases operand with
      | rejected =>
          simp [runFused, runExists, inductionHypothesis]
      | admitted rows =>
          cases rows with
          | nil =>
              simp [runFused, runExists, inductionHypothesis]
          | cons row rows =>
              rfl
      | failed error =>
          rfl

theorem runFused_count_eq_runCount
    (source : List (Operand Row Error)) :
    (runFused source).countResult = runCount source := by
  induction source with
  | nil => rfl
  | cons operand rest inductionHypothesis =>
      cases operand with
      | rejected =>
          simp [
            runFused,
            runCount,
            Execution.visit,
            inductionHypothesis
          ]
      | admitted rows =>
          cases rows with
          | nil =>
              cases outcome : (runCount rest).outcome <;>
                simp [
                  runFused,
                  runCount,
                  prependCount,
                  outcome,
                  inductionHypothesis
                ]
          | cons row rows =>
              cases outcome : (runCount rest).outcome <;>
                simp [
                  runFused,
                  runCount,
                  prependCount,
                  outcome,
                  inductionHypothesis
                ]
      | failed error =>
          rfl

theorem runFused_rows_eq_runRows
    (source : List (Operand Row Error)) :
    (runFused source).rowsResult = runRows source := by
  induction source with
  | nil => rfl
  | cons operand rest inductionHypothesis =>
      cases operand with
      | rejected =>
          simp [runFused, runRows, inductionHypothesis]
      | admitted rows =>
          cases rows with
          | nil =>
              simp [runFused, runRows, inductionHypothesis]
          | cons row rows =>
              simp [runFused, runRows, inductionHypothesis]
      | failed error =>
          rfl

theorem runFused_shared_eq_count_visits
    (source : List (Operand Row Error)) :
    (runFused source).sharedOperandsVisited =
      (runCount source).operandsVisited := by
  induction source with
  | nil => rfl
  | cons operand rest inductionHypothesis =>
      cases operand with
      | rejected =>
          simp [
            runFused,
            runCount,
            Execution.visit,
            inductionHypothesis
          ]
      | admitted rows =>
          cases rows with
          | nil =>
              cases outcome : (runCount rest).outcome <;>
                simp [
                  runFused,
                  runCount,
                  prependCount,
                  outcome,
                  inductionHypothesis
                ]
          | cons row rows =>
              cases outcome : (runCount rest).outcome <;>
                simp [
                  runFused,
                  runCount,
                  prependCount,
                  outcome,
                  inductionHypothesis
                ]
      | failed error =>
          rfl

theorem runFused_shared_le_independent_visits
    (source : List (Operand Row Error)) :
    (runFused source).sharedOperandsVisited ≤
      (runExists source).operandsVisited
        + (runCount source).operandsVisited
        + (runRows source).operandsVisited := by
  rw [runFused_shared_eq_count_visits]
  omega

theorem runExists_constructs_no_rows
    (source : List (Operand Row Error)) :
    (runExists source).rowsConstructed = 0 := by
  induction source with
  | nil => rfl
  | cons operand rest inductionHypothesis =>
      cases operand with
      | rejected =>
          simp [runExists, Execution.visit, inductionHypothesis]
      | admitted rows =>
          cases rows with
          | nil =>
              simp [runExists, Execution.visit, inductionHypothesis]
          | cons row rows =>
              rfl
      | failed error =>
          rfl

theorem runCount_constructs_no_rows
    (source : List (Operand Row Error)) :
    (runCount source).rowsConstructed = 0 := by
  induction source with
  | nil => rfl
  | cons operand rest inductionHypothesis =>
      cases operand with
      | rejected =>
          simp [runCount, Execution.visit, inductionHypothesis]
      | admitted rows =>
          cases outcome : (runCount rest).outcome <;>
            simpa [runCount, prependCount, outcome] using inductionHypothesis
      | failed error =>
          rfl

theorem settledExists_ignores_later_failure
    (firstRows : List Row)
    (first : Row)
    (error : Error)
    (tail : List (Operand Row Error)) :
    runExists
        (.admitted (first :: firstRows) :: .failed error :: tail) =
      {
        outcome := .success true .settled
        operandsVisited := 1
        rowsConstructed := 0
      } := by
  rfl

theorem operandAtomicCount
    (rows : List Row)
    (tail : List (Operand Row Error)) :
    runCount (.admitted rows :: tail) =
      prependCount rows.length (runCount tail) := by
  rfl

inductive Terminal where
  | exists
  | count
  | rows
  deriving Repr, DecidableEq

inductive PlannedExecution (Row Error : Type) where
  | exists (execution : Execution Bool Error)
  | count (execution : Execution Nat Error)
  | rows (execution : Execution (List Row) Error)
  deriving Repr, DecidableEq

def runPlanned
    (terminal : Terminal)
    (source : List (Operand Row Error)) :
    PlannedExecution Row Error :=
  match terminal with
  | .exists => .exists (runExists source)
  | .count => .count (runCount source)
  | .rows => .rows (runRows source)

theorem plannedExists_selects_once
    (source : List (Operand Row Error)) :
    runPlanned .exists source = .exists (runExists source) := by
  rfl

theorem plannedCount_selects_once
    (source : List (Operand Row Error)) :
    runPlanned .count source = .count (runCount source) := by
  rfl

theorem plannedRows_selects_once
    (source : List (Operand Row Error)) :
    runPlanned .rows source = .rows (runRows source) := by
  rfl

end BodyUseTerminalFolding
