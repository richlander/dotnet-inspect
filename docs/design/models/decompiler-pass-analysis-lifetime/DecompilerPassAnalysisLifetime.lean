/-!
# Decompiler pass analysis lifetime

`decompiler-pass-analysis-lifetime.md` requires a manager-owned analysis result
to remain reusable only while every intervening pass preserves it. This module
models one analysis identity and its conservative generation.
-/

namespace DecompilerPassAnalysisLifetime

inductive Cache where
  | empty
  | ready (generation : Nat)
  deriving DecidableEq, Repr

structure Manager where
  generation : Nat
  cache : Cache
  deriving DecidableEq, Repr

inductive Acquisition where
  | constructed
  | reused
  deriving DecidableEq, Repr

def initial : Manager :=
  { generation := 0, cache := .empty }

/-- A cached result is current exactly when it belongs to the manager generation. -/
def Current (manager : Manager) : Prop :=
  manager.cache = .ready manager.generation

/-- Prepare one consumer: reuse a current result or construct one for this generation. -/
def acquire (manager : Manager) : Manager × Acquisition :=
  if manager.cache = .ready manager.generation then
    (manager, .reused)
  else
    ({ manager with cache := .ready manager.generation }, .constructed)

/--
Complete one pass. Preservation keeps the result and its generation;
invalidation clears it and advances the generation.
-/
def complete (manager : Manager) (preserves : Bool) : Manager :=
  if preserves then
    manager
  else
    { generation := manager.generation + 1, cache := .empty }

def completeAll (manager : Manager) (preservations : List Bool) : Manager :=
  preservations.foldl complete manager

theorem acquire_current (manager : Manager) :
    Current (acquire manager).1 := by
  unfold acquire Current
  split <;> simp_all

theorem acquire_reuses_current {manager : Manager}
    (current : Current manager) :
    acquire manager = (manager, .reused) := by
  unfold acquire Current at *
  simp [current]

theorem acquire_constructs_missing {manager : Manager}
    (missing : manager.cache ≠ .ready manager.generation) :
    acquire manager =
      ({ manager with cache := .ready manager.generation }, .constructed) := by
  unfold acquire
  simp [missing]

theorem complete_preserving_keeps_current {manager : Manager}
    (current : Current manager) :
    Current (complete manager true) := by
  simpa [complete] using current

theorem complete_invalidating_clears (manager : Manager) :
    (complete manager false).cache = .empty := by
  simp [complete]

theorem complete_invalidating_advances (manager : Manager) :
    (complete manager false).generation = manager.generation + 1 := by
  simp [complete]

theorem preserving_sequence_keeps_current {manager : Manager}
    (current : Current manager) (preservations : List Bool)
    (allPreserve : ∀ value ∈ preservations, value = true) :
    Current (completeAll manager preservations) := by
  induction preservations generalizing manager with
  | nil => simpa [completeAll] using current
  | cons head tail ih =>
      have headTrue : head = true := allPreserve head (by simp)
      have tailTrue : ∀ value ∈ tail, value = true := by
        intro value member
        exact allPreserve value (by simp [member])
      simp only [completeAll, List.foldl]
      apply ih
      · simpa [headTrue, complete] using current
      · exact tailTrue

theorem invalidation_requires_construction (manager : Manager) :
    (acquire (complete manager false)).2 = .constructed := by
  simp [complete, acquire]

/--
The last pass alone is not a sound preservation rule: an invalidating pass
followed by a preserving pass leaves the original result unavailable.
-/
theorem final_preservation_is_insufficient {manager : Manager}
    (current : Current manager) :
    let after := completeAll manager [false, true]
    [false, true].getLast? = some true ∧
      after.cache = .empty ∧
      manager.cache = .ready manager.generation ∧
      after.generation = manager.generation + 1 := by
  simpa [completeAll, complete, Current] using current

#print axioms acquire_current
#print axioms preserving_sequence_keeps_current
#print axioms invalidation_requires_construction
#print axioms final_preservation_is_insufficient

end DecompilerPassAnalysisLifetime
