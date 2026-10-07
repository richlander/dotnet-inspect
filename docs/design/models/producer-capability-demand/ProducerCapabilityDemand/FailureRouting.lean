/-!
# Shared provision failure routing

`ProducerCapabilityResultSetValidator.ValidateSharedProvisionFailures` collects
every provision that some result reports as failed. For each such provision
`f`, every result whose satisfaction provision depends on `f` must report
exactly `ProvisionFailed(f)`.

The design requires a producer failure to affect "every unsettled requirement
whose chosen path depends on that provision" with a typed failure that names
the failed provision. It does not say that one execution can fail at most one
provision.

When two failed provisions share a dependent, that dependent would have to
name both, so no result set is accepted. A provision with two independent
dependencies that both fail therefore has no representable outcome.
-/

namespace ProducerCapabilityDemand

namespace FailureRouting

/-- A result outcome, with provision identities as natural numbers. -/
inductive Outcome where
  | completed
  | provisionFailed (provision : Nat)
  | requirementFailed
  deriving DecidableEq, Repr

/-- One result: its satisfaction provision and reported outcome. -/
structure Result where
  provision : Nat
  outcome : Outcome
  deriving DecidableEq, Repr

def Result.reportsFailed (r : Result) (f : Nat) : Bool :=
  r.outcome == .provisionFailed f

/--
The validator's shared-failure rule over an arbitrary reflexive-transitive
dependency relation `dependsOn provision dependency`.
-/
def sharedFailuresAccepted (dependsOn : Nat → Nat → Bool)
    (results : List Result) : Bool :=
  results.all fun reporter =>
    match reporter.outcome with
    | .provisionFailed f =>
        results.all fun r =>
          !dependsOn r.provision f || r.outcome == .provisionFailed f
    | _ => true

/--
Two distinct reported provision failures with a common dependent always reject
the result set, whatever that dependent reports.
-/
theorem shared_dependent_of_two_failures_rejected
    (dependsOn : Nat → Nat → Bool) (results : List Result)
    {r₁ r₂ r : Result} {f₁ f₂ : Nat}
    (h₁ : r₁ ∈ results) (h₂ : r₂ ∈ results) (hr : r ∈ results)
    (hf₁ : r₁.outcome = .provisionFailed f₁)
    (hf₂ : r₂.outcome = .provisionFailed f₂)
    (hne : f₁ ≠ f₂)
    (hd₁ : dependsOn r.provision f₁ = true)
    (hd₂ : dependsOn r.provision f₂ = true) :
    sharedFailuresAccepted dependsOn results = false := by
  apply Bool.eq_false_iff.mpr
  intro hacc
  unfold sharedFailuresAccepted at hacc
  rw [List.all_eq_true] at hacc
  have c₁ := hacc r₁ h₁
  have c₂ := hacc r₂ h₂
  rw [hf₁, List.all_eq_true] at c₁
  rw [hf₂, List.all_eq_true] at c₂
  have e₁ := c₁ r hr
  have e₂ := c₂ r hr
  simp only [hd₁, hd₂, Bool.not_true, Bool.false_or, beq_iff_eq] at e₁ e₂
  rw [e₁] at e₂
  exact hne (Outcome.provisionFailed.inj e₂)

/-! ## Concrete instance

Provisions `0` and `1` are independent; provision `2` depends on both. One
execution fails both `0` and `1`.
-/

def demoDependsOn : Nat → Nat → Bool
  | p, d => p == d || (p == 2 && (d == 0 || d == 1))

/--
Every outcome the dependent requirement could report is rejected, including
each design-legitimate typed failure naming one of its failed dependencies.
-/
theorem demo_every_outcome_rejected (o : Outcome) :
    sharedFailuresAccepted demoDependsOn
      [⟨0, .provisionFailed 0⟩, ⟨1, .provisionFailed 1⟩, ⟨2, o⟩] = false :=
  shared_dependent_of_two_failures_rejected demoDependsOn _
    (r₁ := ⟨0, .provisionFailed 0⟩) (r₂ := ⟨1, .provisionFailed 1⟩)
    (r := ⟨2, o⟩) (by simp) (by simp) (by simp) rfl rfl (by decide)
    rfl rfl

/-- The single-failure case is representable. -/
example :
    sharedFailuresAccepted demoDependsOn
      [⟨0, .provisionFailed 0⟩, ⟨1, .completed⟩, ⟨2, .provisionFailed 0⟩]
      = true := by
  decide

end FailureRouting

end ProducerCapabilityDemand
