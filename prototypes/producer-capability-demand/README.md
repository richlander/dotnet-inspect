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
| [#9483](https://github.com/richlander/dotnet-inspect/issues/9483) | `Coverage.lastEdge_unsound_preserving` | Path validation takes `ExactCardinality` from the last covering edge only. Under the enum's documented "preserves" reading, a non-exact provision plus an exact edge satisfies an exact Count. |
| [#9484](https://github.com/richlander/dotnet-inspect/issues/9484) | `FailureRouting.shared_dependent_of_two_failures_rejected` | When two failed provisions share a dependent, no result set is accepted, whatever the dependent reports. |

The current Package Tree and section-row adopters do not reach either shape.

## Abstractions and non-claims

- The `Coverage` key abstracts scope, capability, completion, and outcome
  identities compared by reference equality. Completion is compared exactly,
  as in C#, even though the design text says "at least".
- Producer coverage and provision truth are hypotheses, matching the design's
  statement that the producer remains responsible for its declarations.
- Requirement-set construction, domain and resource identity checks,
  dependency ordering, and cycle detection are not modeled.
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
