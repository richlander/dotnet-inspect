/-!
# Owner-defined demand join

`query-space-producer-capabilities.md#requirement` requires an owner-defined
demand join to be monotone, commutative, and idempotent, so requirement order
cannot change the selected plan and adding a requirement cannot reduce the
admitted demand.

This module states that algebra once, proves the planning consequences for
every requirement list, and instantiates the Method-body Access and Detail
facets from the reference planner in
`tests/DotnetInspector.PerformanceOracles/MethodBodyAnalyzerPlanningPrototype.cs`.
-/

namespace ProducerCapabilityDemand

/-- An owner-defined demand algebra: a join-semilattice. -/
class DemandJoin (α : Type) where
  join : α → α → α
  join_comm : ∀ a b, join a b = join b a
  join_assoc : ∀ a b c, join (join a b) c = join a (join b c)
  join_idem : ∀ a, join a a = a

namespace DemandJoin

variable {α : Type} [DemandJoin α]

/-- `a` is admitted by `b` when joining `a` adds nothing to `b`. -/
def Admits (b a : α) : Prop := join a b = b

/--
The joined demand of a non-empty requirement list, accumulated left to right
from the first requirement as the reference planner does.
-/
def joined (first : α) (rest : List α) : α :=
  rest.foldl join first

theorem foldl_pull (x a : α) (l : List α) :
    join x (l.foldl join a) = l.foldl join (join x a) := by
  induction l generalizing a with
  | nil => rfl
  | cons y l ih =>
      simp only [List.foldl]
      rw [ih, join_assoc]

theorem foldl_perm {l₁ l₂ : List α} (h : l₁.Perm l₂) (a : α) :
    l₁.foldl join a = l₂.foldl join a := by
  induction h generalizing a with
  | nil => rfl
  | cons x _ ih => exact ih _
  | swap x y l =>
      simp only [List.foldl]
      rw [join_assoc, join_comm y x, ← join_assoc]
  | trans _ _ ih₁ ih₂ => exact (ih₁ a).trans (ih₂ a)

/-- The accumulator is admitted by the joined result. -/
theorem admits_start (a : α) (l : List α) : Admits (l.foldl join a) a := by
  unfold Admits
  rw [foldl_pull, join_idem]

/-- Every listed requirement is admitted by the joined result. -/
theorem admits_member {x : α} {l : List α} (hx : x ∈ l) (a : α) :
    Admits (l.foldl join a) x := by
  unfold Admits
  obtain ⟨s, t, rfl⟩ := List.append_of_mem hx
  rw [foldl_perm List.perm_middle a]
  simp only [List.foldl]
  rw [foldl_pull, ← join_assoc, join_comm x a, join_assoc, join_idem]

/-- A list's fold does not depend on which of its members starts it. -/
theorem foldl_start_irrelevant {a b : α} {l : List α}
    (ha : a ∈ l) (hb : b ∈ l) :
    l.foldl join a = l.foldl join b := by
  have hab := admits_member ha b
  have hba := admits_member hb a
  unfold Admits at hab hba
  rw [foldl_pull] at hab hba
  rw [← hba, ← hab, join_comm]

theorem joined_eq_foldl (first : α) (rest : List α) :
    joined first rest = (first :: rest).foldl join first := by
  simp only [joined, List.foldl, join_idem]

/-- Requirement order cannot change the joined demand. -/
theorem joined_perm {first₁ first₂ : α} {rest₁ rest₂ : List α}
    (h : (first₁ :: rest₁).Perm (first₂ :: rest₂)) :
    joined first₁ rest₁ = joined first₂ rest₂ := by
  rw [joined_eq_foldl, joined_eq_foldl, foldl_perm h]
  exact foldl_start_irrelevant
    (h.subset (List.mem_cons_self ..)) (List.mem_cons_self ..)

/-- Every requirement in a request set is admitted by its joined demand. -/
theorem joined_admits_each {first : α} {rest : List α} {x : α}
    (hx : x ∈ first :: rest) : Admits (joined first rest) x := by
  unfold joined
  cases hx with
  | head => exact admits_start first rest
  | tail _ h => exact admits_member h first

/-- Adding requirements cannot reduce the admitted demand. -/
theorem joined_monotone (first : α) (rest more : List α) :
    Admits (joined first (rest ++ more)) (joined first rest) := by
  unfold joined
  rw [List.foldl_append]
  exact admits_start _ _

/-- Admission is transitive. -/
theorem admits_trans {a b c : α} (hab : Admits b a) (hbc : Admits c b) :
    Admits c a := by
  unfold Admits at *
  rw [← hbc, ← join_assoc, hab]

end DemandJoin

/-! ## Method-body instruction demand -/

inductive Access where
  | forwardOnly
  | retainedPrefix
  deriving DecidableEq, Repr

inductive Detail where
  | opcodeAndExtent
  | selectiveOperands
  deriving DecidableEq, Repr

def Access.max : Access → Access → Access
  | .forwardOnly, a => a
  | .retainedPrefix, _ => .retainedPrefix

