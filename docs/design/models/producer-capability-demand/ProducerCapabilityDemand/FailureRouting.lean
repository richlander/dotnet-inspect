/-!
# Shared provision failure routing

`ProducerCapabilityResultSetValidator.ValidateSharedProvisionFailures` collects
every provision that some result reports as failed. The rule it shipped with,
`sharedFailuresAccepted`, required every result whose satisfaction provision
depends on such a provision `f` to report exactly `ProvisionFailed(f)`. When
two failed provisions share a dependent, that dependent would have to name
both, so no result set is accepted
([#9484](https://github.com/richlander/dotnet-inspect/issues/9484)).

The validator now implements `anyFailureAccepted`: a result that depends on
any reported failure reports a failure of some provision it depends on. In C#
the dependency half of `reportsDependency` is the separate
`UnrelatedProvisionFailure` check. The owning design states that a failure
affects every dependent, settled or not, so the rule needs no settlement
evidence.
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

/-! ## Loosened rule: report any failed dependency

`anyFailureAccepted` replaces "report exactly `ProvisionFailed f`" with
"report `ProvisionFailed g` for some failed provision `g` the result depends
on". The reporter of `g` is the result itself, so `g` is always a reported
failure.
-/

/-- The result reports a failure of a provision it depends on. -/
def Result.reportsDependency (dependsOn : Nat → Nat → Bool) (r : Result) : Bool :=
  match r.outcome with
  | .provisionFailed g => dependsOn r.provision g
  | _ => false

/-- The loosened shared-failure rule. -/
def anyFailureAccepted (dependsOn : Nat → Nat → Bool)
    (results : List Result) : Bool :=
  results.all fun reporter =>
    match reporter.outcome with
    | .provisionFailed f =>
        results.all fun r =>
          !dependsOn r.provision f || r.reportsDependency dependsOn
    | _ => true

/-- The provisions some result reports as failed. -/
def reportedFailed (results : List Result) (f : Nat) : Prop :=
  ∃ r ∈ results, r.outcome = .provisionFailed f

/--
Soundness: under the loosened rule, a result that depends on any reported
failure never reports `completed` or `requirementFailed`; it reports a failure
of a provision it depends on. This is the property the shared check exists
to protect: failure never becomes a success-shaped value.
-/
theorem any_sound (dependsOn : Nat → Nat → Bool) (results : List Result)
    (hacc : anyFailureAccepted dependsOn results = true)
    {r : Result} {f : Nat} (hr : r ∈ results)
    (hf : reportedFailed results f) (hd : dependsOn r.provision f = true) :
    ∃ g, r.outcome = .provisionFailed g ∧ dependsOn r.provision g = true := by
  obtain ⟨reporter, hrep, hout⟩ := hf
  unfold anyFailureAccepted at hacc
  rw [List.all_eq_true] at hacc
  have c := hacc reporter hrep
  rw [hout, List.all_eq_true] at c
  have e := c r hr
  simp only [hd, Bool.not_true, Bool.false_or] at e
  unfold Result.reportsDependency at e
  split at e
  · rename_i g hg
    exact ⟨g, hg, e⟩
  · cases e

/--
Completeness: every result set in which each result that depends on a
reported failure reports some failure it depends on is accepted. Such a report
exists for every dependent, since the failure it depends on is one choice.
-/
theorem any_complete (dependsOn : Nat → Nat → Bool) (results : List Result)
    (h : ∀ r ∈ results, ∀ f, reportedFailed results f →
      dependsOn r.provision f = true →
      ∃ g, r.outcome = .provisionFailed g ∧ dependsOn r.provision g = true) :
    anyFailureAccepted dependsOn results = true := by
  unfold anyFailureAccepted
  rw [List.all_eq_true]
  intro reporter hrep
  split
  · rename_i f hf
    rw [List.all_eq_true]
    intro r hr
    cases hd : dependsOn r.provision f
    · rfl
    · obtain ⟨g, hg, hdg⟩ := h r hr f ⟨reporter, hrep, hf⟩ hd
      simp [Result.reportsDependency, hg, hdg]
  · rfl

/-- The current rule implies the loosened rule when every reported failure
is a provision its reporter depends on, which `UnrelatedProvisionFailure`
already enforces. -/
theorem exact_implies_any (dependsOn : Nat → Nat → Bool) (results : List Result)
    (hrefl : ∀ r ∈ results, ∀ g, r.outcome = .provisionFailed g →
      dependsOn r.provision g = true)
    (hacc : sharedFailuresAccepted dependsOn results = true) :
    anyFailureAccepted dependsOn results = true := by
  apply any_complete
  intro r hr f hf hd
  obtain ⟨reporter, hrep, hout⟩ := hf
  unfold sharedFailuresAccepted at hacc
  rw [List.all_eq_true] at hacc
  have c := hacc reporter hrep
  rw [hout, List.all_eq_true] at c
  have e := c r hr
  simp only [hd, Bool.not_true, Bool.false_or, beq_iff_eq] at e
  exact ⟨f, e, hrefl r hr f e⟩

/-- The two-failure execution that the current rule rejects for every outcome
is accepted under the loosened rule when the common dependent names either
failure. -/
example :
    anyFailureAccepted demoDependsOn
      [⟨0, .provisionFailed 0⟩, ⟨1, .provisionFailed 1⟩, ⟨2, .provisionFailed 0⟩]
      = true := by
  decide

example :
    anyFailureAccepted demoDependsOn
      [⟨0, .provisionFailed 0⟩, ⟨1, .provisionFailed 1⟩, ⟨2, .provisionFailed 1⟩]
      = true := by
  decide

/-- The loosened rule still rejects a dependent that reports success. -/
example :
    anyFailureAccepted demoDependsOn
      [⟨0, .provisionFailed 0⟩, ⟨1, .provisionFailed 1⟩, ⟨2, .completed⟩]
      = false := by
  decide

/-! ## Settled dependents

A result cannot show when it settled, so no rule over outcomes alone can tell a
dependent that settled before the failure from one that wrongly reports
success. The owning design therefore makes a provision failure affect every
dependent, settled or not; both rules reject a settled dependent's success.
`SharedTraversal` concerns operand failures inside one traversal, not a
provision reported as failed.
-/

/-- A dependent that settled before its provision failed and kept its value is
rejected by the current rule. -/
theorem exact_rejects_settled_dependent :
    sharedFailuresAccepted demoDependsOn
      [⟨0, .provisionFailed 0⟩, ⟨2, .completed⟩] = false := by
  decide

/-- The loosened rule rejects it too. -/
theorem any_rejects_settled_dependent :
    anyFailureAccepted demoDependsOn
      [⟨0, .provisionFailed 0⟩, ⟨2, .completed⟩] = false := by
  decide

end FailureRouting

end ProducerCapabilityDemand
