# Producer-capability demand Lean pilot

This Lean 4 prototype is proof evidence for
[issue #9482](https://github.com/richlander/dotnet-inspect/issues/9482). The
normative owner remains
[`query-space-producer-capabilities.md`](../../docs/design/query-space-producer-capabilities.md).
This prototype changes no product contract or runtime path.

## Why Lean here

Producer-capability planning is deterministic construction over immutable
data, so it has no state machine for TLA+ to explore. Its correctness claims
are laws over arbitrary requirement sets, analyzer lists, covering paths, and
inputs. The existing Release gates check chosen examples. TLC would check one
finite instance. These theorems hold for every instance.

## Proven claims

| Design claim | Theorem |
| --- | --- |
| The demand join is order-independent | `DemandJoin.joined_perm` |
| Every requirement is admitted by the joined demand | `DemandJoin.joined_admits_each` |
| Adding a requirement cannot reduce admitted demand | `DemandJoin.joined_monotone` |
| Independent facets join pointwise | `DemandJoin (α × β)` instance |
| The selected Method-body source hosts every analyzer | `planSource_supports_each` |
| Analyzer order cannot change the selected source | `planSource_perm` |
| The no-retention stream is kept whenever every analyzer allows it | `planSource_minimal` |
| Shared traversal preserves each participant's result and charge | `Analyzer.runAlone_both` |
| Shared traversal visits no more items than independent runs | `Analyzer.shared_visits_le_independent` |
| A settled `Exists` survives a later failure and stops being charged | `SharedTraversal` pathological example |
| Covering paths are sound when edges certify exactness absolutely | `Coverage.lastEdge_sound_absolute` |
| Conjunctive path properties are sound under either reading | `Coverage.conjunctive_sound_preserving`, `Coverage.conjunctive_sound_absolute` |
| Current adopters are unaffected by the choice of rule | `Coverage.rules_agree_when_all_exact` |

The shared-traversal analyzer is an arbitrary left-to-right step function with
its own settlement predicate. `Analyzer.both` is itself an analyzer, so the
two-participant theorem applies to any number of participants by nesting.
This replaces the right-recursive fold shape in
[`body-use-terminal-folding`](../body-use-terminal-folding/) with the loop
shape that a host executes.

## Findings

Attempting the validator soundness and completeness proofs exposed two latent
defects. Each was reproduced against the shipped C# validators.

| Issue | Lean witness | Consequence |
| --- | --- | --- |
| [#9483](https://github.com/richlander/dotnet-inspect/issues/9483) | `Coverage.lastEdge_unsound_preserving` | Path validation takes `ExactCardinality` from the last covering edge only. Under the enum's documented "preserves" reading, a sound provision without the claim plus a sound edge certifying it forms an accepted path whose Count is not exact. The theorem states soundness, acceptance, and the unsatisfied result together. |
| [#9484](https://github.com/richlander/dotnet-inspect/issues/9484) | `FailureRouting.shared_dependent_of_two_failures_rejected` | When two failed provisions share a dependent, no result set is accepted, whatever the dependent reports. |

The current Package Tree and section-row adopters do not reach either shape.

## Validator check classification

[#9508](https://github.com/richlander/dotnet-inspect/issues/9508) classifies
every `ProducerCapabilityPlanRejectionReason` by whether soundness needs it.
`ValidatorChecks` holds the proofs. The C# pruning they justify is tracked in
[#9509](https://github.com/richlander/dotnet-inspect/issues/9509).

| Reason | Class | Basis |
| --- | --- | --- |
| `DependencyCycle` | Redundant | `ordered_implies_acyclic`: `MissingDependency` plus `DependencyOrder` already exclude every cycle, so the cycle search can be deleted. The converse fails, so keep the order check. |
| `UnknownCapability` | Redundant; early diagnostic | `accepted_capability_is_known`: every accepted satisfaction's capability is a declared provision or coverage target. It is useful only for rejecting before a strategy is examined. |
| `ProducerDomainMismatch` on requirement scope, capability, completion, outcome | Redundant | `accepted_key_inherits`: reference equality with domain-checked declarations implies it. |
| `ProducerDomainMismatch` on requirement parameters, declarations, coverages | Required | Parameters are never compared with an offer, so this is their only check. Declaration and coverage checks are the premises the redundancy relies on. |
| `ResourceMismatch` on requirements and coverages | Required | They fix one resource and keep every covering edge on it. |
| `ResourceMismatch` on provision declarations | Narrowable | `accepted_provision_resource`: implied for every satisfaction-path provision. Still needed for dependency-only provisions, so it can be narrowed from every declaration to selected provisions. |
| `UnknownAssociation` | Diagnostic | `assemble_ignores_unknown`: extra satisfactions never reach the plan. It reports a producer planning bug. |
| `InsufficientCompletion` and exact edge-source completion | Loosenable | `atLeast_sound` and `walkExact_le_walkAtLeast`: "at least" is a strict loosening and is sound when the owner certifies a monotone completion order ([#9486](https://github.com/richlander/dotnet-inspect/issues/9486)). Without monotonicity it is unsound. |
| `DuplicateDeclaration`, `DuplicateAssociation` | Required, or structural | Duplicate identities make the validator's and executor's lookups disagree. A keyed-map input type would make them unrepresentable. |
| `UnknownProvision`, `MissingDependency`, `DependencyOrder`, `UnsatisfiedRequirement` | Required | Each starts, orders, or completes an executable path. |
| `InvalidCoveringPath`, `IncompatibleScope`, `CapabilityMismatch`, `OutcomeContractMismatch`, `RequiredPropertiesMissing` | Required | Premises of `Coverage.lastEdge_sound_absolute` and `Coverage.conjunctive_sound_preserving`. |
| `DuplicateProvision` | Policy | Not needed for result soundness. It enforces the design's "shared construction once". |
| `EmptyRequirementSet`, `UnknownStrategy` | Policy or diagnostic | An empty plan is trivially sound, and the strategy is only recorded. |
| `MissingValue`, `NoAdmittedStrategy` | Structural | These check null inputs. Non-nullable required members would remove `MissingValue`; `NoAdmittedStrategy` is the producer's typed "no plan" outcome. |

"Redundant", "Narrowable", "Diagnostic", and "Loosenable" rows are proven.
"Required" rows are premises of the soundness theorems. Apart from the
`DuplicateDeclaration` example, their necessity is argued from the identities
they distinguish, not proven by a counterexample for each check.

Validation runs once per plan, not per row, so pruning saves code rather than
runtime. The performance lever is the completion loosening, which lets one
stronger provision cover weaker requirements and therefore share
construction.

## Abstractions and non-claims

- The `Coverage` key abstracts scope, capability, completion, and outcome
  identities compared by reference equality. Completion is compared exactly,
  as in C#, even though the design text says "at least"
  ([#9486](https://github.com/richlander/dotnet-inspect/issues/9486)).
- Producer coverage and provision truth are hypotheses, matching the design's
  statement that the producer remains responsible for its declarations.
- Requirement-set construction, domain and resource identity checks,
  dependency ordering, and cycle detection are not modeled.
- `FailureRouting` does not model the C# filter that collects only failures
  naming a selected provision; the findings use only selected provisions.
- The Method-body planner is the reference planner in
  `tests/DotnetInspector.PerformanceOracles`; production adoption remains
  unverified, as the owning design states.
- No theorem establishes correspondence with the C# implementation. Each
  finding's C# reproduction is a separate manual check recorded in its issue.
- No CI job builds this project yet.

## Run

The prototype pins Lean 4.34.1 and has no package dependencies:

```bash
cd prototypes/producer-capability-demand
lake build
```

`lake build` fails on any unproven or ill-typed theorem. The key theorems
depend only on Lean's standard `propext` and `Quot.sound` axioms; check with
`#print axioms`.