def Detail.max : Detail → Detail → Detail
  | .opcodeAndExtent, d => d
  | .selectiveOperands, _ => .selectiveOperands

instance : DemandJoin Access where
  join := Access.max
  join_comm := by intro a b; cases a <;> cases b <;> rfl
  join_assoc := by intro a b c; cases a <;> cases b <;> cases c <;> rfl
  join_idem := by intro a; cases a <;> rfl

instance : DemandJoin Detail where
  join := Detail.max
  join_comm := by intro a b; cases a <;> cases b <;> rfl
  join_assoc := by intro a b c; cases a <;> cases b <;> cases c <;> rfl
  join_idem := by intro a; cases a <;> rfl

/-- Independent facets compose pointwise. -/
instance {α β : Type} [DemandJoin α] [DemandJoin β] : DemandJoin (α × β) where
  join x y := (DemandJoin.join x.1 y.1, DemandJoin.join x.2 y.2)
  join_comm := by
    intro a b
    simp only [DemandJoin.join_comm a.1, DemandJoin.join_comm a.2]
  join_assoc := by
    intro a b c
    simp only [DemandJoin.join_assoc]
  join_idem := by
    intro a
    simp only [DemandJoin.join_idem]

/-- One analyzer's minimum Access and Detail. -/
abbrev MethodDemand := Access × Detail

inductive InstructionSource where
  | noRetentionStream
  | lazyRetainedSequence
  deriving DecidableEq, Repr

/-- Whether a physical source can host an analyzer with this demand. -/
def InstructionSource.supports : InstructionSource → MethodDemand → Bool
  | .noRetentionStream, (a, d) => a == .forwardOnly && d == .opcodeAndExtent
  | .lazyRetainedSequence, _ => true

/-- The reference planner's source choice for one joined demand. -/
def selectSource (demand : MethodDemand) : InstructionSource :=
  if InstructionSource.noRetentionStream.supports demand then
    .noRetentionStream
  else
    .lazyRetainedSequence

/-- The reference planner over a non-empty analyzer list. -/
def planSource (first : MethodDemand) (rest : List MethodDemand) :
    InstructionSource :=
  selectSource (DemandJoin.joined first rest)

theorem supports_downward {s : InstructionSource} {d x : MethodDemand}
    (hs : s.supports d = true) (hx : DemandJoin.Admits d x) :
    s.supports x = true := by
  obtain ⟨da, dd⟩ := d
  obtain ⟨xa, xd⟩ := x
  cases s <;> cases da <;> cases dd <;> cases xa <;> cases xd <;>
    simp_all [InstructionSource.supports, DemandJoin.Admits,
      DemandJoin.join, Access.max, Detail.max]

/-- The selected source hosts every requested analyzer. -/
theorem planSource_supports_each {first : MethodDemand}
    {rest : List MethodDemand} {x : MethodDemand}
    (hx : x ∈ first :: rest) :
    (planSource first rest).supports x = true := by
  have hadm := DemandJoin.joined_admits_each hx
  unfold planSource selectSource
  split
  · exact supports_downward (by assumption) hadm
  · rfl

/-- Analyzer order cannot change the selected source. -/
theorem planSource_perm {first₁ first₂ : MethodDemand}
    {rest₁ rest₂ : List MethodDemand}
    (h : (first₁ :: rest₁).Perm (first₂ :: rest₂)) :
    planSource first₁ rest₁ = planSource first₂ rest₂ := by
  unfold planSource
  rw [DemandJoin.joined_perm h]

theorem stream_supports_foldl {l : List MethodDemand} :
    ∀ acc : MethodDemand,
      InstructionSource.noRetentionStream.supports acc = true →
      (∀ x ∈ l, InstructionSource.noRetentionStream.supports x = true) →
      InstructionSource.noRetentionStream.supports (l.foldl DemandJoin.join acc)
        = true := by
  induction l with
  | nil => intro acc hacc _; exact hacc
  | cons y ys ih =>
      intro acc hacc hall
      simp only [List.foldl]
      apply ih
      · have hy := hall y (List.mem_cons_self ..)
        obtain ⟨aa, ad⟩ := acc
        obtain ⟨ya, yd⟩ := y
        cases aa <;> cases ad <;> cases ya <;> cases yd <;>
          simp_all [InstructionSource.supports, DemandJoin.join,
            Access.max, Detail.max]
      · intro x hx; exact hall x (List.mem_cons_of_mem _ hx)

/--
Singleton specialization, generalized: when every analyzer can run on the
no-retention stream, the planner selects it rather than a richer source.
-/
theorem planSource_minimal {first : MethodDemand} {rest : List MethodDemand}
    (h : ∀ x ∈ first :: rest,
      InstructionSource.noRetentionStream.supports x = true) :
    planSource first rest = .noRetentionStream := by
  have hs := stream_supports_foldl first (h first (List.mem_cons_self ..))
    (fun x hx => h x (List.mem_cons_of_mem _ hx))
  unfold planSource selectSource DemandJoin.joined
  simp [hs]

end ProducerCapabilityDemand
